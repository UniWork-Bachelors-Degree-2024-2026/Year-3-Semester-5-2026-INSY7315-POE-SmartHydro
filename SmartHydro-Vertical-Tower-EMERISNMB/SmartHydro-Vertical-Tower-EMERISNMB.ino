// ==============================================================================
// SMART HYDROPONIC CONTROLLER - VERTICAL TOWER NODE FIRMWARE (ESP32-C6)
// ==============================================================================
// Target MCU:  DFRobot FireBeetle 2 ESP32-C6 (Single-Core 32-bit RISC-V @ 160MHz)
// Co-Proc:     Seeed Studio XIAO ESP32S3 + Wio-SX1262 B2B via UART1 (115,200 baud)
// Deployment:  Vertical Aeroponic Tower System (25-30L Reservoir, EMERISNMB)
// Features:
// - Tri-Mode Operation (Primary Default: 868.1 MHz LoRa P2P + NimBLE 5.0;
//   Mode 2: Emergency SoftAP via 3s BOOT button or MVC;
//   Mode 1: Cloud Wi-Fi + SignalR WebSockets + LoRa Redundancy)
// - Hybrid User Threshold + 4 Embedded Random Forest ML Classifiers (EC, pH, Temp, Hum)
// - Strict 16 Hours ON / 8 Hours OFF Non-Dynamic Photoperiod + Gravity Ambient Light Percentage
// - Timed dosing with shared fault checks, mixing lockout and a pH hourly cap.
// - Unconditional Pump Operation: Low water level is informational only and never aborts any pump.
// ==============================================================================

#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <Arduino.h>
#include <ArduinoJson.h>
#include <DHT.h>
#include <HTTPClient.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include "CloudSecurity.h"
#include <WebSocketsClient_Generic.h>
#include <Wire.h>
#include <esp_mac.h>
#include <math.h>
#include <mbedtls/md.h>
#include <mbedtls/base64.h>

#include "Config.h"
#include "CommandProtocol.h"
#include "RelayReports.h"
static QueueHandle_t bleCommandQueue = nullptr;
struct SHQueuedCommand { char payload[512]; uint8_t source; uint32_t receivedAt; };
static bool queueControlPayload(const String& payload, uint8_t source) {
    if (!bleCommandQueue || payload.length() >= 512) return false;
    SHQueuedCommand message = {};
    message.source = source;
    message.receivedAt = millis();
    payload.toCharArray(message.payload, sizeof(message.payload));
    return xQueueSend(bleCommandQueue, &message, 0) == pdTRUE;
}
bool queueBleCommandPayload(const String& payload) { return queueControlPayload(payload, 1); }
static SHCommandHistory commandHistory;
static SHDoseSafety doseSafety;
static SHManualOverrides manualOverrides;
static bool lastDoseAccepted = false;
static uint16_t activeDoseCid = 0;
static uint8_t activeDoseSource = 0;
static bool activeCommandDose = false;
static int commandDoseResult = 0;
static bool lastCommandAccepted = false;
static const char* lastCommandReason = "invalid_command";
static void sendCommandAck(SHCommandResult result, uint8_t source = 0);
static void serviceCommandCompletion();
static void serviceConnectivity();
static void cancelActiveCommand();
static bool canStoreCommandReceipt(uint16_t cid);
#include "SensorCalibration.h"
static SensorCalibration sensorCalibration;
#include "EC.h"
#include "Humidity.h"
#include "Provisioning.h"
#include "ConnectivityPolicy.h"
static SHConnectivityPolicy g_connectivity;
static bool g_connectivityStarted = false;
static const char* g_modeSwitchFailure = nullptr;
#define SH_CAPTURE_SYSTEM "hydroponics"
#define SH_HOTSPOT_PREFIX "SmartHydro-Setup-"
#include "Temperature.h"
#include "pH.h"
#include "TowerBLE.h"

#if defined(CONFIG_IDF_TARGET_ESP32C6) || defined(ARDUINO_DFROBOT_FIREBEETLE_2_ESP32C6) || defined(ESP32C6)
#include "soc/soc.h"
#include "soc/lp_analog_peri_reg.h"
#include "soc/gpio_reg.h"
#include "esp_rom_gpio.h"
#include "esp_err.h"
#include "esp_private/startup_internal.h"
#include "esp_private/brownout.h"

// ==============================================================================
// EARLY-BOOT BROWNOUT DETECTOR OVERRIDE & RELAY RAIL STABILIZATION (ESP32-C6)
// ==============================================================================
// Override esp_brownout_init() / esp_brownout_disable() and register an
// ESP_SYSTEM_INIT_STAGE_CORE priority 1 hook so the brownout ISR is never linked
// or enabled and all 8 Active-LOW relay GPIOs (5, 6, 7, 14, 18, 21, 22, 23) are
// latched HIGH immediately on boot before RF or peripheral initialization.
static void IRAM_ATTR disableTowerBrownoutAndLatchRelays() {
#ifdef LP_ANALOG_PERI_LP_ANA_FIB_ENABLE_REG
  WRITE_PERI_REG(LP_ANALOG_PERI_LP_ANA_FIB_ENABLE_REG, 0);
#endif
#ifdef LP_ANALOG_PERI_LP_ANA_BOD_MODE0_CNTL_REG
  WRITE_PERI_REG(LP_ANALOG_PERI_LP_ANA_BOD_MODE0_CNTL_REG, 0);
#endif

  const uint32_t relayMask = (1UL << PIN_RELAY_WATER_PUMP) | (1UL << PIN_RELAY_LIGHT) |
                             (1UL << PIN_RELAY_FAN_CIRC)   | (1UL << PIN_RELAY_FAN_EXT) |
                             (1UL << PIN_DOSE_PH_UP)       | (1UL << PIN_DOSE_PH_DOWN) |
                             (1UL << PIN_DOSE_EC_UP)       | (1UL << PIN_DOSE_EC_DOWN);
#ifdef GPIO_OUT_W1TS_REG
  REG_WRITE(GPIO_OUT_W1TS_REG, relayMask);
#endif

  const uint8_t relayPins[] = {PIN_RELAY_WATER_PUMP, PIN_RELAY_LIGHT,
                               PIN_RELAY_FAN_CIRC,   PIN_RELAY_FAN_EXT,
                               PIN_DOSE_PH_UP,       PIN_DOSE_PH_DOWN,
                               PIN_DOSE_EC_UP,       PIN_DOSE_EC_DOWN};
  for (size_t i = 0; i < sizeof(relayPins); i++) {
    uint8_t p = relayPins[i];
    esp_rom_gpio_pad_select_gpio(p);
    esp_rom_gpio_pad_pullup_only(p);
  }

#ifdef GPIO_OUT_W1TS_REG
  REG_WRITE(GPIO_OUT_W1TS_REG, relayMask);
#endif
#ifdef GPIO_ENABLE_W1TS_REG
  REG_WRITE(GPIO_ENABLE_W1TS_REG, relayMask);
#endif
}

extern "C" void esp_brownout_init(void) {
  disableTowerBrownoutAndLatchRelays();
}

extern "C" void esp_brownout_disable(void) {
  disableTowerBrownoutAndLatchRelays();
}

ESP_SYSTEM_INIT_FN(early_tower_boot_stabilize, CORE, BIT(0), 1) {
  disableTowerBrownoutAndLatchRelays();
  return ESP_OK;
}

__attribute__((constructor(101))) static void earlyDisableTowerBrownout() {
  disableTowerBrownoutAndLatchRelays();
}
#endif

// Hardware Instances
static DHT dht(PIN_DHT, DHT22);
static Adafruit_SSD1306 display(128, 64, &Wire, -1);
static HardwareSerial LoRaBridgeSerial(1);
static bool hasOled = false;
static bool isDeviceRegistered = false;

// ML Model Instances (Eloquent ML Ported Random Forests)
static Eloquent::ML::Port::RandomForestEC modelEC;
static Eloquent::ML::Port::RandomForestpH modelPH;
static Eloquent::ML::Port::RandomForestTemperature modelTemp;
static Eloquent::ML::Port::RandomForestHumidity modelHum;

// Relay States Cache & Previous Bitmask Tracker
RelayStates relays;
static uint8_t g_lastBroadcastBitmask = 0xFF;
static uint8_t g_lastLocalRelayReport = 0;
static bool g_hasLocalRelayReport = false;

// Device Identity & Operational Mode
static String deviceMac = "";
static int g_operatingMode = MODE_LORA_BLE; // Defaults to Primary LoRa + BLE Mode (0)
bool isOfflineMode = false;
bool portalActive = false;

// LoRa Co-Processor (XIAO ESP32S3 + Wio-SX1262) Bridge Telemetry State
static String g_loraUartRxBuf = "";
static unsigned long g_loraTxCount = 0;
static unsigned long g_lastLoraTxMs = 0;
static unsigned long g_lastGatewayCloudUpMs = 0;
static bool g_pendingImmediateLoraTx = false;
static bool g_lastTxAcked = false;
static int g_xiaoLoraRssi = 0;     // RSSI measured by Seeed XIAO ESP32S3 + Wio-SX1262 ("br")
static int g_gatewayLoraRssi = 0;  // RSSI measured by SenseLoRa 4G Gateway ("r")
static float g_xiaoLoraSnr = 0.0f; // SNR measured by Seeed XIAO ESP32S3 + Wio-SX1262 ("bs")

// Dynamic Recipe Thresholds (Synced with Cloud/LoRa/BLE, Persisted in NVS Flash)
static float targetMinPh = 5.5f;
static float targetMaxPh = 6.5f;
static float targetMinEc = 1.0f;
static float targetMaxEc = 2.5f;
static float targetMinTemp = 18.0f;
static float targetMaxTemp = 25.0f;
static float targetMinHum = 50.0f;
static float targetMaxHum = 75.0f;


// Dual-Tier Dosing & Plant Safety State (25-30L Vertical Tower Reservoir)
static uint8_t g_activeDosePin = 0;
static unsigned long g_doseEndMs = 0;
static bool g_activeDoseIsManual = false;
static bool g_requestedPhOverride = false, g_activePhOverride = false;

static uint32_t manualPhOverrideWindow() {
    // A signed, confirmed manual request may bypass pH availability only.
    // Fresh EC and its recovery gate remain mandatory throughout the pulse.
    return sensorCalibration.ecDoseGuard.freshFor(millis());
}

static bool verifiedPhOverride(JsonObject cmd, uint16_t cid) {
    const char* key = shDeviceKey();
    const char* proof = cmd["ps"] | "";
    const char* type = cmd["dt"] | "";
    const char* doseId = cmd["doseId"] | "";
    if (strlen(key) != 64 || strspn(key, "0123456789abcdefABCDEF") != 64 || strlen(proof) != 22 ||
        strlen(doseId) != 36 || !cmd["exp"].is<uint32_t>() || !cmd["dm"].is<uint32_t>()) return false;
    String mac = deviceMac; mac.replace(":", ""); mac.toUpperCase();
    String message = "po1|" + mac + "|" + String(cid) + "|" + String(cmd["exp"].as<uint32_t>()) +
        "|" + String(type) + "|" + String(cmd["dm"].as<uint32_t>()) + "|" + String(doseId);
    uint8_t bytes[32], hash[32];
    for (uint8_t i = 0; i < 32; ++i) { char pair[3] = {key[i * 2], key[i * 2 + 1], 0}; bytes[i] = strtoul(pair, nullptr, 16); }
    const auto* algorithm = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
    if (!algorithm || mbedtls_md_hmac(algorithm, bytes, sizeof(bytes), (const uint8_t*)message.c_str(), message.length(), hash)) return false;
    uint8_t encoded[25]; size_t length = 0;
    if (mbedtls_base64_encode(encoded, sizeof(encoded), &length, hash, 16) || length != 24) return false;
    uint8_t difference = 0;
    for (uint8_t i = 0; i < 22; ++i) {
        char expected = encoded[i] == '+' ? '-' : encoded[i] == '/' ? '_' : encoded[i];
        difference |= (uint8_t)(expected ^ proof[i]);
    }
    return difference == 0;
}

static const char* activeDoseSensorReason() {
    if (!g_activePhOverride) return sensorCalibration.doseGuard.refusal(millis());
    return sensorCalibration.ecDoseGuard.refusal(millis());
}
static String g_activeDoseCmdId = "";
static String g_activeDoseName = "";
static String g_pendingCallbackCmdId = "";
static String g_pendingCallbackStatus = "";

static unsigned long g_lastAnyDoseMs = 0;       // Timestamp of last autonomous micro-dose
static bool g_inDiffusionLockout = false;       // True while the reservoir mixing lockout is active
static uint8_t g_dailyDoseCount = 0;            // Cumulative autonomous micro-doses in rolling window
static unsigned long g_dayWindowStartMs = 0;    // Start timestamp of rolling dosing window
static bool g_phRunawayAbort = false;           // Latched true if >= MAX_DAILY_DOSES autonomous doses
static bool g_sensorFaultActive = false;        // True if probe outside plausible biological bounds
static String g_activeAlertCode = "";

