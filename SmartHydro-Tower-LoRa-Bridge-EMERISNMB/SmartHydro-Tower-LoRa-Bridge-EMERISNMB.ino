#include <Arduino.h>
#include "TelemetryReceiptFrame.h"
#include <vector>
#include "BridgeCommandQueue.h"
// ==============================================================================
// SMARTHYDRO VERTICAL TOWER LORA CO-PROCESSOR BRIDGE FIRMWARE
// ==============================================================================
// Target MCU:  Seeed Studio XIAO ESP32S3 + Wio-SX1262 B2B Expansion Board
// Role:        Sub-GHz 868.1 MHz LoRa P2P Transceiver Co-Processor for
//              DFRobot FireBeetle 2 ESP32-C6 Master Controller
// Interface:   Hardware UART1 Bridge (115,200 baud on D6=TX1/GPIO43, D7=RX1/GPIO44)
// RF Spec:     868.1 MHz | SF10 | BW 125.0 kHz | CR 4/7 | Sync 0x12 | +14 dBm
// ==============================================================================

#include <Arduino.h>
#include <SPI.h>
#include <RadioLib.h>
#include <ArduinoJson.h>
#include <esp_task_wdt.h>
#include "soc/soc.h"
#include "soc/rtc_cntl_reg.h"
#include "esp_err.h"
#include "esp_private/startup_internal.h"
#include "esp_private/brownout.h"
#include "Config.h"

// Early-boot brownout override (ESP-IDF 5.x) to prevent RF calibration brownout resets
static void IRAM_ATTR disableBridgeBrownoutHw() {
#ifdef RTC_CNTL_BROWN_OUT_ENA
  REG_CLR_BIT(RTC_CNTL_BROWN_OUT_REG, RTC_CNTL_BROWN_OUT_ENA);
#endif
#ifdef RTC_CNTL_BROWN_OUT_INT_ENA_M
  REG_CLR_BIT(RTC_CNTL_INT_ENA_REG, RTC_CNTL_BROWN_OUT_INT_ENA_M);
#endif
  WRITE_PERI_REG(RTC_CNTL_BROWN_OUT_REG, 0);
#ifdef RTC_CNTL_BROWN_OUT_INT_CLR_M
  WRITE_PERI_REG(RTC_CNTL_INT_CLR_REG, RTC_CNTL_BROWN_OUT_INT_CLR_M);
#endif
}

extern "C" void esp_brownout_init(void) {
  disableBridgeBrownoutHw();
}

extern "C" void esp_brownout_disable(void) {
  disableBridgeBrownoutHw();
}

ESP_SYSTEM_INIT_FN(early_bridge_boot_stabilize, CORE, BIT(0), 1) {
  disableBridgeBrownoutHw();
  return ESP_OK;
}

__attribute__((constructor(101))) static void earlyDisableBridgeBrownout() {
  disableBridgeBrownoutHw();
}

// Hardware SPI & Wio-SX1262 Radio Instance
static SPIClass LoRaSPI(FSPI);
static SX1262 radio = new Module(LORA_CS, LORA_DIO1, LORA_RST, LORA_BUSY, LoRaSPI);

// Hardware UART1 Bridge to FireBeetle 2 ESP32-C6
static HardwareSerial BridgeSerial(1);

// Runtime State
static bool g_loraOnline = false;
static volatile bool g_loraRxFlag = false;
static unsigned long g_txPacketCount = 0;
static unsigned long g_rxPacketCount = 0;
static unsigned long g_lastRfTxMs = 0;
static unsigned long g_lastRfRxActivityMs = 0;
static float g_lastRssi = 0.0f;
static float g_lastSnr = 0.0f;
static String g_uartRxBuffer = "";
static std::vector<String> g_pendingMasterFrames;
static SmartHydroAirtimeLedger g_bridgeAirtimeLedger;

// Interrupt Service Routine for Wio-SX1262 DIO1
#if defined(ESP8266) || defined(ESP32)
  ICACHE_RAM_ATTR
#endif
static void onLoraDio1Action() {
  g_loraRxFlag = true;
}

// ------------------------------------------------------------------------------
// Hardware Task Watchdog Timer (30 Seconds)
// ------------------------------------------------------------------------------
static void initBridgeWatchdog(uint32_t timeoutSeconds = WDT_TIMEOUT_SEC) {
#if defined(ESP_IDF_VERSION_VAL) && ESP_IDF_VERSION >= ESP_IDF_VERSION_VAL(5, 0, 0)
  esp_task_wdt_config_t wdt_config = {
      .timeout_ms = timeoutSeconds * 1000,
      .idle_core_mask = 0,
      .trigger_panic = true
  };
  if (esp_task_wdt_reconfigure(&wdt_config) != ESP_OK) {
    esp_task_wdt_init(&wdt_config);
  }
#else
  esp_task_wdt_init(timeoutSeconds, true);
#endif
  esp_task_wdt_add(NULL);
}

