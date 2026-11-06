#pragma once
#include "TelemetryReceiptFrame.h"
#include <Arduino.h>
#include <SPI.h>
#include <RadioLib.h>
#include <ArduinoJson.h>
#include "Config.h"

void pauseFodderBleForLoraTx();
void resumeFodderBleAfterLoraTx();

// Hardware SPI and SX1262 Radio Instance for Heltec WiFi LoRa 32 V3
static SPIClass LoRaSPI(FSPI);
static SX1262 radio = new Module(LORA_CS, LORA_DIO1, LORA_RST, LORA_BUSY, LoRaSPI);

static bool g_isLoRaActive = false;
static unsigned long g_loraTxCount = 0;
static float g_lastRssi = 0.0f;
static float g_lastSnr = 0.0f;
static volatile bool g_loraRxFlag = false;
static SmartHydroAirtimeLedger g_fodderAirtimeLedger;

#if defined(ESP8266) || defined(ESP32)
  ICACHE_RAM_ATTR
#endif
static void onLoraRxAction() {
    g_loraRxFlag = true;
}

inline bool initNativeSX1262() {
    if (!SH_FODDER_LORA_ENABLED) {
        g_isLoRaActive = false;
        Serial.println("[LoRa] Initialization and TX/RX disabled for the isolation test.");
        return false;
    }
    Serial.println(F("[LoRa SX1262] Initializing Native SPI on Heltec WiFi LoRa 32 V3..."));

    // Initialize dedicated SPI pins for Heltec SX1262
    LoRaSPI.begin(LORA_SCK, LORA_MISO, LORA_MOSI, LORA_CS);

    // Pass 1.8V TCXO and useRegulatorLDO=false (DC-DC Buck mode) directly into begin()
    // so the SX1262 uses its internal DC-DC buck regulator and cuts 3.3V rail current by >50%
    int state = radio.begin(LORA_FREQUENCY, LORA_BANDWIDTH, LORA_SPREAD_FACTOR,
                            LORA_CODING_RATE, LORA_SYNC_WORD, LORA_OUTPUT_POWER,
                            LORA_PREAMBLE_LEN, LORA_TCXO_VOLTAGE, false);

    if (state == RADIOLIB_ERR_NONE) {
        radio.setRegulatorDCDC();
        radio.setCurrentLimit(60.0);
        radio.setOutputPower(LORA_OUTPUT_POWER);
        radio.setDio1Action(onLoraRxAction);
        g_loraRxFlag = false;
        radio.startReceive(); // Enter continuous listening mode for downlinks
        g_isLoRaActive = true;
        Serial.printf("[LoRa SX1262] [SUCCESS] SX1262 Online at %.1f MHz (SF%d, BW %.1fkHz, CR 4/%d, +%ddBm DC-DC Mode, Continuous RX Active)\n",
                      LORA_FREQUENCY, LORA_SPREAD_FACTOR, LORA_BANDWIDTH, LORA_CODING_RATE, LORA_OUTPUT_POWER);
        return true;
    } else {
        Serial.printf("[LoRa SX1262] CRITICAL: RadioLib begin() failed, code: %d\n", state);
        g_isLoRaActive = false;
        return false;
    }
}