// Deferred Non-Blocking SoftAP Relay Action Queue (Prevents HTTP socket drop on relay switch)
static String g_pendingSoftApRelayName = "";
static bool g_pendingSoftApRelayState = false;
static bool g_hasPendingSoftApRelay = false;
static String g_localDosePump = "";
static unsigned long g_localDoseMs = 0;

// Real-time Bidirectional WebSocket (Active only in Online Mode 1: MODE_WIFI_CLOUD)
static WebSocketsClient wsActuator;
static bool isWsConnected = false;
static bool isWsHandshakeDone = false;
static bool wsInitialized = false;
static bool inWsCallback = false;

// Schedule & Manual Override Timers
static bool g_manualLightOverride = false;
static bool g_manualWaterPumpOverride = false;
static bool g_manualCircFanOverride = false;
static bool g_manualExtFanOverride = false;
static unsigned long g_manualOverrideUntilMs = 0;
static unsigned long manualOverrideUntilMs = 0;
static unsigned long timedPumpEndMs = 0;

static unsigned long lastSensorPollMs = 0;
static unsigned long lastCloudSyncMs = 0;
static unsigned long pumpCycleStartMs = 0;
static unsigned long lightCycleStartMs = 0;
static unsigned long g_lastRelayEnergizeMs = 0;

// Forward Declarations
void setRelay(uint8_t pin, bool active);
static void updateOledDisplay();
static void showModeBootBanner();
void broadcastCompactJson(bool forceLoRaTx = false);
void switchOperatingMode(int newMode);

// ------------------------------------------------------------------------------
// Helper: Current 8-Bit Relay Bitmask (Canonical SmartHydro API Bit Order)
// Bit 0 (1):   MainWaterPump / SpraySystem
// Bit 1 (2):   GrowLight
// Bit 2 (4):   CirculationFan
// Bit 3 (8):   ExtractorFan
// Bit 4 (16):  PhUpPump
// Bit 5 (32):  PhDownPump
// Bit 6 (64):  NutrientUpPump
// Bit 7 (128): DilutionDownPump
// ------------------------------------------------------------------------------
static uint8_t computeRelayBitmask() {
  // Active-low GPIO read-back confirms output levels, not contact closure or flow.
  return (digitalRead(PIN_RELAY_WATER_PUMP) == LOW ? 1 : 0) |
         (digitalRead(PIN_RELAY_LIGHT) == LOW ? 2 : 0) |
         (digitalRead(PIN_RELAY_FAN_CIRC) == LOW ? 4 : 0) |
         (digitalRead(PIN_RELAY_FAN_EXT) == LOW ? 8 : 0) |
         (digitalRead(PIN_DOSE_PH_UP) == LOW ? 16 : 0) |
         (digitalRead(PIN_DOSE_PH_DOWN) == LOW ? 32 : 0) |
         (digitalRead(PIN_DOSE_EC_UP) == LOW ? 64 : 0) |
         (digitalRead(PIN_DOSE_EC_DOWN) == LOW ? 128 : 0);
}

#include "TelemetryStorage.h"
#include "OnlineTransport.h"
#define SH_AUDIT_WATER_PIN PIN_RELAY_WATER_PUMP
#include "ActivityStorage.h"


static String compactHexMac(const String& mac) {
  String out;
  out.reserve(12);
  for (unsigned int i = 0; i < mac.length(); i++) {
    char c = mac[i];
    if (c != ':' && c != '-') {
      out += (char)toupper(c);
    }
  }
  return out;
}

static bool isMatchingNodeMac(const String& candidate) {
  if (candidate == "*" || candidate.equalsIgnoreCase("ALL")) return true;
  return compactHexMac(candidate) == compactHexMac(deviceMac);
}

// ------------------------------------------------------------------------------
// Staggered Active-LOW Relay Driver (150ms Inrush Suppression)
// ------------------------------------------------------------------------------
void setRelay(uint8_t pin, bool active) {
  int targetLevel = active ? LOW : HIGH;
    bool changed = digitalRead(pin) != targetLevel;
  if (digitalRead(pin) != targetLevel) {
    if (active) {
      unsigned long elapsed = millis() - g_lastRelayEnergizeMs;
      if (elapsed < RELAY_STAGGER_MS) {
        delay(RELAY_STAGGER_MS - elapsed);
      }
      g_lastRelayEnergizeMs = millis();
    }
    digitalWrite(pin, targetLevel);
    bool chemical = pin == PIN_DOSE_PH_UP || pin == PIN_DOSE_PH_DOWN || pin == PIN_DOSE_EC_UP || pin == PIN_DOSE_EC_DOWN;
    if (active && chemical && !doseSafety.outputStarted(pin)) { digitalWrite(pin, HIGH); active = false; }
    if (active) {
      delay(30); // Allow rail decoupling capacitor to settle
    }
  }
  active = digitalRead(pin) == LOW;
  if (pin == PIN_DOSE_PH_UP) relays.phUp = active;
  else if (pin == PIN_DOSE_PH_DOWN) relays.phDown = active;
  else if (pin == PIN_DOSE_EC_UP) relays.ecUp = active;
  else if (pin == PIN_DOSE_EC_DOWN) relays.ecDown = active;
  else if (pin == PIN_RELAY_WATER_PUMP) relays.waterPump = active;
  else if (pin == PIN_RELAY_LIGHT) relays.growLight = active;
  else if (pin == PIN_RELAY_FAN_CIRC) relays.circulationFan = active;
  else if (pin == PIN_RELAY_FAN_EXT) relays.extractorFan = active;
    const char* key = pin == PIN_RELAY_WATER_PUMP ? "pump_on" : pin == PIN_RELAY_LIGHT ? "light_on" :
                      pin == PIN_RELAY_FAN_CIRC ? "fan_on" : pin == PIN_RELAY_FAN_EXT ? "ext_on" : nullptr;
    if (changed && key) {
        prefs.begin("smarthydro", false);
        prefs.putBool(key, active);
        prefs.end();
    }

    if (changed && auditAction(pin) < 4) recordActivity(pin, active ? 0 : 1, auditSource, 0, auditCid);
}

void safeDelay(unsigned long ms) {
  unsigned long start = millis();
  while (millis() - start < ms) {
    feedWatchdog();
    if (!inWsCallback && g_operatingMode == MODE_WIFI_CLOUD && WiFi.status() == WL_CONNECTED) {
      wsActuator.loop();
    }
    if (g_operatingMode == MODE_EMERGENCY_SOFTAP || isOfflineMode) {
      server.handleClient();
    }
    delay(10);
  }
}

// ------------------------------------------------------------------------------
// Dual-Tier Peristaltic Dosing Engine (Tier 1 Manual Override + Tier 2 Autonomous)
// ------------------------------------------------------------------------------
static uint8_t resolveTowerDosePin(const char* actName) {
  if (!actName) return 0;
  if (strcasecmp(actName, "PhUpPump") == 0 || strcasecmp(actName, "PhUp") == 0 ||
      strcasecmp(actName, "PH_UP") == 0 || strcasecmp(actName, "pu") == 0 ||
      strcasecmp(actName, "phUp") == 0) {
    return PIN_DOSE_PH_UP;
  }
  if (strcasecmp(actName, "PhDownPump") == 0 || strcasecmp(actName, "PhDown") == 0 ||
      strcasecmp(actName, "PH_DOWN") == 0 || strcasecmp(actName, "pd") == 0 ||
      strcasecmp(actName, "phDown") == 0) {
    return PIN_DOSE_PH_DOWN;
  }
  if (strcasecmp(actName, "NutrientUpPump") == 0 || strcasecmp(actName, "NutrientUp") == 0 ||
      strcasecmp(actName, "EcUpPump") == 0 || strcasecmp(actName, "EcUp") == 0 ||
      strcasecmp(actName, "NUTRIENT_UP") == 0 || strcasecmp(actName, "EC_UP") == 0 ||
      strcasecmp(actName, "NUTRIENT_A") == 0 || strcasecmp(actName, "nutrientA") == 0 ||
      strcasecmp(actName, "nu") == 0 || strcasecmp(actName, "ecUp") == 0) {
    return PIN_DOSE_EC_UP;
  }
  if (strcasecmp(actName, "DilutionDownPump") == 0 || strcasecmp(actName, "DilutionDown") == 0 ||
      strcasecmp(actName, "EcDownPump") == 0 || strcasecmp(actName, "EcDown") == 0 ||
      strcasecmp(actName, "DILUTION_DOWN") == 0 || strcasecmp(actName, "EC_DOWN") == 0 ||
      strcasecmp(actName, "NUTRIENT_B") == 0 || strcasecmp(actName, "nutrientB") == 0 ||
      strcasecmp(actName, "nd") == 0 || strcasecmp(actName, "ecDown") == 0) {
    return PIN_DOSE_EC_DOWN;
  }
  return 0;
}

static void stopTowerDosingPump(uint8_t pin) {
  if (pin == 0) return;
    if (g_activeDosePin == pin) recordActivity(pin, 5, auditSource, activeAuditDuration, activeAuditCid);
  setRelay(pin, false);
  if (g_activeDosePin == pin) {
    cancelActiveCommand();
    g_activeDosePin = 0;
    g_doseEndMs = 0;
    g_activeDoseIsManual = false;
    g_activePhOverride = false;
    if (g_activeDoseCmdId.length() > 0) {
      g_pendingCallbackCmdId = g_activeDoseCmdId;
      g_pendingCallbackStatus = "FAILED";
      g_activeDoseCmdId = "";
    }
  }
}

static bool isDosingSafe(const char* callerReason, bool isManual = false, bool phOverride = false) {
  unsigned long now = millis();

  // Rolling window reset for autonomous dosing cap
  if (g_dayWindowStartMs == 0 || (now - g_dayWindowStartMs >= DAY_WINDOW_MS)) {
    g_dayWindowStartMs = now;

    g_dailyDoseCount = 0;
    g_phRunawayAbort = false;
    g_activeAlertCode = "";
  }



  // Only a confirmed manual override bypasses the pH part of this gate.
  if (sensorCalibration.doseGuard.refusal(now) && !(isManual && phOverride && manualPhOverrideWindow())) {
    g_sensorFaultActive = true;
    g_activeAlertCode = "SENSOR_FAULT";
    return false;
  }
  g_sensorFaultActive = sensorCalibration.doseGuard.refusal(now) != nullptr;

  // 2. Rolling dose cap
  if (g_dailyDoseCount >= MAX_DAILY_DOSES) {
    g_phRunawayAbort = true;
    g_activeAlertCode = "PH_RUNAWAY_ABORT";
    return false;
  }

  // 3. Shared reservoir mixing lockout
  if (g_lastAnyDoseMs != 0 && (doseSafety.remaining() > 0)) {
    g_inDiffusionLockout = true;
    return false;
  }
  g_inDiffusionLockout = false;

  return true;
}

void startMicroDoseMs(const char* actName, unsigned long durationMs, const char* cmdId, bool isManual) {
  lastDoseAccepted = false;
  uint8_t pin = resolveTowerDosePin(actName);
  if (pin && auditSource == 0 && auditCid && !canStoreCommandReceipt(auditCid)) {
    reportDoseRefusal(pin, actName, "safety_storage", durationMs);
    return;
  }
  bool phOverride = isManual && g_requestedPhOverride && manualPhOverrideWindow();
  if (g_requestedPhOverride && !phOverride) {
    const char* reason = sensorCalibration.ecDoseGuard.refusal(millis());
    reportDoseRefusal(pin, actName, reason ? reason : "sensor_fault", durationMs); return;
  }
  bool outerSafe = pin && isDosingSafe(actName, isManual, phOverride);
  if (!outerSafe ||
      !doseSafety.arm(pin, durationMs, phOverride ? false : sensorCalibration.faultPH || sensorCalibration.faultEC,
                      g_activeDosePin != 0, phOverride ? manualPhOverrideWindow() : sensorCalibration.doseGuard.freshFor(millis()))) {
    const char* sensorReason = phOverride ? sensorCalibration.ecDoseGuard.refusal(millis()) : sensorCalibration.doseGuard.refusal(millis());
    const char* reason = outerSafe ? doseSafety.reason() : sensorReason ? sensorReason :
        g_phRunawayAbort ? "dose_count_limit" : g_inDiffusionLockout ? "mixing_lockout" : "outer_safety_limit";
    if (!reason) reason = "sensor_fault";
    reportDoseRefusal(pin, actName, reason, durationMs);
    return;
  }
  g_activeDosePin = pin;
  g_activeDoseName = String(actName);
  g_activeDoseCmdId = cmdId ? String(cmdId) : "";
  g_doseEndMs = millis() + durationMs;
  g_activeDoseIsManual = isManual;
  g_activePhOverride = phOverride;
  if (phOverride) Serial.println(F("[Dosing] Confirmed manual pH fault override. Current pH is unknown; fresh EC and dose limits remain required. Automatic dosing remains guarded."));
  g_lastAnyDoseMs = millis();
  g_dailyDoseCount++;
  setRelay(pin, true);
  if (digitalRead(pin) != LOW) { g_activeDosePin = 0; return; }
  g_doseEndMs = doseSafety.endsAt();
  lastDoseAccepted = true;
  // Record the ON edge before slower activity writes; the independent cutoff keeps running.
  notifyTowerBLE(buildCompactJson());
  captureTelemetrySnapshot();
    activeAuditSource = auditSource; activeAuditCid = auditCid; activeAuditDuration = durationMs;
    recordActivity(pin, 2, auditSource, durationMs, auditCid);
  Serial.printf("[Dosing] %s: estimated %.2f mL, %lu ms at 800 ms/mL\n", actName, durationMs / 800.0f, durationMs);
}
void startAsyncDose(const char* actName, int volumeMl, const char* cmdId, bool isManual) {
  unsigned long durationMs = MICRO_DOSE_MS;
  if (isManual) {
    if (volumeMl <= 1) {
      durationMs = MS_PER_ML;
    } else {
      durationMs = constrain((unsigned long)volumeMl * MICRO_DOSE_MS, MICRO_DOSE_MS, MANUAL_MAX_DOSE_MS);
    }
  } else {
    int vol = volumeMl > 0 ? volumeMl : 1;
    durationMs = constrain((unsigned long)vol * MICRO_DOSE_MS, MICRO_DOSE_MS, 4000UL);
  }
  startMicroDoseMs(actName, durationMs, cmdId, isManual);
}