static inline void feedBridgeWatchdog() {
  esp_task_wdt_reset();
}

// ------------------------------------------------------------------------------
// Wio-SX1262 Initialization
// ------------------------------------------------------------------------------
static bool initWioSX1262() {
  Serial.println(F("[LoRa Bridge] Initializing Seeed Wio-SX1262 B2B Expansion Radio..."));

  // Configure RF Switch Control Pin on Wio-SX1262 B2B board
  pinMode(LORA_RF_SW, OUTPUT);
  digitalWrite(LORA_RF_SW, HIGH);

  // Initialize Hardware SPI on XIAO ESP32S3 (SCK=7, MISO=8, MOSI=9, NSS=41)
  LoRaSPI.begin(LORA_SCK, LORA_MISO, LORA_MOSI, LORA_CS);

  int state = radio.begin(
      LORA_FREQUENCY,
      LORA_BANDWIDTH,
      LORA_SPREAD_FACTOR,
      LORA_CODING_RATE,
      LORA_SYNC_WORD,
      LORA_OUTPUT_POWER,
      LORA_PREAMBLE_LEN,
      LORA_TCXO_VOLTAGE,
      false
  );

  if (state != RADIOLIB_ERR_NONE) {
    // Fallback if TCXO is externally clocked on specific board revision
    state = radio.begin(
        LORA_FREQUENCY,
        LORA_BANDWIDTH,
        LORA_SPREAD_FACTOR,
        LORA_CODING_RATE,
        LORA_SYNC_WORD,
        LORA_OUTPUT_POWER,
        LORA_PREAMBLE_LEN
    );
  }

  if (state == RADIOLIB_ERR_NONE) {
    radio.setRegulatorDCDC();
    radio.setDio2AsRfSwitch(true);
    radio.setCurrentLimit(60.0);
    radio.setOutputPower(LORA_OUTPUT_POWER);
    radio.setDio1Action(onLoraDio1Action);
    g_loraRxFlag = false;
    radio.startReceive();
    g_loraOnline = true;
    g_lastRfRxActivityMs = millis();
    Serial.printf("[LoRa Bridge] [SUCCESS] Wio-SX1262 Online at %.1f MHz (SF%d, BW %.1fkHz, CR 4/%d, +%ddBm)\n",
                  LORA_FREQUENCY, LORA_SPREAD_FACTOR, LORA_BANDWIDTH, LORA_CODING_RATE, LORA_OUTPUT_POWER);
    return true;
  }

  Serial.printf("[LoRa Bridge] [CRITICAL] Wio-SX1262 init failed with RadioLib error: %d\n", state);
  g_loraOnline = false;
  return false;
}