inline bool broadcastLoRaCompactJson(String compactJson, uint8_t priorityClass = SH_PRIO_TELEMETRY) {
    if (!g_isLoRaActive || !SH_FODDER_LORA_TX_ENABLED) return false;

    StaticJsonDocument<768> packet;
    TelemetrySampleFrame packed;
    bool binary = deserializeJson(packet, compactJson) == DeserializationError::Ok && encodeTelemetrySample(packet.as<JsonObject>(), packed);
    size_t wireSize = binary ? sizeof(packed) : compactJson.length();
    uint32_t toaUs = (uint32_t)radio.getTimeOnAir(wireSize);
    uint16_t toaMs = (toaUs > 0) ? (uint16_t)((toaUs + 999UL) / 1000UL)
                                 : (uint16_t)shCalculateLoRaToaMs(wireSize, LORA_SPREAD_FACTOR, LORA_BANDWIDTH, LORA_CODING_RATE, LORA_PREAMBLE_LEN);
    if (compactJson.indexOf("\"cack\"") != -1 || compactJson.indexOf("\"cid\"") != -1) {
        priorityClass = SH_PRIO_CRITICAL_ACK;
    }
    if (!g_fodderAirtimeLedger.canTransmit(toaMs, priorityClass)) {
        Serial.printf("[ICASA Guard] Fodder skipping LoRa TX (%u ms) - 1%% hourly airtime budget reached (%lu ms used)\n",
                      (unsigned int)toaMs, (unsigned long)g_fodderAirtimeLedger.getUsedAirtimeMs());
        return false;
    }

    // Listen-Before-Talk (CAD / Channel Activity Detection)
    for (int retry = 0; retry < 3; retry++) {
        int cadState = radio.scanChannel();
        if (cadState == RADIOLIB_PREAMBLE_DETECTED) {
            unsigned long backoffMs = random(1200, 2501);
            Serial.printf("[LoRa CAD] Channel busy! Backing off for %lu ms (attempt %d/3)...\n", backoffMs, retry + 1);
            delay(backoffMs);
        } else {
            break;
        }
    }

    g_loraTxCount++;
    Serial.printf("[LoRa TX] >>> Broadcasting Compact Minified JSON (#%lu, %d bytes, ToA ~%u ms, 1h Airtime %lu/%lu ms):\n%s\n",
                  g_loraTxCount, (int)wireSize, (unsigned int)toaMs,
                  (unsigned long)g_fodderAirtimeLedger.getUsedAirtimeMs(),
                  (unsigned long)ICASA_MAX_AIRTIME_MS_PER_HOUR, compactJson.c_str());
    Serial.flush();

    // Pause BLE advertising during the LoRa TX burst so 2.4GHz BLE and 868MHz LoRa
    // never draw simultaneous peak PA current from the Heltec V3 3.3V LDO rail
    pauseFodderBleForLoraTx();
    delay(15);

    int state = binary ? radio.transmit((uint8_t*)&packed, sizeof(packed)) : radio.transmit(compactJson);

    // Clear any TxDone interrupt that tripped DIO1 during the transmission burst
    g_loraRxFlag = false;
    radio.startReceive(); // Immediately restore continuous receive mode

    delay(10);
    resumeFodderBleAfterLoraTx();

    if (state == RADIOLIB_ERR_NONE) {
        g_fodderAirtimeLedger.recordTransmission(toaMs);
        Serial.println(F("[LoRa TX] [OK] Packet transmitted successfully over 868.1 MHz."));
        return true;
    } else {
        Serial.printf("[LoRa TX] [FAIL] Transmission error: %d\n", state);
        return false;
    }
}

inline bool checkLoraDownlink(String &payload) {
    if (!g_isLoRaActive || !g_loraRxFlag) return false;

    g_loraRxFlag = false;
    size_t packetSize = radio.getPacketLength();
    if (!packetSize || packetSize > 255) { radio.startReceive(); return false; }
    uint8_t bytes[256] = {};
    int state = radio.readData(bytes, packetSize);
    if (state == RADIOLIB_ERR_NONE && !telemetryReceiptJson(bytes, packetSize, payload))
        payload = String((const char*)bytes);
    if (state == RADIOLIB_ERR_NONE && payload.length() > 0) {
        g_lastRssi = radio.getRSSI();
        g_lastSnr = radio.getSNR();
        Serial.printf("[LoRa RX] <<< Downlink received from Gateway (%d bytes) [RSSI: %.1f dBm, SNR: %.1f dB]:\n%s\n",
                      (int)payload.length(), g_lastRssi, g_lastSnr, payload.c_str());
        radio.startReceive();
        return true;
    }
    radio.startReceive();
    return false;
}

inline unsigned long getLoRaTxCount() {
    return g_loraTxCount;
}

inline float getLoRaLastRssi() {
    return g_lastRssi;
}

inline float getLoRaLastSnr() {
    return g_lastSnr;
}