static inline void executeDosing(const char* actName, int durationSec, const char* cmdId = "", bool isManual = false) {
  startAsyncDose(actName, durationSec, cmdId, isManual);
}

static void sendDosingCallback(const char* cmdId, const char* status) {
  if (!cmdId || strlen(cmdId) == 0) return;
  if (g_operatingMode != MODE_WIFI_CLOUD || WiFi.status() != WL_CONNECTED) return;
  if (beginOnlineRequest("/api/Dosing/callback", 1500)) {
    StaticJsonDocument<256> doc;
    doc["commandId"] = cmdId; doc["status"] = status;
    String payload; serializeJson(doc, payload);
    towerOnlineHttp.POST(payload);
    towerOnlineHttp.getString(); // Drain the reply before reusing this connection.
    towerOnlineHttp.end();
  }
}

static void checkDosingProgress() {
    if (g_activeDosePin && (activeDoseSensorReason() ||
                           strcmp(doseSafety.reason(), "sensor_stale") == 0)) {
        const char* reason = activeDoseSensorReason();
        recordActivity(g_activeDosePin, 6, 6, activeAuditDuration, activeAuditCid,
                       auditReason(reason ? reason : doseSafety.reason()));
        uint8_t previousSource = auditSource; auditSource = 6;
        stopTowerDosingPump(g_activeDosePin); auditSource = previousSource;
    }
  unsigned long now = millis();
  if (g_activeDosePin != 0 && (int32_t)(now - g_doseEndMs) >= 0) {
    bool wasManual = g_activeDoseIsManual;
    Serial.printf("[Dosing] Timed pulse completed for %s (Pin %d, %s).\n",
                  g_activeDoseName.c_str(), g_activeDosePin, wasManual ? "Manual" : "Autonomous");
    recordActivity(g_activeDosePin, 3, activeAuditSource, activeAuditDuration, activeAuditCid);
    setRelay(g_activeDosePin, false);
    g_activeDosePin = 0;
    g_doseEndMs = 0;
    g_activeDoseIsManual = false;
    g_activePhOverride = false;
    relays.phUp = relays.phDown = relays.ecUp = relays.ecDown = false;

    if (g_activeDoseCmdId.length() > 0) {
      g_pendingCallbackCmdId = g_activeDoseCmdId;
      g_pendingCallbackStatus = "COMPLETED";
      g_activeDoseCmdId = "";
    }

    // Broadcast updated OFF state after pulse finishes
    broadcastCompactJson(true);
    updateOledDisplay();
  }

  // Clear the flag once the shared mixing lockout has elapsed
  if (g_inDiffusionLockout && doseSafety.remaining() == 0) {
    g_inDiffusionLockout = false;
    Serial.println(F("[Plant Safety] Reservoir wait complete. Dosing guards will be checked before another dose."));
  }

  if (!inWsCallback && g_pendingCallbackCmdId.length() > 0 &&
      g_operatingMode == MODE_WIFI_CLOUD && WiFi.status() == WL_CONNECTED) {
    String cmdId = g_pendingCallbackCmdId;
    String status = g_pendingCallbackStatus;
    g_pendingCallbackCmdId = "";
    g_pendingCallbackStatus = "";
    sendDosingCallback(cmdId.c_str(), status.c_str());
  }
}

// ------------------------------------------------------------------------------
// Hybrid Compact Minified JSON Builders (LoRa Bit-Packed "r" vs Local BLE)
// ------------------------------------------------------------------------------
String buildLoraCompactJson() {
    return telemetryLoraJson(captureTelemetrySnapshot());
}

String buildCompactJson() {
  StaticJsonDocument<512> doc;
  doc["m"] = deviceMac;
  doc["p"] = serialized(String(currentTelemetry.ph, 2));
  doc["e"] = serialized(String(currentTelemetry.ec, 2));
  doc["t"] = serialized(String(currentTelemetry.temp, 1));
  doc["h"] = serialized(String(currentTelemetry.hum, 1));
  doc["w"] = currentTelemetry.waterOk ? 1 : 0;

  shBleRelayFields(doc.as<JsonObject>(), computeRelayBitmask());
  doc["lux"] = (int)currentTelemetry.lux;
  doc["l"] = (int)currentTelemetry.lux;
  doc["md"] = g_operatingMode;
    doc["pm"] = g_connectivity.preferred; doc["mr"] = g_connectivity.reason;
    doc["sf"] = (currentTelemetry.ph < 0 ? 1 : 0) | (currentTelemetry.ec < 0 ? 2 : 0)
        | (currentTelemetry.temp == -99 ? 4 : 0) | (currentTelemetry.hum < 0 ? 8 : 0)
        | (currentTelemetry.lux < 0 ? 16 : 0);

  if (g_phRunawayAbort) {
    doc["al"] = "PH_RUNAWAY_ABORT";
  } else if (g_sensorFaultActive) {
    doc["al"] = "SENSOR_FAULT";
  }

  String out;
  serializeJson(doc, out);
  return out;
}

void sendLoRaBridgePacket(const String& compactJson) {
  g_loraTxCount++;
  g_lastLoraTxMs = millis();
  g_lastTxAcked = false;
  g_lastBroadcastBitmask = computeRelayBitmask();

  LoRaBridgeSerial.println(compactJson);
  markTelemetrySent(compactJson);
  Serial.printf("[UART1 -> XIAO LoRa #%lu] (%d bytes): %s\n",
                g_loraTxCount, compactJson.length(), compactJson.c_str());
}

void broadcastCompactJson(bool forceLoRaTx) {
  String bleJson = buildCompactJson();
  notifyTowerBLE(bleJson);

  uint8_t currentMask = computeRelayBitmask();
  g_lastLocalRelayReport = currentMask;
  g_hasLocalRelayReport = true;
  unsigned long now = millis();
  bool wantsLoRaTx = forceLoRaTx || (currentMask != g_lastBroadcastBitmask);
  if (!wantsLoRaTx) {
    return;
  }

  // Save this transition now, even if the radio must wait. A short pulse can
  // finish before the next transmission slot, so keeping only its final mask loses history.
  RingBufferSlot sample = captureTelemetrySnapshot();
  bool minCooldownElapsed = (g_lastLoraTxMs == 0) || (now - g_lastLoraTxMs >= 2500UL);
  if (minCooldownElapsed) {
    g_pendingImmediateLoraTx = false;
    sendLoRaBridgePacket(telemetryLoraJson(sample));
  } else {
    // Latch deferred TX so rapid pump/relay transitions are never silently dropped!
    g_pendingImmediateLoraTx = true;
  }
}

// ------------------------------------------------------------------------------
// Actuator State & Command Handlers (SignalR, LoRa Downlink, BLE, SoftAP)
// ------------------------------------------------------------------------------
static String getApiHostName() {
  String host = getApiBaseUrl();
  if (host.startsWith("https://")) host = host.substring(8);
  else if (host.startsWith("http://")) host = host.substring(7);
  int slashIdx = host.indexOf('/');
  if (slashIdx != -1) host = host.substring(0, slashIdx);
  int colonIdx = host.indexOf(':');
  if (colonIdx != -1) host = host.substring(0, colonIdx);
  return host;
}

static int getApiPort() {
  String api = getApiBaseUrl();
  int port = api.startsWith("https://") ? 443 : 80;
  int colonIdx = api.indexOf(':', 8);
  if (colonIdx != -1) {
    int slashIdx = api.indexOf('/', colonIdx);
    String portStr = (slashIdx != -1) ? api.substring(colonIdx + 1, slashIdx) : api.substring(colonIdx + 1);
    int parsed = portStr.toInt();
    if (parsed > 0) port = parsed;
  }
  return port;
}

void sendActuatorStateReport(const char* actKey, bool state, const char* reason) {
  if (g_operatingMode == MODE_WIFI_CLOUD && isWsConnected && isWsHandshakeDone) {
    uint64_t sequence = nextRelayReportSequence();
    if (!sequence) return;
    StaticJsonDocument<640> message;
    message["type"] = 1; message["target"] = "ReportActuatorState";
    auto args = message.createNestedArray("arguments"); args.add(deviceMac);
    auto report = args.createNestedObject(); uint8_t mask = computeRelayBitmask();
    const char* keys[] = {"mainWaterPump", "growLight", "circulationFan", "extractorFan",
                          "phUpPump", "phDownPump", "nutrientUpPump", "dilutionDownPump"};
    for (int bit = 0; bit < 8; ++bit) report[keys[bit]] = (mask & (1 << bit)) != 0;
    report["reportedSequence"] = sequence; report["changedBy"] = reason;
    String payload; serializeJson(message, payload); payload += '\x1e';
    wsActuator.sendTXT(payload);
  }
}

static void updateThresholdsInNvs(float minPh, float maxPh, float minEc, float maxEc, float maxTemp, float maxHum) {
  bool changed = false;
  if (minPh >= 0.0f && maxPh <= 14.0f && maxPh > minPh && (fabsf(minPh - targetMinPh) > 0.02f || fabsf(maxPh - targetMaxPh) > 0.02f)) {
    targetMinPh = minPh;
    targetMaxPh = maxPh;
    changed = true;
  }
  if (minEc >= 0.0f && maxEc > minEc && (fabsf(minEc - targetMinEc) > 0.02f || fabsf(maxEc - targetMaxEc) > 0.02f)) {
    targetMinEc = minEc;
    targetMaxEc = maxEc;
    changed = true;
  }
  if (maxTemp >= -40.0f && maxTemp <= 80.0f && fabsf(maxTemp - targetMaxTemp) > 0.1f) {
    targetMaxTemp = maxTemp;
    changed = true;
  }
  if (maxHum >= 0.0f && maxHum <= 100.0f && fabsf(maxHum - targetMaxHum) > 0.5f) {
    targetMaxHum = maxHum;
    changed = true;
  }

  if (changed) {
    prefs.begin("smarthydro", false);
    prefs.putFloat("min_ph", targetMinPh);
    prefs.putFloat("max_ph", targetMaxPh);
    prefs.putFloat("min_ec", targetMinEc);
    prefs.putFloat("max_ec", targetMaxEc);
    prefs.putFloat("max_temp", targetMaxTemp);
    prefs.putFloat("max_hum", targetMaxHum);
    prefs.end();
    Serial.printf("[Recipe Thresholds] Updated: pH [%.2f-%.2f], EC [%.2f-%.2f], MaxTemp %.1fC, MaxHum %.0f%%\n",
                  targetMinPh, targetMaxPh, targetMinEc, targetMaxEc, targetMaxTemp, targetMaxHum);
  }
}