// ------------------------------------------------------------------------------
// Radiate Validated JSON Frame Over 868.1 MHz Wio-SX1262
// ------------------------------------------------------------------------------
static void radiateMasterFrameNow(String frame) {
  if (!g_loraOnline) {
    initWioSX1262();
    if (!g_loraOnline) return;
  }

  g_lastRfTxMs = millis();

  // Listen-Before-Talk (CAD / Channel Activity Detection)
  for (int retry = 0; retry < 3; retry++) {
    feedBridgeWatchdog();
    int cadState = radio.scanChannel();
    if (cadState == RADIOLIB_PREAMBLE_DETECTED) {
      unsigned long backoffMs = random(900, 1801);
      Serial.printf("[LoRa CAD] Channel busy! Backing off for %lu ms (attempt %d/3)...\n", backoffMs, retry + 1);
      delay(backoffMs);
    } else {
      break;
    }
  }

  digitalWrite(PIN_STATUS_LED, LOW); // Blink LED ON during RF TX
  StaticJsonDocument<768> packet;
  TelemetrySampleFrame packed;
  bool binary = deserializeJson(packet, frame) == DeserializationError::Ok && encodeTelemetrySample(packet.as<JsonObject>(), packed);
  size_t wireSize = binary ? sizeof(packed) : frame.length();
  uint32_t toaUs = (uint32_t)radio.getTimeOnAir(wireSize);
  uint16_t toaMs = (toaUs > 0) ? (uint16_t)((toaUs + 999UL) / 1000UL)
                               : (uint16_t)shCalculateLoRaToaMs(wireSize, LORA_SPREAD_FACTOR, LORA_BANDWIDTH, LORA_CODING_RATE, LORA_PREAMBLE_LEN);
  uint8_t prio = (frame.indexOf("\"cack\"") != -1 || frame.indexOf("\"cid\"") != -1) ? SH_PRIO_CRITICAL_ACK : SH_PRIO_TELEMETRY;
  if (!g_bridgeAirtimeLedger.canTransmit(toaMs, prio)) {
    digitalWrite(PIN_STATUS_LED, HIGH);
    Serial.printf("[ICASA Guard] Bridge skipping TX (%u ms) - 1%% hourly airtime budget reached (%lu ms used)\n",
                  (unsigned int)toaMs, (unsigned long)g_bridgeAirtimeLedger.getUsedAirtimeMs());
    return;
  }

  g_txPacketCount++;
  Serial.printf("[LoRa TX #%lu] >>> Radiating %d bytes from C6 Master (ToA ~%u ms, 1h Airtime %lu/%lu ms):\n%s\n",
                g_txPacketCount, wireSize, (unsigned int)toaMs,
                (unsigned long)g_bridgeAirtimeLedger.getUsedAirtimeMs(),
                (unsigned long)ICASA_MAX_AIRTIME_MS_PER_HOUR, frame.c_str());

  // Enable RF switch RX/TX path and transmit
  digitalWrite(LORA_RF_SW, LOW);
  int state = binary ? radio.transmit((uint8_t*)&packed, sizeof(packed)) : radio.transmit(frame);
  digitalWrite(LORA_RF_SW, HIGH); // Restore RX path on Wio-SX1262

  g_loraRxFlag = false;
  g_lastRfRxActivityMs = millis();
  radio.startReceive();
  digitalWrite(PIN_STATUS_LED, HIGH);

  if (state == RADIOLIB_ERR_NONE) {
    g_bridgeAirtimeLedger.recordTransmission(toaMs);
    Serial.println(F("[LoRa TX] [OK] Packet radiated over 868.1 MHz. Listening for Gateway ACK/Downlink..."));
  } else {
    Serial.printf("[LoRa TX] [FAIL] RadioLib transmit error: %d\n", state);
  }
}

// ------------------------------------------------------------------------------
// Transmit or Latch Compact JSON Frame Received over UART1 from FireBeetle 2 ESP32-C6
// ------------------------------------------------------------------------------
static void handleMasterUartFrame(String frame) {
  frame.trim();
  if (frame.length() == 0) return;

  // Respond to local bridge ping/status check from C6
  if (frame == "PING" || frame == "{\"ping\":1}") {
    StaticJsonDocument<128> pong;
    pong["bridge"] = g_loraOnline ? "OK" : "ERR";
    pong["rssi"] = (int)g_lastRssi;
    pong["snr"] = g_lastSnr;
    pong["tx"] = g_txPacketCount;
    String out;
    serializeJson(pong, out);
    BridgeSerial.println(out);
    return;
  }

  // Validate JSON before radiating over 868.1 MHz
  StaticJsonDocument<640> doc;
  DeserializationError err = deserializeJson(doc, frame);
  if (err != DeserializationError::Ok || !doc.containsKey("m")) {
    Serial.printf("[LoRa Bridge] Ignored invalid UART frame (%d bytes): %s\n", frame.length(), frame.c_str());
    return;
  }

  // Keep distinct history samples while the RF cooldown is active.
  unsigned long now = millis();
  if (g_loraRxFlag || !g_pendingMasterFrames.empty() || (g_lastRfTxMs && now - g_lastRfTxMs < 2500UL)) {
    auto commandId = [](const String& queued) -> uint16_t {
      StaticJsonDocument<384> packet;
      return deserializeJson(packet, queued) == DeserializationError::Ok && (packet["cack"] | 0) == 1 ? packet["cid"] | 0 : 0;
    };
    auto isTerminal = [](const String& queued) -> bool {
      StaticJsonDocument<384> packet;
      if (deserializeJson(packet, queued) != DeserializationError::Ok) return false;
      const char* phase = packet["phase"] | "accepted";
      return strcmp(phase, "completed") == 0 || strcmp(phase, "cancelled") == 0;
    };
    auto reportSequence = [](const String& queued) -> uint64_t {
      StaticJsonDocument<384> packet;
      return deserializeJson(packet, queued) == DeserializationError::Ok ? packet["rs"] | (uint64_t)0 : 0;
    };
    if (!shQueueBridgeFrame(g_pendingMasterFrames, frame, commandId, isTerminal, reportSequence)) {
      Serial.println(F("[Bridge] TX queue full; the controller will retry unconfirmed readings."));
      return;
    }
    return;
  }
  radiateMasterFrameNow(frame);
}