static bool startCommandDose(const char* name, uint32_t duration, const char* doseId) {
    startMicroDoseMs(name, duration, doseId, true);
    return lastDoseAccepted;
}
static void stopCommandDose(const char* name) { stopTowerDosingPump(resolveTowerDosePin(name)); }
static void applyCommandBody(JsonObject& state) {
    String calibrationCommand = state["cmd"] | (state["command"] | "");
    if (sensorCalibration.command(calibrationCommand, PIN_PH, PIN_EC, currentTelemetry.temp)) return;

  unsigned long now = millis();
  manualOverrideUntilMs = now + MANUAL_OVERRIDE_HOLD_MS;
  g_manualOverrideUntilMs = manualOverrideUntilMs;

  // Check for remote Operating Mode switch command ("mode": 0=LoRa+BLE, 1=WiFi Cloud, 2=SoftAP or string)
  if (state.containsKey("operatingMode") || state.containsKey("mode")) {
    int reqMode = -1;
    JsonVariant modeVar = state.containsKey("operatingMode") ? state["operatingMode"] : state["mode"];
    if (modeVar.is<int>()) {
      reqMode = modeVar.as<int>();
    } else {
      reqMode = shParseModeVariant(modeVar.as<String>());
    }
    if (reqMode >= 0 && reqMode <= 2) {
      switchOperatingMode(reqMode);
      return;
    }
  }

  // Optional Bit-Packed Relay State ("r") in downlink command
  if (state.containsKey("r") && !state.containsKey("ack")) {
    uint8_t rMask = (uint8_t)state["r"].as<int>();
    g_manualWaterPumpOverride = true;
    bool targetPump = ((rMask & 1) != 0);
    setRelay(PIN_RELAY_WATER_PUMP, targetPump);
    if (targetPump) {
      unsigned long durationSec = 0;
      if (state.containsKey("wt")) durationSec = state["wt"].as<unsigned long>();
      else if (state.containsKey("d")) durationSec = state["d"].as<unsigned long>();
      else if (state.containsKey("durationSeconds")) durationSec = state["durationSeconds"].as<unsigned long>();
      else if (state.containsKey("duration")) durationSec = state["duration"].as<unsigned long>();
      unsigned long durationMs = (durationSec > 0) ? (durationSec * 1000UL) : PUMP_ON_DURATION_MS;
      timedPumpEndMs = millis() + durationMs;
      pumpCycleStartMs = millis();
      Serial.printf("[Tower Pump] Water pump ON for %lu s timed burst.\n", durationMs / 1000UL);
    } else {
      timedPumpEndMs = 0;
      g_manualWaterPumpOverride = true;
      pumpCycleStartMs = millis() - PUMP_ON_DURATION_MS;
      Serial.println(F("[Tower Pump] Water pump manually turned OFF (override latched)."));
    }

    g_manualLightOverride = true;
    setRelay(PIN_RELAY_LIGHT, (rMask & 2) != 0);

    g_manualCircFanOverride = true;
    setRelay(PIN_RELAY_FAN_CIRC, (rMask & 4) != 0);

    g_manualExtFanOverride = true;
    setRelay(PIN_RELAY_FAN_EXT, (rMask & 8) != 0);
  }

  // Non-chemical actuators
  if (state.containsKey("growLight") || state.containsKey("GrowLight") || state.containsKey("gl") || state.containsKey("l")) {
    bool target = state.containsKey("gl") ? (state["gl"] == 1 || state["gl"] == true)
                : (state.containsKey("l") ? (state["l"] == 1 || state["l"] == true)
                : (state.containsKey("growLight") ? state["growLight"].as<bool>() : state["GrowLight"].as<bool>()));
    g_manualLightOverride = true;
    setRelay(PIN_RELAY_LIGHT, target);
  }

  if (state.containsKey("circulationFan") || state.containsKey("CirculationFan") || state.containsKey("fc")) {
    bool target = state.containsKey("fc") ? (state["fc"] == 1 || state["fc"] == true)
                : (state.containsKey("circulationFan") ? state["circulationFan"].as<bool>() : state["CirculationFan"].as<bool>());
    g_manualCircFanOverride = true;
    setRelay(PIN_RELAY_FAN_CIRC, target);
  }

  if (state.containsKey("extractorFan") || state.containsKey("ExtractorFan") || state.containsKey("fe")) {
    bool target = state.containsKey("fe") ? (state["fe"] == 1 || state["fe"] == true)
                : (state.containsKey("extractorFan") ? state["extractorFan"].as<bool>() : state["ExtractorFan"].as<bool>());
    g_manualExtFanOverride = true;
    setRelay(PIN_RELAY_FAN_EXT, target);
  }

  if (state.containsKey("mainWaterPump") || state.containsKey("MainWaterPump") || state.containsKey("waterPump") || state.containsKey("w") || state.containsKey("s")) {
    bool target = state.containsKey("w") ? (state["w"] == 1 || state["w"] == true)
                : (state.containsKey("s") ? (state["s"] == 1 || state["s"] == true)
                : (state.containsKey("mainWaterPump") ? state["mainWaterPump"].as<bool>()
                : (state.containsKey("MainWaterPump") ? state["MainWaterPump"].as<bool>() : state["waterPump"].as<bool>())));
    g_manualWaterPumpOverride = true;
    setRelay(PIN_RELAY_WATER_PUMP, target);
    if (target) {
      unsigned long durationSec = 0;
      if (state.containsKey("wt")) durationSec = state["wt"].as<unsigned long>();
      else if (state.containsKey("st")) durationSec = state["st"].as<unsigned long>();
      else if (state.containsKey("d")) durationSec = state["d"].as<unsigned long>();
      else if (state.containsKey("durationSeconds")) durationSec = state["durationSeconds"].as<unsigned long>();
      else if (state.containsKey("duration")) durationSec = state["duration"].as<unsigned long>();
      unsigned long durationMs = (durationSec > 0) ? (durationSec * 1000UL) : PUMP_ON_DURATION_MS;
      timedPumpEndMs = millis() + durationMs;
      pumpCycleStartMs = millis();
      Serial.printf("[Tower Pump] Water pump ON for %lu s timed burst.\n", durationMs / 1000UL);
    } else {
      timedPumpEndMs = 0;
      g_manualWaterPumpOverride = true;
      Serial.println(F("[Tower Pump] Water pump manually turned OFF."));
    }
  }

  g_requestedPhOverride = state["po"].is<bool>() && state["po"].as<bool>();
  commandDoseResult = shApplyDose(state, startCommandDose, stopCommandDose);
  g_requestedPhOverride = false;

  // Dynamic Threshold Sync (Supports full names, e0/e1/t1/h1, and LoraDownlinkQueue p0..p7 keys)
  float minPh = state["minPh"] | (state["p0"] | targetMinPh);
  float maxPh = state["maxPh"] | (state["p1"] | targetMaxPh);
  float minEc = state["minEc"] | (state["e0"] | (state["p2"] | targetMinEc));
  float maxEc = state["maxEc"] | (state["e1"] | (state["p3"] | targetMaxEc));
  float maxTemp = state["maxTemp"] | (state["t1"] | (state["p5"] | targetMaxTemp));
  float maxHum = state["maxHum"] | (state["h1"] | (state["p7"] | targetMaxHum));
  updateThresholdsInNvs(minPh, maxPh, minEc, maxEc, maxTemp, maxHum);

  updateOledDisplay();
  broadcastCompactJson(true);
}

#include "TowerCommandOutbox.h"
static SHTowerCommandOutbox<String> commandAckOutbox;
static uint8_t commandPhaseCode(const char* phase) {
    return strcmp(phase, "completed") == 0 ? 1 : strcmp(phase, "cancelled") == 0 ? 2 : 0;
}
#include "TowerReceiptStorage.h"
static bool canStoreCommandReceipt(uint16_t cid) {
    return outboxFilesReady && commandAckOutbox.canPut(cid);
}

static void sendCommandAck(SHCommandResult result, uint8_t source) {
    lastCommandReason = result.reason;
    if (source == 1) notifyBleCommandAck(result.cid, result.ok, result.reason, result.phase);
    commandHistory.save(result, source);
    // A phone's CID is local to BLE; it must not acknowledge a cloud request with the same number.
    if (source != 0) return;
    if (!result.cid) return;
    StaticJsonDocument<256> ack;
    ack["m"] = deviceMac; ack["cack"] = 1; ack["cid"] = result.cid;
    ack["ar"] = 1; // Ask the gateway to confirm that Azure accepted this reply.
    ack["ok"] = result.ok ? 1 : 0; ack["r"] = computeRelayBitmask();
    uint64_t sequence = nextRelayReportSequence();
    if (sequence) ack["rs"] = sequence;
    ack["md"] = g_operatingMode; ack["pm"] = g_connectivity.preferred; ack["mr"] = g_connectivity.reason; ack["reason"] = result.reason; ack["phase"] = result.phase;
    String json; serializeJson(ack, json);
    if (!commandAckOutbox.put(result.cid, commandPhaseCode(result.phase), json, millis()))
        Serial.printf("[Command ACK] Queue full; CID %u will be recovered on command retry.\n", result.cid);
    Serial.printf("[Command ACK] CID %u %s: %s, relay mask %u.\n", result.cid, result.phase, result.reason, computeRelayBitmask());
}

static void cancelActiveCommand() {
    if (activeCommandDose) {
        sendCommandAck({activeDoseCid, true, "cancelled", "cancelled"}, activeDoseSource);
        activeCommandDose = false;
        activeDoseCid = 0;
    }
}

static void serviceCommandCompletion() {
    if (activeCommandDose && (!g_activeDosePin || digitalRead(g_activeDosePin) == HIGH)) {
        bool staleCutoff = strcmp(doseSafety.reason(), "sensor_stale") == 0;
        sendCommandAck({activeDoseCid, true, staleCutoff ? "sensor_stale" : "timer_finished",
                        staleCutoff ? "cancelled" : "completed"}, activeDoseSource);
        activeCommandDose = false;
        activeDoseCid = 0;
    }
    auto* pending = commandAckOutbox.next(millis());
    if (!pending) return;
    bool sent = false;
    if (g_operatingMode == MODE_WIFI_CLOUD && isWsConnected && isWsHandshakeDone) {
        // Reuse the command connection. Its invocation reply confirms database storage.
        StaticJsonDocument<256> ack;
        if (deserializeJson(ack, pending->payload) == DeserializationError::Ok) {
            StaticJsonDocument<512> message;
            message["type"] = 1; message["target"] = "ReportCommandAck";
            message["invocationId"] = "ack-" + String(pending->cid) + "-" + String(pending->phase);
            message.createNestedArray("arguments").add(ack.as<JsonObject>());
            String wire; serializeJson(message, wire); wire += '\x1e'; wsActuator.sendTXT(wire);
        }
    } else if (g_operatingMode == MODE_WIFI_CLOUD && WiFi.status() == WL_CONNECTED && !g_activeDosePin) {
        if (beginOnlineRequest("/api/telemetry/lora", 1500)) {
            int status = towerOnlineHttp.POST(pending->payload);
            StaticJsonDocument<128> reply;
            sent = status == 200 && deserializeJson(reply, towerOnlineHttp.getString()) == DeserializationError::Ok && (reply["success"] | false);
            towerOnlineHttp.end();
        }
        // Wi-Fi association alone does not mean Azure is reachable.
        if (!sent) sendLoRaBridgePacket(pending->payload);
    } else {
        sendLoRaBridgePacket(pending->payload);
    }
    if (sent) commandAckOutbox.confirm(pending->cid, pending->phase);
    else commandAckOutbox.sent(*pending, millis());
}

static void handleReceiveActuatorState(JsonObject& cmd, uint8_t source = 0, uint32_t receivedAt = millis()) {
    lastCommandAccepted = false; lastCommandReason = "invalid_command";
    if (cmd["command"].is<JsonObject>()) {
        JsonObject nested = cmd["command"].as<JsonObject>();
        handleReceiveActuatorState(nested, source, receivedAt);
        return;
    }
    String calibration = cmd["cmd"] | (cmd["command"] | "");
    if (cmd.containsKey("m") && !isMatchingNodeMac(cmd["m"].as<String>())) return;
    if (cmd.containsKey("cid") && (!cmd["cid"].is<unsigned int>() || cmd["cid"].as<uint32_t>() > 65535)) {
        sendCommandAck({0, false, "invalid_cid", "accepted"}, source); return;
    }
    uint32_t requestedCid = cmd["cid"] | 0UL;
    if (requestedCid > 65535) return;
    uint16_t cid = (uint16_t)requestedCid;
    if (cmd.containsKey("po") && (!cmd["po"].is<bool>() || (cmd["po"].as<bool>() && !cid))) {
        sendCommandAck({cid, false, "invalid_command", "accepted"}, source); return;
    }
    if (auto* previous = commandHistory.find(cid, source)) { lastCommandAccepted = previous->ok; sendCommandAck(*previous, source); return; }
    if (source == 0 && cid) {
        if ((cmd.containsKey("ttl") && !cmd["ttl"].is<uint32_t>()) ||
            (cmd.containsKey("exp") && !cmd["exp"].is<uint32_t>())) {
            sendCommandAck({cid, false, "invalid_command", "accepted"}, source); return;
        }
        const char* expired = shCommandDeadlineReason(cmd["ttl"] | 60000UL, cmd["exp"] | 0UL,
                                                      receivedAt, millis(), telemetryUtc());
        if (expired) { sendCommandAck({cid, false, expired, "accepted"}, source); return; }
    }
    if (cmd["po"] | false) {
        const char* expired = shCommandDeadlineReason(cmd["ttl"] | 60000UL, cmd["exp"] | 0UL,
            receivedAt, millis(), telemetryUtc());
        if (expired || !verifiedPhOverride(cmd, cid)) {
            sendCommandAck({cid, false, expired ? expired : "invalid_override", "accepted"}, source); return;
        }
    }
    if (sensorCalibration.command(calibration, PIN_PH, PIN_EC, currentTelemetry.temp)) return;
    if (cmd.containsKey("mode") || cmd.containsKey("operatingMode")) {
        JsonVariant value = cmd.containsKey("operatingMode") ? cmd["operatingMode"] : cmd["mode"];
        int mode = value.is<int>() ? value.as<int>() : value.is<const char*>() ? shParseModeVariant(value.as<String>()) : -1;
        if (mode < 0 || mode > 2) { sendCommandAck({cid, false, "invalid_mode", "accepted"}, source); return; }
    }
    if (!shNormaliseCommand(cmd, computeRelayBitmask()) || !shHasCommandAction(cmd)) {
        sendCommandAck({cid, false, "invalid_command", "accepted"}, source); return;
    }
    uint8_t previousSource = auditSource; uint16_t previousCid = auditCid;
    auditSource = source; auditCid = cid;
    if (!shThresholdsValid(cmd, targetMinPh, targetMaxPh, targetMinEc, targetMaxEc)) {
        auditSource = previousSource; auditCid = previousCid;
        sendCommandAck({cid, false, "invalid_thresholds", "accepted"}, source); return;
    }
    manualOverrides.apply(cmd);
    commandDoseResult = 0;
    lastDoseRefusal = "invalid_dose";
    applyCommandBody(cmd);
    auditSource = previousSource; auditCid = previousCid;
    if (g_requestedConnectivityMode >= 0) serviceConnectivity();
    bool ok = commandDoseResult >= 0;
    if (g_modeSwitchFailure && (cmd.containsKey("mode") || cmd.containsKey("operatingMode"))) ok = false;
    lastCommandAccepted = ok;
    if (commandDoseResult > 0 && g_activeDosePin) {
        activeDoseCid = cid;
        activeDoseSource = source;
        activeCommandDose = true;
    }
    const char* failureReason = (cmd.containsKey("mode") || cmd.containsKey("operatingMode")) && g_modeSwitchFailure
        ? g_modeSwitchFailure : lastDoseRefusal;
    sendCommandAck({cid, ok, ok ? "executed" : failureReason, "accepted"}, source);
}
// Handle incoming JSON frames over UART1 from XIAO ESP32S3 + Wio-SX1262 LoRa Bridge
static void handleLoRaBridgeDownlink(const String& line) {
  StaticJsonDocument<512> doc;
  DeserializationError err = deserializeJson(doc, line);
  if (err != DeserializationError::Ok) {
    return;
  }

  // Capture XIAO ESP32S3 + Wio-SX1262 reception RSSI ("br") & SNR ("bs")
  if (doc.containsKey("br")) {
    g_xiaoLoraRssi = doc["br"].as<int>();
  }
  if (doc.containsKey("bs")) {
    g_xiaoLoraSnr = doc["bs"].as<float>();
  }

  // Verify target MAC address matches this Vertical Tower node (colon-agnostic)
  if (doc.containsKey("m")) {
    String targetMac = doc["m"].as<String>();
    if (targetMac.length() > 0 && !isMatchingNodeMac(targetMac)) {
      // Packet is addressed to the Fodder node; ignore on Tower
      return;
    }
  }

  // Check for Gateway ACK packet
  if ((doc["ack"] | 0) == 1) {
    if (doc["ac"].is<unsigned int>() && doc["ap"].is<unsigned int>()) {
      uint16_t cid = doc["ac"] | 0;
      commandAckOutbox.confirm(cid, doc["ap"] | 0);
      Serial.printf("[Command ACK] API storage confirmed for CID %u.\n", cid);
      return;
    }
    handleTelemetryReceipt(doc.as<JsonObject>());
    int upFlag = doc["up"] | 0;
    g_gatewayLoraRssi = doc["r"] | g_xiaoLoraRssi;
    if (upFlag == 1) {
      g_lastTxAcked = true;
      g_lastGatewayCloudUpMs = millis();
      g_connectivity.loraAck(true, millis());
      Serial.printf("[LoRa Bridge ACK] Gateway confirmed reception & cloud UP (up=1)! Gateway RSSI: %d dBm | XIAO Wio-SX1262 RSSI: %d dBm (Recovery ACKs: %u/3)\n",
                    g_gatewayLoraRssi, g_xiaoLoraRssi, (unsigned int)g_connectivity.goodAcks);
    } else {
      g_lastTxAcked = false;
      g_connectivity.loraAck(false, millis());
      Serial.printf("[LoRa Bridge ACK] Gateway RF link OK (GW: %d dBm | XIAO: %d dBm), but 4G cloud uplink is DOWN (up=0).\n",
                    g_gatewayLoraRssi, g_xiaoLoraRssi);
    }
    if (doc.containsKey("sync") && doc["sync"].is<JsonObject>()) {
      JsonObject syncObj = doc["sync"].as<JsonObject>();
      handleReceiveActuatorState(syncObj);
    }
    updateOledDisplay();
    return;
  }

  Serial.printf("[LoRa Downlink] <<< Applying command from Gateway (XIAO RSSI: %d dBm): %s\n",
                g_xiaoLoraRssi, line.c_str());
  JsonObject obj = doc.as<JsonObject>();
  handleReceiveActuatorState(obj);
}

// Handle incoming BLE Write commands on Characteristic 0xFF02 from SmartHydro_Android
static void handleQueuedCommandPayload(const String& payload, uint8_t source, uint32_t receivedAt) {
    if (sensorCalibration.command(payload, PIN_PH, PIN_EC, currentTelemetry.temp)) return;
  StaticJsonDocument<256> doc;
  DeserializationError err = deserializeJson(doc, payload);
  if (err != DeserializationError::Ok) {
    doc.clear(); doc["cmd"] = payload;
  }
  JsonObject obj = doc.as<JsonObject>();
  handleReceiveActuatorState(obj, source, receivedAt);
}

// ------------------------------------------------------------------------------
// Local Hotspot / Emergency SoftAP Control Routes (Non-Blocking & Glitch-Free)
// ------------------------------------------------------------------------------
String localRelaysJson() {
  StaticJsonDocument<256> state;
  shLocalRelayFields(state.to<JsonObject>(), computeRelayBitmask());
  String json; serializeJson(state, json); return json;
}

static void sendLocalControlReply(bool ok, const char* reason, uint32_t duration = 0) {
    StaticJsonDocument<640> reply;
    uint8_t mask = computeRelayBitmask();
    reply["ok"] = ok; reply["r"] = mask;
    shLocalRelayFields(reply.createNestedObject("relays"), mask);
    reply["reason"] = reason;
    reply["message"] = ok ? "The controller has applied the request. Relay output states are shown in the reply." : shLocalRefusalMessage(reason);
    if (duration) reply["durationMs"] = duration;
    String json; serializeJson(reply, json);
    server.send(ok ? 200 : 409, "application/json", json);
}

static void serviceRelayReports() {
    uint8_t mask = computeRelayBitmask();
    relays.waterPump = mask & 1; relays.growLight = mask & 2;
    relays.circulationFan = mask & 4; relays.extractorFan = mask & 8;
    relays.phUp = mask & 16; relays.phDown = mask & 32;
    relays.ecUp = mask & 64; relays.ecDown = mask & 128;
    if (g_hasLocalRelayReport && mask != g_lastLocalRelayReport) broadcastCompactJson(true);
    g_lastLocalRelayReport = mask; g_hasLocalRelayReport = true;
}

static bool isKnownRelay(const char* name) {
  static const char* const names[] = {"growLight", "circulationFan", "mainWaterPump", "extractorFan",
                                      "phUpPump", "phDownPump", "nutrientUpPump", "dilutionDownPump",
                                      "phUp", "phDown", "ecUp", "ecDown", "waterPump"};
  for (const char* known : names) {
    if (strcasecmp(name, known) == 0) return true;
  }
  return false;
}

static void runPendingLocalActions() {
  if (g_hasPendingSoftApRelay) {
    g_hasPendingSoftApRelay = false;
    StaticJsonDocument<96> change;
    JsonObject one = change.to<JsonObject>();
    one[g_pendingSoftApRelayName] = g_pendingSoftApRelayState;
    handleReceiveActuatorState(one, 2);
  }
  if (g_localDosePump.length() > 0) {
    String pump = g_localDosePump;
    unsigned long dMs = g_localDoseMs > 0 ? g_localDoseMs : MANUAL_DEFAULT_DOSE_MS;
    g_localDosePump = "";
    g_localDoseMs = 0;
    auditSource = 2; startMicroDoseMs(pump.c_str(), dMs, "", true); auditSource = 5;
  }
}

void registerLocalControlRoutes() {
  // Android receives the GPIO snapshot after the request has been applied.
  server.on("/api/control", HTTP_POST, []() {
    StaticJsonDocument<512> doc;
    if (deserializeJson(doc, server.arg("plain")) != DeserializationError::Ok) {
        server.send(400, "application/json", "{\"ok\":false,\"reason\":\"invalid_json\"}"); return;
    }
    JsonObject command = doc.as<JsonObject>();
    handleReceiveActuatorState(command, 2);
    sendLocalControlReply(lastCommandAccepted, lastCommandReason);
  });

  // 2. Local dose requests
  server.on("/api/dose", HTTP_POST, []() {
    StaticJsonDocument<192> doc;
    DeserializationError err = deserializeJson(doc, server.arg("plain"));
    const char* type = err ? "" : (doc["type"] | (doc["actuatorName"] | (doc["dt"] | "")));

    if (resolveTowerDosePin(type) == 0) {
      server.send(400, "application/json", "{\"ok\":false,\"message\":\"Unknown dosing pump type.\"}");
      return;
    }

    uint32_t durMs;
    if (!shRequestedDoseMs(doc.as<JsonObject>(), durMs)) {
        server.send(400, "application/json", "{\"ok\":false,\"reason\":\"invalid_dose\"}"); return;
    }
    auditSource = 2; startMicroDoseMs(type, durMs, "", true); auditSource = 5;
    broadcastCompactJson(true);
    sendLocalControlReply(lastDoseAccepted, lastDoseAccepted ? "accepted" : lastDoseRefusal,
                          lastDoseAccepted ? activeAuditDuration : 0);
  });

  // Keep the legacy URLs, but report GPIO read-back after applying each request.
  const char* routes[] = {"/light", "/fan", "/extract", "/pump"};
  const char* keys[] = {"growLight", "circulationFan", "extractorFan", "mainWaterPump"};
  const uint8_t bits[] = {2, 4, 8, 1};
  for (int i = 0; i < 4; ++i) {
    const char* key = keys[i]; uint8_t bit = bits[i];
    server.on(routes[i], HTTP_GET, [key, bit]() {
      StaticJsonDocument<96> request;
      request[key] = (computeRelayBitmask() & bit) == 0;
      JsonObject command = request.as<JsonObject>(); handleReceiveActuatorState(command, 2);
      sendLocalControlReply(lastCommandAccepted, lastCommandReason);
    });
  }
  const char* doseRoutes[] = {"/phUp", "/phDown", "/ecUp", "/ecDown"};
  const char* doseNames[] = {"PH_UP", "PH_DOWN", "NUTRIENT_UP", "DILUTION_DOWN"};
  for (int i = 0; i < 4; ++i) {
    const char* pump = doseNames[i];
    server.on(doseRoutes[i], HTTP_GET, [pump]() {
      auditSource = 2; startMicroDoseMs(pump, MS_PER_ML, "", true); auditSource = 5;
      broadcastCompactJson(true);
      sendLocalControlReply(lastDoseAccepted, lastDoseAccepted ? "accepted" : lastDoseRefusal,
                            lastDoseAccepted ? activeAuditDuration : 0);
    });
  }
  server.on("/phOff", HTTP_GET, []() {
    uint8_t previous = auditSource; auditSource = 2;
    stopTowerDosingPump(PIN_DOSE_PH_UP); stopTowerDosingPump(PIN_DOSE_PH_DOWN);
    auditSource = previous; broadcastCompactJson(true); sendLocalControlReply(true, "stopped");
  });
  server.on("/ecOff", HTTP_GET, []() {
    uint8_t previous = auditSource; auditSource = 2;
    stopTowerDosingPump(PIN_DOSE_EC_UP); stopTowerDosingPump(PIN_DOSE_EC_DOWN);
    auditSource = previous; broadcastCompactJson(true); sendLocalControlReply(true, "stopped");
  });
  server.on("/components", HTTP_GET, []() { server.send(200, "application/json", localRelaysJson()); });
}