// ------------------------------------------------------------------------------
// Setup & Main Loop
// ------------------------------------------------------------------------------
void setup() {
  disableBridgeBrownoutHw();
  setCpuFrequencyMhz(80);
  pinMode(PIN_STATUS_LED, OUTPUT);
  digitalWrite(PIN_STATUS_LED, HIGH); // LED OFF (Active-LOW)

  Serial.begin(115200);
  delay(400);
  Serial.println(F("\n=================================================================="));
  Serial.println(F("  SmartHydro Vertical Tower LoRa Bridge (XIAO ESP32S3 + SX1262)   "));
  Serial.println(F("=================================================================="));

  initBridgeWatchdog(WDT_TIMEOUT_SEC);

  // Initialize UART1 Bridge to FireBeetle 2 ESP32-C6 (RX=D7/GPIO44, TX=D6/GPIO43)
  BridgeSerial.setRxBufferSize(512);
  BridgeSerial.begin(BRIDGE_UART_BAUD, SERIAL_8N1, BRIDGE_UART_RX_PIN, BRIDGE_UART_TX_PIN);
  g_uartRxBuffer.reserve(256);
  Serial.printf("[UART1 Bridge] Listening at %d baud (RX=GPIO%d/D7, TX=GPIO%d/D6)\n",
                BRIDGE_UART_BAUD, BRIDGE_UART_RX_PIN, BRIDGE_UART_TX_PIN);

  initWioSX1262();
}

void loop() {
  feedBridgeWatchdog();
  unsigned long now = millis();

  // 1. Read incoming telemetry frames from FireBeetle 2 ESP32-C6 over Hardware UART1
  while (BridgeSerial.available()) {
    char c = (char)BridgeSerial.read();
    if (c == '\n') {
      String completedFrame = g_uartRxBuffer;
      g_uartRxBuffer = "";
      handleMasterUartFrame(completedFrame);
    } else if (c != '\r') {
      if (g_uartRxBuffer.length() < 250) {
        g_uartRxBuffer += c;
      } else {
        // Prevent buffer overflow on line noise
        g_uartRxBuffer = "";
      }
    }
  }

  // 2. Flush any deferred UART frame once the 2.5s RF cooldown has elapsed
  if (!g_loraRxFlag && !g_pendingMasterFrames.empty() && (g_lastRfTxMs == 0 || now - g_lastRfTxMs >= 2500UL)) {
    String deferred = g_pendingMasterFrames.front();
    g_pendingMasterFrames.erase(g_pendingMasterFrames.begin());
    radiateMasterFrameNow(deferred);
  }

  // 3. Periodic 60s RX Re-Arm Watchdog
  if (g_loraOnline && (now - g_lastRfRxActivityMs >= 60000UL)) {
    g_lastRfRxActivityMs = now;
    radio.startReceive();
  }

  // 4. Check for incoming 868.1 MHz LoRa Downlink / ACK packets from SenseLoRa 4G Gateway
  if (g_loraOnline && g_loraRxFlag) {
    g_loraRxFlag = false;
    g_lastRfRxActivityMs = now;
    String downlinkPayload;
    size_t packetSize = radio.getPacketLength();
    uint8_t bytes[256] = {};
    int state = packetSize && packetSize <= 255 ? radio.readData(bytes, packetSize) : RADIOLIB_ERR_PACKET_TOO_LONG;
    if (state == RADIOLIB_ERR_NONE && !telemetryReceiptJson(bytes, packetSize, downlinkPayload))
      downlinkPayload = String((const char*)bytes);

    if (state == RADIOLIB_ERR_NONE && downlinkPayload.length() > 0) {
      g_rxPacketCount++;
      g_lastRssi = radio.getRSSI();
      g_lastSnr = radio.getSNR();

      Serial.printf("[LoRa RX #%lu] <<< Downlink from Gateway (%d bytes) [RSSI: %.1f dBm, SNR: %.1f dB]:\n%s\n",
                    g_rxPacketCount, downlinkPayload.length(), g_lastRssi, g_lastSnr, downlinkPayload.c_str());

      // Inject local Wio-SX1262 reception RSSI ("br") & SNR ("bs") into the JSON before forwarding to FireBeetle C6
      StaticJsonDocument<384> doc;
      DeserializationError err = deserializeJson(doc, downlinkPayload);
      if (err == DeserializationError::Ok) {
        doc["br"] = (int)roundf(g_lastRssi);
        doc["bs"] = serialized(String(g_lastSnr, 1));
        String enrichedDownlink;
        serializeJson(doc, enrichedDownlink);
        BridgeSerial.println(enrichedDownlink);
        Serial.printf("[UART1 Bridge] >>> Forwarded to C6 Master: %s\n", enrichedDownlink.c_str());
      } else {
        // Forward raw if not standard JSON
        BridgeSerial.println(downlinkPayload);
      }
    }
    radio.startReceive();
  }
}