// ------------------------------------------------------------------------------
// SignalR WebSocket Event Handler (Online Mode 1: MODE_WIFI_CLOUD)
// ------------------------------------------------------------------------------
static void handleReceiveTimedCommand(JsonObject& cmd) {
  const char* actName = cmd["actuatorName"] | (cmd["ActuatorName"] | (cmd["dosingType"] | ""));
  unsigned long durMs = cmd["durationMs"] | (cmd["DurationMs"] | 0UL);
  int duration = cmd["durationSeconds"] | (cmd["DurationSeconds"] | 8);
  const char* cmdId = cmd["commandId"] | (cmd["CommandId"] | "");

  if (strcasecmp(actName, "MainWaterPump") == 0 || strcasecmp(actName, "WaterPump") == 0) {
    setRelay(PIN_RELAY_WATER_PUMP, true);
    timedPumpEndMs = millis() + ((unsigned long)duration * 1000UL);
    broadcastCompactJson(true);
  } else {
    if (durMs == 0) durMs = (unsigned long)duration * 1000UL;
    startMicroDoseMs(actName, durMs, cmdId, true);
  }
}

static void onActuatorWsEvent(const WStype_t& type, uint8_t * payload, const size_t& length) {
  inWsCallback = true;
  switch (type) {
    case WStype_DISCONNECTED:
      isWsConnected = false;
      isWsHandshakeDone = false;
      break;
    case WStype_CONNECTED:
      isWsConnected = false;
      isWsHandshakeDone = false;
      wsActuator.sendTXT("{\"protocol\":\"json\",\"version\":1}\x1e");
      break;
    case WStype_TEXT: {
      if (payload == nullptr || length == 0) break;
      String msg;
      msg.reserve(length + 1);
      msg.concat((const char*)payload, length);
      int startIdx = 0;
      while (startIdx < msg.length()) {
        int recordEnd = msg.indexOf('\x1e', startIdx);
        if (recordEnd == -1) recordEnd = msg.length();
        String record = msg.substring(startIdx, recordEnd);
        record.trim();
        startIdx = recordEnd + 1;
        if (record.length() == 0) continue;

        if (record == "{}") {
          String regPayload = "{\"type\":1,\"invocationId\":\"register\",\"target\":\"RegisterDevice\",\"arguments\":[\"" + deviceMac + "\"]}\x1e";
          wsActuator.sendTXT(regPayload);
          continue;
        }

        StaticJsonDocument<1024> doc;
        if (deserializeJson(doc, record) == DeserializationError::Ok) {
          int msgType = doc["type"] | 0;
          if (msgType == 3 && String(doc["invocationId"] | "") == "register") {
            isWsConnected = !doc.containsKey("error");
            isWsHandshakeDone = isWsConnected;
            if (isWsConnected) {
              g_connectivity.cloudSuccess(millis());
              Serial.println("[Online] Device registered on the secure command channel.");
            }
            else Serial.println("[Online] Device registration refused; command channel is unavailable.");
          } else if (msgType == 3 && doc["result"].is<bool>() && doc["result"].as<bool>()) {
            String invocation = doc["invocationId"] | "";
            int separator = invocation.lastIndexOf('-');
            if (invocation.startsWith("ack-") && separator > 4) {
              uint16_t cid = invocation.substring(4, separator).toInt();
              commandAckOutbox.confirm(cid, invocation.substring(separator + 1).toInt());
              Serial.printf("[Command ACK] API confirmed CID %u over Wi-Fi.\n", cid);
            }
          } else if (msgType == 6) {
            // Server keep-alive is one-way; our periodic ping keeps this side alive.
          } else if (msgType == 1) {
            const char* target = doc["target"];
            if (target && (strcmp(target, "ReceiveActuatorState") == 0 || strcmp(target, "ReceiveCommand") == 0)) {
              JsonObject state = doc["arguments"][0];
              if (!state.isNull()) {
                String queued; serializeJson(state, queued);
                if (!queueControlPayload(queued, 0)) {
                    uint16_t cid = state["cid"].is<unsigned int>() && state["cid"].as<uint32_t>() <= 65535
                        ? state["cid"].as<uint16_t>() : 0;
                    sendCommandAck({cid, false, queued.length() >= 512 ? "command_too_large" : "controller_busy", "accepted"});
                }
              }
            } else if (target && (strcmp(target, "ReceiveOfflineTransition") == 0 || strcmp(target, "receiveOfflineTransition") == 0)) {
              switchOperatingMode(MODE_EMERGENCY_SOFTAP);
            } else if (target && (strcmp(target, "ReceiveOnlineTransition") == 0 || strcmp(target, "receiveOnlineTransition") == 0)) {
              switchOperatingMode(MODE_WIFI_CLOUD);
            } else if (target && (strcmp(target, "ReceiveLoraTransition") == 0 || strcmp(target, "receiveLoraTransition") == 0)) {
              switchOperatingMode(MODE_LORA_BLE);
            } else if (target && strcmp(target, "ReceiveTimedCommand") == 0) {
              JsonObject cmd = doc["arguments"][0];
              if (!cmd.isNull()) handleReceiveTimedCommand(cmd);
            }
          }
        }
      }
      break;
    }
    default:
      break;
  }
  inWsCallback = false;
}

static void initActuatorWebSocket() {
  String host = getApiHostName();
  int port = getApiPort();
  wsActuator.beginSslWithCA(host.c_str(), port, "/hubs/actuators", GATEWAY_ROOT_CA);
  static String deviceHeaders = String("X-SmartHydro-Device-Key: ") + shDeviceKey();
    wsActuator.setExtraHeaders(deviceHeaders.c_str());
    wsActuator.onEvent(onActuatorWsEvent);
  wsActuator.setReconnectInterval(5000);
}

// ------------------------------------------------------------------------------
// Sensor Acquisition & Calibration (DFRobot pH V2 and TDS V1)
// ------------------------------------------------------------------------------
static int analogReadAvg(int pin, uint8_t samples = 10) {
  long sum = 0;
  for (uint8_t i = 0; i < samples; i++) {
    sum += analogRead(pin);
    delayMicroseconds(250);
  }
  return (int)(sum / samples);
}








static void readSensors() {
    float temperature = dht.readTemperature();
    float humidity = dht.readHumidity();
    bool validDht = sensorCalibration.dhtValid(temperature, humidity);
    currentTelemetry.temp = validDht ? temperature : -99.0f;
    currentTelemetry.hum = validDht ? humidity : -1.0f;
    if (!validDht && sensorCalibration.faultDHT) dht.begin();

    currentTelemetry.lux = sensorCalibration.readLight(PIN_LIGHT);
    int waterADC = analogReadAvg(PIN_WATER, 10);
    currentTelemetry.waterRaw = waterADC;
    if (waterADC >= WATER_THRESHOLD_HIGH) currentTelemetry.waterOk = true;
    else if (waterADC < WATER_THRESHOLD_LOW) currentTelemetry.waterOk = false;

    currentTelemetry.ph = sensorCalibration.readPh(PIN_PH);
    currentTelemetry.ec = sensorCalibration.readEc(PIN_EC, currentTelemetry.temp);
    g_sensorFaultActive = currentTelemetry.ph < 0.0f || currentTelemetry.ec < 0.0f;
    bool doseSampleValid = isfinite(currentTelemetry.ph) && isfinite(currentTelemetry.ec) &&
        currentTelemetry.ph >= PH_PLAUSIBLE_MIN && currentTelemetry.ph <= PH_PLAUSIBLE_MAX &&
        currentTelemetry.ec >= EC_PLAUSIBLE_MIN && currentTelemetry.ec <= EC_PLAUSIBLE_MAX;
    sensorCalibration.doseGuard.observe(doseSampleValid, millis());
    sensorCalibration.ecDoseGuard.observe(isfinite(currentTelemetry.ec) && currentTelemetry.ec >= EC_PLAUSIBLE_MIN &&
        currentTelemetry.ec <= EC_PLAUSIBLE_MAX && !sensorCalibration.faultEC, millis());
    // Stop on the first invalid sample, before considering any new automatic dose.
    if (g_activeDosePin) checkDosingProgress();
    if (g_activeDosePin && !doseSafety.refreshSensorWindow(g_activePhOverride ? manualPhOverrideWindow() : sensorCalibration.doseGuard.freshFor(millis()))) {
        recordActivity(g_activeDosePin, 6, 6, activeAuditDuration, activeAuditCid, 7);
        stopTowerDosingPump(g_activeDosePin);
    }
    static unsigned long lastSensorDiagnosticMs = 0;
    static int previousPhValidity = -1;
    static const char* previousPhReason = "not_sampled";
    int phValidity = currentTelemetry.ph >= 0 ? 1 : 0;
    if (phValidity != previousPhValidity || strcmp(previousPhReason, sensorCalibration.phReason) != 0 ||
        millis() - lastSensorDiagnosticMs >= 15000UL) {
        previousPhValidity = phValidity;
        previousPhReason = sensorCalibration.phReason;
        lastSensorDiagnosticMs = millis();
        const char* guardReason = sensorCalibration.doseGuard.refusal(millis());
        Serial.printf("[Sensors] pH %.2f (%.1f mV) | EC %.3f mS/cm (%.1f mV) | Temp %.1f C | Hum %.1f %% | Light %.1f %% | Water %s | Dose readings: %s | pH calibration: %s\n",
            currentTelemetry.ph, sensorCalibration.phMv, currentTelemetry.ec, sensorCalibration.ecMv,
            currentTelemetry.temp, currentTelemetry.hum, currentTelemetry.lux,
            currentTelemetry.waterOk ? "OK" : "LOW", guardReason ? guardReason : "ready",
            sensorCalibration.hasPhCalibration() ? "two-point" : "Gravity V2 factory estimate (uncalibrated)");
        Serial.printf("[pH Check] GPIO%d reason=%s mV batch: avg=%.1f min=%lu max=%lu span=%lu limit=%lu mV; later raw batch: avg=%lu min=%lu max=%lu counts; reference points=%.1f/%.1f mV relay mask=0x%02X\n",
            PIN_PH, sensorCalibration.phReason, sensorCalibration.phMv,
            (unsigned long)sensorCalibration.phMvMin, (unsigned long)sensorCalibration.phMvMax,
            (unsigned long)(sensorCalibration.phMvMax - sensorCalibration.phMvMin),
            (unsigned long)SH_TOWER_PH_MAX_SPAN_MV, (unsigned long)sensorCalibration.phRaw,
            (unsigned long)sensorCalibration.phRawMin, (unsigned long)sensorCalibration.phRawMax,
            sensorCalibration.phV7, sensorCalibration.phV4, computeRelayBitmask());
    }

}

// ------------------------------------------------------------------------------
// Hybrid User Threshold + Embedded Random Forest ML Autonomous Engine
// ------------------------------------------------------------------------------
static void executeAutonomousML() {
    auditSource = 3;
    g_inDiffusionLockout = doseSafety.remaining() > 0;
  unsigned long now = millis();
  if (now < 10000UL) return; // 10s sensor stabilization grace period at boot

  // Always compute ML predictions for telemetry analytics
  float ecFeature[1] = {currentTelemetry.ec};
  float phFeature[1] = {currentTelemetry.ph};
  float tempFeature[1] = {currentTelemetry.temp};
  float humFeature[1] = {currentTelemetry.hum};

  currentTelemetry.mlEc = modelEC.predict(ecFeature);
  currentTelemetry.mlPh = modelPH.predict(phFeature);
  currentTelemetry.mlTemp = modelTemp.predict(tempFeature);
  currentTelemetry.mlHum = modelHum.predict(humFeature);

  g_manualWaterPumpOverride = manualOverrides.active(0);
  g_manualLightOverride = manualOverrides.active(1);
  g_manualCircFanOverride = manualOverrides.active(2);
  g_manualExtFanOverride = manualOverrides.active(3);

  // 1. Climate Fans (Hybrid ML + User Thresholds, Bypasses Chemical Lockouts)
  bool highTemp = currentTelemetry.temp != -99.0f && ((currentTelemetry.temp > targetMaxTemp) || (currentTelemetry.temp > 27.0f) ||
                  (fabsf(targetMaxTemp - 25.0f) < 0.2f && currentTelemetry.mlTemp == 0));
  bool highHum = currentTelemetry.hum >= 0.0f && ((currentTelemetry.hum > targetMaxHum) || (currentTelemetry.hum > 75.0f) ||
                 (fabsf(targetMaxHum - 75.0f) < 0.5f && currentTelemetry.mlHum == 0));

  if (!g_manualCircFanOverride && currentTelemetry.temp != -99.0f && currentTelemetry.hum >= 0.0f) {
    bool circTarget = highTemp || highHum;
    if (relays.circulationFan != circTarget) {
      setRelay(PIN_RELAY_FAN_CIRC, circTarget);
    }
  }

  if (!g_manualExtFanOverride && currentTelemetry.temp != -99.0f && currentTelemetry.hum >= 0.0f) {
    bool extTarget = highHum || (currentTelemetry.temp > 27.0f);
    if (relays.extractorFan != extTarget) {
      setRelay(PIN_RELAY_FAN_EXT, extTarget);
    }
  }

  auditSource = 5;
  // 2. Strict 16 Hours ON / 8 Hours OFF Non-Dynamic Grow Light Schedule
  //    (Never dynamically turned off by the LDR sensor)
  if (!g_manualLightOverride) {
    unsigned long lightElapsed = (now - lightCycleStartMs) % LIGHT_CYCLE_TOTAL_MS;
    bool lightTarget = (lightElapsed < LIGHT_ON_DURATION_MS);
    if (relays.growLight != lightTarget) {
      setRelay(PIN_RELAY_LIGHT, lightTarget);
    }
  }

  // 3. Main Irrigation Water Pump: 15 Minutes ON / 45 Minutes OFF + Mixing Interlock
  //    (Operates unconditionally even when water level sensor reports low)
  if (!g_manualWaterPumpOverride && timedPumpEndMs == 0) {
    if (g_inDiffusionLockout) {
      setRelay(PIN_RELAY_WATER_PUMP, true);
    } else {
      unsigned long pumpElapsed = (now - pumpCycleStartMs) % PUMP_CYCLE_TOTAL_MS;
      bool pumpTarget = (pumpElapsed < PUMP_ON_DURATION_MS);
      if (relays.waterPump != pumpTarget) {
        setRelay(PIN_RELAY_WATER_PUMP, pumpTarget);
      }
    }
  }

  // The edge owns dosing in every mode; cloud recommendations never add a second dose.
  if (g_activeDosePin == 0) {
    SHDosePlan plan = shPlanDose(currentTelemetry.ph, targetMinPh, targetMaxPh, currentTelemetry.mlPh, true);
    if (!plan.pump) plan = shPlanDose(currentTelemetry.ec, targetMinEc, targetMaxEc, currentTelemetry.mlEc, false);
    if (plan.pump && doseSafety.remaining() == 0) {
      const char* pumps[] = {"", "PH_UP", "PH_DOWN", "NUTRIENT_UP", "DILUTION_DOWN"};
      auditSource = plan.source;
      startMicroDoseMs(pumps[plan.pump], plan.duration, "", false);
      auditSource = 5;
    }
  }
  // Broadcast immediately if any schedule or ML rule changed a relay state
  broadcastCompactJson(false);
}

// ------------------------------------------------------------------------------
// OLED Splash Screen, Mode Banner & Dual-Screen Rotating Display (With XIAO RSSI)
// ------------------------------------------------------------------------------
#define LOGO_WIDTH 32
#define LOGO_HEIGHT 36

const unsigned char PROGMEM smarthydro_logo_bmp[] = {
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00,
    0x00, 0x00, 0x01, 0xC0, 0x00, 0x00, 0x00, 0x03, 0x60, 0x00, 0x00, 0x00,
    0x02, 0x20, 0x00, 0x00, 0x00, 0x06, 0x30, 0x00, 0x00, 0x00, 0x0C, 0x18,
    0x00, 0x00, 0x00, 0x18, 0x0C, 0x00, 0x00, 0x00, 0x30, 0x06, 0x00, 0x00,
    0x00, 0x21, 0xC2, 0x00, 0x00, 0x00, 0x61, 0xC3, 0x00, 0x00, 0x00, 0xC4,
    0x91, 0x80, 0x00, 0x01, 0x8F, 0xF8, 0x80, 0x00, 0x01, 0x89, 0xD8, 0xC0,
    0x00, 0x03, 0x07, 0xF0, 0x60, 0x00, 0x03, 0x03, 0xE0, 0x60, 0x00, 0x02,
    0x00, 0x80, 0x20, 0x00, 0x06, 0x0E, 0xB8, 0x30, 0x00, 0x06, 0x09, 0xC8,
    0x30, 0x00, 0x06, 0x05, 0xD0, 0x30, 0x00, 0x06, 0x03, 0xE0, 0x30, 0x00,
    0x06, 0x00, 0x80, 0x30, 0x00, 0x06, 0x0E, 0xB8, 0x30, 0x00, 0x02, 0x0B,
    0xE8, 0x20, 0x00, 0x03, 0x0D, 0xD8, 0x60, 0x00, 0x01, 0x07, 0xF0, 0x40,
    0x00, 0x01, 0x80, 0x80, 0xC0, 0x00, 0x00, 0xDF, 0x81, 0x80, 0x00, 0x00,
    0x7F, 0xFF, 0x00, 0x00, 0x00, 0x3F, 0xFE, 0x00, 0x00, 0x00, 0x0F, 0xF8,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
};

static void showSplashScreen() {
  if (!hasOled) return;
  const int totalSteps = 12;
  for (int step = 1; step <= totalSteps; step++) {
    feedWatchdog();
    display.clearDisplay();
    display.setTextColor(SSD1306_WHITE);
    display.drawBitmap(6, 4, smarthydro_logo_bmp, LOGO_WIDTH, LOGO_HEIGHT, SSD1306_WHITE);

    display.setTextSize(1);
    display.setCursor(44, 6);
    display.print(F("SMART"));
    display.setCursor(44, 18);
    display.print(F("HYDRO"));
    display.setCursor(44, 30);
    display.print(F("ESP32-C6"));

    display.drawRoundRect(4, 45, 120, 7, 2, SSD1306_WHITE);
    int barWidth = (116 * step) / totalSteps;
    if (barWidth > 0) {
      display.fillRoundRect(6, 47, barWidth, 3, 1, SSD1306_WHITE);
    }
    display.setCursor(10, 55);
    display.print(F("INIT LORA+BLE C6..."));
    display.display();
    feedWatchdog();
    delay(60);
  }
}

static void showModeBootBanner() {
  if (!hasOled) return;
  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.setTextSize(1);

  if (g_operatingMode == MODE_LORA_BLE) {
    display.setCursor(6, 2);
    display.print(F("[ MODE: LORA + BLE ]"));
    display.drawLine(0, 12, 127, 12, SSD1306_WHITE);
    display.setCursor(0, 16);
    display.print(F("Primary: 868.1MHz P2P"));
    display.setCursor(0, 28);
    display.print(F("BLE 5.0: SmartHydro"));
    display.setCursor(0, 40);
    display.print(F("Wi-Fi:   STANDBY OFF"));
    display.setCursor(0, 53);
    display.print(F("Hold BOOT 3s->SoftAP"));
  } else if (g_operatingMode == MODE_EMERGENCY_SOFTAP) {
    display.setCursor(4, 2);
    display.print(F("[ EMERGENCY SOFTAP ]"));
    display.drawLine(0, 12, 127, 12, SSD1306_WHITE);
    display.setCursor(0, 16);
    display.print(F("AP: SmartHydro-Tower"));
    display.setCursor(0, 28);
    display.print(F("IP: 192.168.4.1"));
    display.setCursor(0, 40);
    display.print(F("LoRa+BLE: ACTIVE"));
    display.setCursor(0, 53);
    display.print(F("Hold BOOT 3s->LoRa"));
  } else {
    display.setCursor(6, 2);
    display.print(F("[ MODE: CLOUD WIFI ]"));
    display.drawLine(0, 12, 127, 12, SSD1306_WHITE);
    display.setCursor(0, 16);
    display.print(F("Wi-Fi + SignalR WS"));
    display.setCursor(0, 28);
    display.print(F("LoRa 868M: REDUNDANT"));
    display.setCursor(0, 40);
    display.print(F("BLE 5.0:   ACTIVE"));
    display.setCursor(0, 53);
    display.print(F("Hold BOOT 3s->SoftAP"));
  }
  display.display();
  delay(2000);
}

static void updateOledDisplay() {
  if (!hasOled) return;
    if (blePairingVisible()) {
        display.clearDisplay(); display.setTextColor(SSD1306_WHITE);
        display.setTextWrap(true);
        display.setTextSize(1); display.setCursor(0, 0);
        // The full advertised name can wrap onto a second line on 128x64 OLEDs.
        display.print(g_bleDeviceName); display.setCursor(0, 24);
        display.print("Enter code on phone"); display.setCursor(0, 40);
        display.setTextSize(2);
        display.printf("%06lu", (unsigned long)g_blePasskey);
        display.setTextSize(1);
        display.display(); return;
    }


  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.setTextSize(1);
  display.setTextWrap(false);

  // Line 0: Header & Connectivity Status
  display.setCursor(0, 0);
  display.print(F("TOWER C6"));
  display.setCursor(64, 0);
  if (g_operatingMode == MODE_EMERGENCY_SOFTAP) {
    display.print(F("[SoftAP]"));
  } else if (g_operatingMode == MODE_WIFI_CLOUD) {
    display.print(F("[WiFi]"));
  } else {
    display.print(isTowerBleConnected() ? F("BLE:CONN") : F("[LoRa+BLE]"));
  }
  display.drawLine(0, 10, 127, 10, SSD1306_WHITE);

  // Full controller identity fits on one 128-pixel row at text size 1.
  display.setCursor(0, 12);
  display.print(deviceMac);

  // Two sensor pages keep the pH history and signal diagnostic readable without
  // overwriting environmental readings or presenting an old value as live.
  bool chemistryPage = (millis() / OLED_PAGE_INTERVAL_MS) % 2 == 0;
  if (chemistryPage) {
    uint64_t now = (uint64_t)esp_timer_get_time() / 1000ULL;
    bool currentPh = isfinite(currentTelemetry.ph) && currentTelemetry.ph >= 0 && currentTelemetry.ph <= 14;
    bool previousPh = !currentPh && sensorCalibration.lastPh.hasReading(now);
    display.setCursor(0, 23);
    if (currentPh) display.printf("pH:%.2f", currentTelemetry.ph);
    else if (previousPh) display.printf("Last pH:%.2f", sensorCalibration.lastPh.value());
    else display.print(F("pH: no valid reading"));

    display.setCursor(0, 34);
    if (isfinite(currentTelemetry.ec) && currentTelemetry.ec >= 0)
      display.printf("EC:%.2f mS/cm", currentTelemetry.ec);
    else display.print(F("EC: unavailable"));

    display.setCursor(0, 45);
    if (previousPh) {
      uint64_t minutes = sensorCalibration.lastPh.ageMs(now) / 60000ULL;
      display.printf("%lum ago %.0fmV", (unsigned long)minutes, sensorCalibration.phMv);
    } else {
      display.printf("pH signal:%.1fmV", sensorCalibration.phMv);
    }
  } else {
    display.setCursor(0, 23);
    if (sensorCalibration.faultDHT) display.print(F("Tmp:--C Hum:--%"));
    else display.printf("Tmp:%.1fC Hum:%.0f%%", currentTelemetry.temp, currentTelemetry.hum);

    display.setCursor(0, 34);
    if (sensorCalibration.faultLight) display.print(F("Light:unavailable"));
    else display.printf("Light:%.0f%%", currentTelemetry.lux);
    display.setCursor(0, 45);
    display.printf("Water:%s", currentTelemetry.waterOk ? "OK" : "LOW");
  }

  // Line 5: Gateway and XIAO LoRa RSSI
  display.setCursor(0, 56);
  if (g_gatewayLoraRssi != 0 && g_xiaoLoraRssi != 0) {
    display.printf("GW:%ddBm XI:%ddBm", g_gatewayLoraRssi, g_xiaoLoraRssi);
  } else if (g_gatewayLoraRssi != 0) {
    display.printf("GW:%ddBm", g_gatewayLoraRssi);
  } else if (g_xiaoLoraRssi != 0) {
    display.printf("XIAO:%ddBm", g_xiaoLoraRssi);
  } else {
    display.print(F("LoRa RSSI: waiting"));
  }

  display.display();
}

// ------------------------------------------------------------------------------
// Tri-Mode Runtime Switcher (Mode 0: LoRa+BLE, Mode 1: Cloud WiFi, Mode 2: Emergency SoftAP)
// ------------------------------------------------------------------------------
void switchOperatingMode(int newMode) {
  requestConnectivityMode(newMode);
}

// ------------------------------------------------------------------------------
// Hardware Initialization (Boot-Glitch Suppression)
// ------------------------------------------------------------------------------
static void initHardware() {
  // Ensure strapping pins (GPIO 9 BOOT, GPIO 8) are pulled HIGH immediately on startup
  pinMode(9, INPUT_PULLUP);
  pinMode(8, INPUT_PULLUP);

  // 1. Boot-Glitch Elimination: Pre-charge internal pull-ups and write HIGH before OUTPUT
  const uint8_t outputPins[] = {PIN_RELAY_WATER_PUMP, PIN_RELAY_LIGHT,
                                PIN_RELAY_FAN_CIRC,   PIN_RELAY_FAN_EXT,
                                PIN_DOSE_PH_UP,       PIN_DOSE_PH_DOWN,
                                PIN_DOSE_EC_UP,       PIN_DOSE_EC_DOWN};
  for (uint8_t pin : outputPins) {
    pinMode(pin, INPUT_PULLUP);
    digitalWrite(pin, HIGH); // De-energized (Active-LOW)
    pinMode(pin, OUTPUT);
    digitalWrite(pin, HIGH);
  }

  // 2. Configure 12-Bit ADC1 with 11dB Attenuation (0 - 3.1V range across all ADC channels)
  sensorCalibration.begin();
    analogReadResolution(12);
    analogSetAttenuation(ADC_11db);
    analogRead(PIN_PH); analogRead(PIN_EC); analogRead(PIN_LIGHT);
    analogSetPinAttenuation(PIN_PH, ADC_11db);
    analogSetPinAttenuation(PIN_EC, ADC_11db);
    analogSetPinAttenuation(PIN_LIGHT, ADC_11db);
  analogSetAttenuation(ADC_11db);

  // 3. Initialize DHT22 on GPIO 15 with pull-up
  pinMode(PIN_DHT, INPUT_PULLUP);
  dht.begin();

  // 4. Initialize Inter-MCU Hardware UART1 Bridge to XIAO ESP32S3 (TX1=16, RX1=17)
  LoRaBridgeSerial.setRxBufferSize(512);
  LoRaBridgeSerial.begin(LORA_UART_BAUD, SERIAL_8N1, PIN_LORA_UART_RX, PIN_LORA_UART_TX);
  g_loraUartRxBuf.reserve(256);
  Serial.printf("[UART1] Bridge to XIAO ESP32S3 initialized at %d baud (TX=GPIO%d, RX=GPIO%d).\n",
                LORA_UART_BAUD, PIN_LORA_UART_TX, PIN_LORA_UART_RX);

  // 5. Initialize Mini SSD1306 OLED on Hardware I2C (SDA=GPIO 19, SCL=GPIO 20)
  pinMode(PIN_OLED_SDA, INPUT_PULLUP);
  pinMode(PIN_OLED_SCL, INPUT_PULLUP);
  Wire.begin(PIN_OLED_SDA, PIN_OLED_SCL);
  Wire.setClock(100000);
  Wire.setTimeOut(25);

  Wire.beginTransmission(0x3C);
  if (Wire.endTransmission() == 0) {
    hasOled = display.begin(SSD1306_SWITCHCAPVCC, 0x3C);
    if (hasOled) {
      display.clearDisplay();
      display.display();
      showSplashScreen();
    }
  } else {
    hasOled = false;
    Serial.println(F("[OLED] SSD1306 not detected on GPIO 19/20 (running headless)."));
  }

  deviceMac = getDeviceMacAddress();
}

// ------------------------------------------------------------------------------
// Cloud Ingestion Sync (Active Only When Online Mode 1 Wi-Fi is Enabled)
// ------------------------------------------------------------------------------
#define SH_HANDLE_CAPTURE_COMMAND(command) handleReceiveActuatorState(command)
#define SH_SEND_STORED_LORA(wire) sendLoRaBridgePacket(wire)
#include "TelemetryTransport.h"

// ------------------------------------------------------------------------------
// Setup & Main Loop
// ------------------------------------------------------------------------------
#include "ConnectivityManager.h"

void setup() {
    bleCommandQueue = xQueueCreate(4, sizeof(SHQueuedCommand));
#if defined(CONFIG_IDF_TARGET_ESP32C6) || defined(ARDUINO_DFROBOT_FIREBEETLE_2_ESP32C6) || defined(ESP32C6)
  disableTowerBrownoutAndLatchRelays();
#endif
  setCpuFrequencyMhz(80);
  Serial.begin(115200);
  delay(400);
  Serial.println(F("\n=================================================================="));
  Serial.println(F("  SmartHydro Vertical Tower - FireBeetle 2 ESP32-C6 + XIAO LoRa   "));
  Serial.println(F("=================================================================="));

  initHardwareWatchdog(30);
  initHardware();
  beginTelemetryStorage();
  doseSafety.begin();
  beginActivityStorage();
  beginTowerReceiptStorage();

  // Load saved Operating Mode & Dynamic Recipe Thresholds from NVS Flash
  // Note: dose_cnt is intentionally NOT persisted in NVS so reboots never lock pumps!
  prefs.begin("smarthydro", false);
  g_operatingMode = prefs.getInt("op_mode", MODE_LORA_BLE); // Default: Primary LoRa + BLE Mode (0)
  targetMinPh = prefs.getFloat("min_ph", 5.5f);
  targetMaxPh = prefs.getFloat("max_ph", 6.5f);
  targetMinEc = prefs.getFloat("min_ec", 1.0f);
  targetMaxEc = prefs.getFloat("max_ec", 2.5f);
  targetMaxTemp = prefs.getFloat("max_temp", 25.0f);
  targetMaxHum = prefs.getFloat("max_hum", 75.0f);
  prefs.putUChar("dose_cnt", 0);
  prefs.end();

  g_dailyDoseCount = 0;
  g_phRunawayAbort = false;

  pinMode(9, INPUT_PULLUP); // ESP32-C6 BOOT Button (GPIO 9)
  showModeBootBanner();

  // Initialize BLE GATT Server after RF/Wi-Fi mode is configured
  initTowerBLE(deviceMac);

  pumpCycleStartMs = millis();
  lightCycleStartMs = millis();
  g_dayWindowStartMs = millis();

  prefs.begin("smarthydro", true);
  bool pumpOn = prefs.getBool("pump_on", false);
  bool lightOn = prefs.getBool("light_on", false);
  bool fanOn = prefs.getBool("fan_on", false);
  bool extOn = prefs.getBool("ext_on", false);
  prefs.end();
  setRelay(PIN_RELAY_WATER_PUMP, pumpOn);
  setRelay(PIN_RELAY_LIGHT, lightOn);
  setRelay(PIN_RELAY_FAN_CIRC, fanOn);
  setRelay(PIN_RELAY_FAN_EXT, extOn);
  if (!pumpOn) pumpCycleStartMs -= PUMP_ON_DURATION_MS;
  if (!lightOn) lightCycleStartMs -= LIGHT_ON_DURATION_MS;
  readSensors();
  loadConnectivityState();

  updateOledDisplay();

  // Request initial status/RSSI from XIAO LoRa Bridge & transmit initial boot packet
  LoRaBridgeSerial.println("{\"ping\":1}");
  delay(100);
  broadcastCompactJson(true);
}

void loop() {
    static bool wasPairing = false;
    bool pairingNow = blePairingVisible();
    if (pairingNow != wasPairing) { wasPairing = pairingNow; updateOledDisplay(); }

    SHQueuedCommand queuedCommand;
    if (bleCommandQueue && xQueueReceive(bleCommandQueue, &queuedCommand, 0) == pdTRUE)
        handleQueuedCommandPayload(String(queuedCommand.payload), queuedCommand.source, queuedCommand.receivedAt);
  serviceRelayReports();
  serviceCommandCompletion();
    static String serialCommand;
    while (Serial.available()) {
        char ch = Serial.read();
        if (ch == '\n') {
            sensorCalibration.command(serialCommand, PIN_PH, PIN_EC, currentTelemetry.temp);
            serialCommand = "";
        } else if (ch != '\r') {
            if (serialCommand.length() < 64) serialCommand += ch;
            else serialCommand = "";
        }
    }

  feedWatchdog();
  unsigned long now = millis();

  // 1. 3-Second BOOT Button Hold (GPIO 9): Toggle between Primary LoRa+BLE Mode and Emergency SoftAP Mode
  static unsigned long btnPressStart = 0;
  static bool btnHandled = false;
  if (digitalRead(9) == LOW) {
    if (btnPressStart == 0) {
      btnPressStart = now;
    } else if (!btnHandled && now - btnPressStart >= 3000UL) {
      btnHandled = true;
      btnPressStart = 0;
      int nextMode = (g_operatingMode == MODE_EMERGENCY_SOFTAP) ? MODE_LORA_BLE : MODE_EMERGENCY_SOFTAP;
      Serial.printf("[BOOT Button] Held 3s -> Toggling Operating Mode to %s!\n",
                    nextMode == MODE_EMERGENCY_SOFTAP ? "EMERGENCY SOFTAP" : "PRIMARY LORA + BLE");
      switchOperatingMode(nextMode);
    }
  } else {
    btnHandled = false;
    btnPressStart = 0;
  }

  // 2. Non-blocking Dual-Tier Dosing Completion & Mixing Window Check
  checkDosingProgress();

  // 3. Process Incoming Downlink Frames over UART1 from XIAO ESP32S3 + Wio-SX1262 Bridge
  while (LoRaBridgeSerial.available()) {
    char c = (char)LoRaBridgeSerial.read();
    if (c == '\n') {
      String frame = g_loraUartRxBuf;
      g_loraUartRxBuf = "";
      frame.trim();
      if (frame.length() > 0) {
        handleLoRaBridgeDownlink(frame);
      }
    } else if (c != '\r') {
      if (g_loraUartRxBuf.length() < 380) {
        g_loraUartRxBuf += c;
      } else {
        g_loraUartRxBuf = "";
      }
    }
  }

  // 3b. Flush any deferred event-driven LoRa TX as soon as the 2.5s cooldown expires
  if (g_pendingImmediateLoraTx && (g_lastLoraTxMs == 0 || (now - g_lastLoraTxMs >= 2500UL))) {
    broadcastCompactJson(true);
  }

  // 4. Handle Emergency SoftAP HTTP Requests & Deferred Relay Switching
  if (g_operatingMode == MODE_EMERGENCY_SOFTAP || isOfflineMode || portalActive) {
    handleProvisioningLoop();
    runPendingLocalActions();
  }

  // 5. Handle Online Mode 1 SignalR WebSockets (if enabled)
  if (g_operatingMode == MODE_WIFI_CLOUD && WiFi.status() == WL_CONNECTED) {
    if (!wsInitialized && onlineTlsReady()) {
      initActuatorWebSocket();
      wsInitialized = true;
    }
    wsActuator.loop();
    static unsigned long lastWsPingMs = 0;
    if (isWsHandshakeDone && now - lastWsPingMs >= 15000UL) {
      lastWsPingMs = now;
      wsActuator.sendTXT("{\"type\":6}\x1e");
    }
  }

  // 6. Timed Water Pump Completion
  if (timedPumpEndMs > 0 && now >= timedPumpEndMs) {
    timedPumpEndMs = 0;
    g_manualWaterPumpOverride = false;
    pumpCycleStartMs = now - PUMP_ON_DURATION_MS; // Enters 45-minute resting phase
    setRelay(PIN_RELAY_WATER_PUMP, false);
    Serial.println(F("[Tower Pump] Timed irrigation run finished -> Pump OFF."));
    broadcastCompactJson(true);
    updateOledDisplay();
  }

  // 7. Sensor Acquisition, Hybrid ML + User Threshold Evaluation (Every 3 Seconds)
  if (now - lastSensorPollMs >= SENSOR_POLL_INTERVAL_MS) {
    lastSensorPollMs = now;
    readSensors();
    executeAutonomousML();
    notifyTowerBLE(buildCompactJson());
    ensureTowerBleAdvertising();
    updateOledDisplay();

    if (g_operatingMode == MODE_EMERGENCY_SOFTAP) {
      pushOfflineRecord(currentTelemetry.ph, currentTelemetry.ec,
                        currentTelemetry.temp, currentTelemetry.hum,
                        currentTelemetry.lux, currentTelemetry.waterRaw,
                        currentTelemetry.waterOk);
    }
  }

  // 8. Periodic 17.5-Second Desynchronized LoRa Telemetry Broadcast (+-2000ms random jitter)
  static unsigned long currentLoraIntervalMs = LORA_TELEMETRY_INTERVAL_MS;
  if ((g_operatingMode != MODE_WIFI_CLOUD || !directOnlineHealthy()) &&
      millis() - g_lastLoraTxMs >= currentLoraIntervalMs) {
    long jitter = g_operatingMode == MODE_LORA_BLE ? random(-2000, 2001) : 0;
    currentLoraIntervalMs = g_operatingMode == MODE_LORA_BLE ? (unsigned long)((long)LORA_TELEMETRY_INTERVAL_MS + jitter) : 15000UL;
    String loraJson = buildLoraCompactJson();
    sendLoRaBridgePacket(loraJson);
    updateOledDisplay();
  }

  // 9. Cloud Wi-Fi Sync (Only if Online Mode 1 is active)
  if (g_operatingMode == MODE_WIFI_CLOUD && WiFi.status() == WL_CONNECTED) {
    if (now - lastCloudSyncMs >= 15000UL) {
      lastCloudSyncMs = now;
      syncWithCloud();
    }
  }
    serviceStoredTelemetry();
    serviceRelayReports();
    serviceConnectivity();
}
