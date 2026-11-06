// ==================================================================================================
// SMART HYDROPONIC CONTROLLER - FODDER SPROUTING SYSTEM FIRMWARE
// ==================================================================================================
// Target MCU:  Heltec WiFi LoRa 32 V3 (ESP32-S3 + On-board Semtech SX1262 LoRa Radio)
// Deployment:  Barley/Seed Fodder Sprouting Trays (EMERISNMB)
// Features:
// - Tri-Mode Operation:
//     * MODE_LORA_BLE (0 - Default): Wi-Fi OFF, 868.1 MHz LoRa P2P (15s cadence + instant TX) + NimBLE 5.0
//     * MODE_EMERGENCY_SOFTAP (2): Glitch-free Local Hotspot (Hold PRG/BOOT GPIO 0 for 3s or via MVC)
//     * MODE_WIFI_CLOUD (1): Standby WPA2-Enterprise / Personal Wi-Fi + SignalR + LoRa Redundancy
// - Non-blocking 10-Second Spray ON / 3 Hours OFF Misting Cycle (with real-time LoRa/BLE/MVC sync)
// - Strict 16 Hours ON / 8 Hours OFF Non-Dynamic Photoperiod Schedule + Gravity Ambient Light Percentage
// - Timed dosing with shared fault checks, mixing lockout and a pH hourly cap.
// - Unconditional Pump Operation: Low water level is informational only and never aborts any pump.
// ==================================================================================================

#include <Arduino.h>
#include <WiFi.h>
#include <esp_mac.h>
#include <esp_system.h>
#include <HTTPClient.h>
#include <WiFiClientSecure.h>
#include "CloudSecurity.h"
#include <WebSocketsClient_Generic.h>
#include <ArduinoJson.h>
#include <DHT.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <math.h>
#include "soc/soc.h"
#include "soc/rtc_cntl_reg.h"
#include "soc/gpio_reg.h"
#include "esp_rom_gpio.h"
#include "esp_err.h"
#include "esp_private/startup_internal.h"


// Keep relay outputs OFF during startup without disabling brownout protection.
static void IRAM_ATTR latchFodderRelaysOff() {
    // Pre-latch all possible Active-LOW relay GPIOs HIGH (OFF) immediately:
    // Bank 0: GPIO 6, 7, 26 | Bank 1: GPIO 38, 39, 40, 41, 42, 47, 48
    const uint32_t bank0Mask = (1UL << 6) | (1UL << 7) | (1UL << 26);
    const uint32_t bank1Mask = (1UL << (38 - 32)) | (1UL << (39 - 32)) |
                               (1UL << (40 - 32)) | (1UL << (41 - 32)) |
                               (1UL << (42 - 32)) | (1UL << (47 - 32)) |
                               (1UL << (48 - 32));
    REG_WRITE(GPIO_OUT_W1TS_REG, bank0Mask);
    REG_WRITE(GPIO_OUT1_W1TS_REG, bank1Mask);

    const uint8_t relayPins[] = {38, 39, 40, 41, 42, 6, 7, 47, 48, 26};
    for (size_t i = 0; i < sizeof(relayPins); i++) {
        uint8_t p = relayPins[i];
        esp_rom_gpio_pad_select_gpio(p);
        esp_rom_gpio_pad_pullup_only(p);
    }

    REG_WRITE(GPIO_OUT_W1TS_REG, bank0Mask);
    REG_WRITE(GPIO_OUT1_W1TS_REG, bank1Mask);
    REG_WRITE(GPIO_ENABLE_W1TS_REG, bank0Mask);
    REG_WRITE(GPIO_ENABLE1_W1TS_REG, bank1Mask);
}

ESP_SYSTEM_INIT_FN(early_fodder_boot_stabilize, CORE, BIT(0), 1) {
    latchFodderRelaysOff();
    return ESP_OK;
}

__attribute__((constructor(101))) static void earlyLatchFodderRelays() {
    latchFodderRelaysOff();
}

#include "Config.h"
#include "CommandProtocol.h"
static QueueHandle_t bleCommandQueue = nullptr;
bool queueBleCommandPayload(const String& payload) {
    if (!bleCommandQueue || payload.length() >= 512) return false;
    char message[512] = {};
    payload.toCharArray(message, sizeof(message));
    return xQueueSend(bleCommandQueue, message, 0) == pdTRUE;
}
static SHCommandHistory commandHistory;
static SHDoseSafety doseSafety;
static SHManualOverrides manualOverrides;
static bool lastDoseAccepted = false;
static uint16_t activeDoseCid = 0;
static int commandDoseResult = 0;
static bool lastCommandAccepted = false;
static void sendCommandAck(SHCommandResult result);
static void serviceCommandCompletion();
static void serviceConnectivity();
static void cancelActiveCommand();
#include "SensorCalibration.h"
static SensorCalibration sensorCalibration;
#include "EC.h"
#include "pH.h"
#include "Temperature.h"
#include "Humidity.h"
#include "Provisioning.h"
#include "ConnectivityPolicy.h"
static SHConnectivityPolicy g_connectivity;
#define SH_CAPTURE_SYSTEM "fodder"
#define SH_HOTSPOT_PREFIX "SmartHydro-Fodder-"
#include "LoRa_SX1262.h"
#include "FodderBLE.h"

// Hardware instances
static DHT dht(PIN_DHT, DHT22);
static Adafruit_SSD1306 display(128, 64, &Wire, -1);
static bool hasOled = false;

static void readSensors();
static void updateOledDisplay();
void broadcastCompactJson(bool forceImmediateLoRa = false);

static bool g_lastTxAcked = false;
static int g_gatewayRssi = 0;
static unsigned long g_lastLoraTxMs = 0;
static unsigned long g_lastGatewayCloudUpMs = 0;
static uint8_t g_consecutiveGoodAcks = 0;
static bool g_pendingImmediateLoraTx = false;

// ML model instances
static Eloquent::ML::Port::RandomForestEC modelEC;
static Eloquent::ML::Port::RandomForestpH modelPH;
static Eloquent::ML::Port::RandomForestTemperature modelTemp;
static Eloquent::ML::Port::RandomForestHumidity modelHum;

// Relay states cache (All 8 channels: 4 AC / Mist relays + 4 Peristaltic Dosing relays)
struct RelayStates {
    bool growLight = false;
    bool circulationFan = false;
    bool spraySystem = false;
    bool extractorFan = false;
    bool phUp = false;
    bool phDown = false;
    bool ecUp = false;
    bool ecDown = false;
};
static RelayStates relays;

// Dynamic User Recipe Thresholds (Synced with Cloud/LoRa, Persisted in NVS Flash)
static float targetMinPh   = 5.8f;
static float targetMaxPh   = 6.4f;
static float targetMinEc   = 0.8f;
static float targetMaxEc   = 1.6f;
static float targetMaxTemp = 24.0f;
static float targetMaxHum  = 75.0f;

static String deviceMac = "";
static unsigned long g_lightScheduleStartMs = 0;
static int g_operatingMode = MODE_LORA_BLE;
bool isOfflineMode = false;
bool portalActive = false;

// Non-blocking timers
static unsigned long lastSensorPollMs = 0;
static unsigned long lastCloudSyncMs = 0;

// Non-blocking Fodder Misting State Machine (10s ON / 3h OFF)
static unsigned long g_lastSprayStartMs = 0;
static unsigned long g_sprayAutoOffAtMs = 0;
static bool g_autoSprayActive = false;

// Dual-Tier Anti-pH Shock & Peristaltic Dosing State
static unsigned long g_activeDoseEndMs = 0;
static uint8_t g_activeDosePin = 0;
static bool g_activeDoseIsManual = false;
static String g_activeDoseCmdId = "";
static unsigned long g_lastDoseCompletedMs = 0;
static unsigned long g_dailyWindowStartMs = 0;
static int g_dailyDoseCount = 0;
static bool g_phRunawayAbort = false;
static bool g_sensorFault = false;

// Manual override & timed spray hold
static unsigned long timedSprayEndMs = 0;
static unsigned long manualOverrideUntilMs = 0;

// SignalR WebSocket state (used only when MODE_WIFI_CLOUD is active)
static WebSocketsClient wsActuator;
static bool wsInitialized = false;
static bool isWsConnected = false;
static bool isWsHandshakeDone = false;
static bool inWsCallback = false;

static unsigned long g_lastRelaySwitchMs = 0;

static uint8_t computeRelayBitmask() {
    // Active-low GPIO read-back confirms output levels, not contact closure or flow.
    return (digitalRead(PIN_RELAY_SPRAY) == LOW ? 1 : 0) |
           (digitalRead(PIN_RELAY_LIGHT) == LOW ? 2 : 0) |
           (digitalRead(PIN_RELAY_FAN_CIRC) == LOW ? 4 : 0) |
           (digitalRead(PIN_RELAY_FAN_EXT) == LOW ? 8 : 0) |
           (digitalRead(PIN_DOSE_PH_UP) == LOW ? 16 : 0) |
           (digitalRead(PIN_DOSE_PH_DOWN) == LOW ? 32 : 0) |
           (digitalRead(PIN_DOSE_EC_UP) == LOW ? 64 : 0) |
           (digitalRead(PIN_DOSE_EC_DOWN) == LOW ? 128 : 0);
}

#include "TelemetryStorage.h"
#define SH_AUDIT_WATER_PIN PIN_RELAY_SPRAY
#include "ActivityStorage.h"

void setRelay(uint8_t pin, bool active) {
    int targetLevel = active ? LOW : HIGH;
    bool changed = digitalRead(pin) != targetLevel;
    // Stagger relay coil energization by 150ms to suppress back-EMF and 5V rail brownouts
    if (active && digitalRead(pin) != targetLevel) {
        unsigned long elapsed = millis() - g_lastRelaySwitchMs;
        if (elapsed < RELAY_STAGGER_MS) {
            delay(RELAY_STAGGER_MS - elapsed);
        }
    }
    digitalWrite(pin, targetLevel);
    bool chemical = pin == PIN_DOSE_PH_UP || pin == PIN_DOSE_PH_DOWN || pin == PIN_DOSE_EC_UP || pin == PIN_DOSE_EC_DOWN;
    if (active && chemical && !doseSafety.outputStarted(pin)) { digitalWrite(pin, HIGH); active = false; }
    g_lastRelaySwitchMs = millis();
    if (active) {
        delay(30); // Allow rail decoupling capacitor to settle before any RF TX
    }

    if (pin == PIN_DOSE_PH_UP) relays.phUp = active;
    else if (pin == PIN_DOSE_PH_DOWN) relays.phDown = active;
    else if (pin == PIN_DOSE_EC_UP) relays.ecUp = active;
    else if (pin == PIN_DOSE_EC_DOWN) relays.ecDown = active;
    else if (pin == PIN_RELAY_SPRAY) relays.spraySystem = active;
    else if (pin == PIN_RELAY_FAN_CIRC) relays.circulationFan = active;
    else if (pin == PIN_RELAY_FAN_EXT) relays.extractorFan = active;
    else if (pin == PIN_RELAY_LIGHT) relays.growLight = active;
    const char* key = pin == PIN_RELAY_SPRAY ? "pump_on" : pin == PIN_RELAY_LIGHT ? "light_on" :
                      pin == PIN_RELAY_FAN_CIRC ? "fan_on" : pin == PIN_RELAY_FAN_EXT ? "ext_on" : nullptr;
    if (changed && key) {
        prefs.begin("smarthydro", false);
        prefs.putBool(key, active);
        prefs.end();
    }

    if (changed && auditAction(pin) < 4) recordActivity(pin, active ? 0 : 1, auditSource, 0, auditCid);
}

static bool isDiffusionLockoutActive() {
    return doseSafety.remaining() > 0;
}

static unsigned long getDiffusionLockoutRemainingSec() {
    if (!isDiffusionLockoutActive()) return 0;
    return doseSafety.remaining() / 1000UL;
}

static void resetDailyDoseWindowIfExpired() {
    unsigned long now = millis();
    if (g_dailyWindowStartMs == 0 || (now - g_dailyWindowStartMs >= DAILY_WINDOW_MS)) {
        g_dailyWindowStartMs = now;
        g_dailyDoseCount = 0;
        g_phRunawayAbort = false;
    }
}

static void sendDosingCallback(const char* cmdId, const char* status) {
    if (!cmdId || strlen(cmdId) == 0) return;
    if (g_operatingMode != MODE_WIFI_CLOUD || WiFi.status() != WL_CONNECTED) return;

    HTTPClient http;
    String apiBase = getApiBaseUrl();
    bool isHttps = apiBase.startsWith("https://");
    WiFiClientSecure secureClient;
    WiFiClient plainClient;
    String url = apiBase + "/api/Dosing/callback";
    bool beginOk = false;
    if (isHttps) {
        secureClient.setCACert(GATEWAY_ROOT_CA);
        beginOk = http.begin(secureClient, url);
    } else {
        beginOk = http.begin(plainClient, url);
    }
    if (beginOk) {
        http.addHeader("Content-Type", "application/json"); http.addHeader("X-SmartHydro-Device-Key", shDeviceKey());
        StaticJsonDocument<256> doc;
        doc["commandId"] = cmdId;
        doc["status"] = status;
        String payload;
        serializeJson(doc, payload);
        int code = http.POST(payload);
        Serial.printf("[Dosing Callback] Reported command %s as %s (HTTP %d)\n", cmdId, status, code);
        http.end();
    }
}

static uint8_t resolveDosePin(const char* actName) {
    if (!actName) return 0;
    if (strcasecmp(actName, "PhUp") == 0 || strcasecmp(actName, "PhUpPump") == 0 ||
        strcasecmp(actName, "PH_UP") == 0 || strcasecmp(actName, "pu") == 0 ||
        strcasecmp(actName, "phUp") == 0) {
        return PIN_DOSE_PH_UP;
    }
    if (strcasecmp(actName, "PhDown") == 0 || strcasecmp(actName, "PhDownPump") == 0 ||
        strcasecmp(actName, "PH_DOWN") == 0 || strcasecmp(actName, "pd") == 0 ||
        strcasecmp(actName, "phDown") == 0) {
        return PIN_DOSE_PH_DOWN;
    }
    if (strcasecmp(actName, "EcUp") == 0 || strcasecmp(actName, "NutrientUpPump") == 0 ||
        strcasecmp(actName, "NUTRIENT_UP") == 0 || strcasecmp(actName, "EC_UP") == 0 ||
        strcasecmp(actName, "NUTRIENT_A") == 0 || strcasecmp(actName, "nutrientA") == 0 ||
        strcasecmp(actName, "nu") == 0 || strcasecmp(actName, "ecUp") == 0) {
        return PIN_DOSE_EC_UP;
    }
    if (strcasecmp(actName, "EcDown") == 0 || strcasecmp(actName, "DilutionDownPump") == 0 ||
        strcasecmp(actName, "DILUTION_DOWN") == 0 || strcasecmp(actName, "EC_DOWN") == 0 ||
        strcasecmp(actName, "NUTRIENT_B") == 0 || strcasecmp(actName, "nutrientB") == 0 ||
        strcasecmp(actName, "nd") == 0 || strcasecmp(actName, "ecDown") == 0) {
        return PIN_DOSE_EC_DOWN;
    }
    return 0;
}

static void stopDosingPump(uint8_t pin) {
    if (pin == 0) return;
    if (g_activeDosePin == pin) recordActivity(pin, 5, auditSource, activeAuditDuration, activeAuditCid);
    setRelay(pin, false);
    if (g_activeDosePin == pin) {
        cancelActiveCommand();
        g_activeDosePin = 0;
        g_activeDoseEndMs = 0;
        g_activeDoseIsManual = false;
        if (g_activeDoseCmdId.length() > 0) {
            sendDosingCallback(g_activeDoseCmdId.c_str(), "FAILED");
            g_activeDoseCmdId = "";
        }
    }
}

void startAsyncDoseMs(const char* actName, unsigned long durationMs, const char* cmdId = "", bool isManual = false);

// Manual and automatic doses share the same checks.
void startAsyncDoseMs(const char* actName, unsigned long durationMs, const char* cmdId, bool isManual) {
    lastDoseAccepted = false;
    resetDailyDoseWindowIfExpired();
    uint8_t pin = resolveDosePin(actName);
    bool outerSafe = pin && g_dailyDoseCount < MAX_DAILY_DOSES;
    if (!outerSafe ||
        !doseSafety.arm(pin, durationMs, sensorCalibration.faultPH || sensorCalibration.faultEC ||
            !isfinite(currentTelemetry.ph) || currentTelemetry.ph < 0 || currentTelemetry.ph > 14 ||
            !isfinite(currentTelemetry.ec) || currentTelemetry.ec < 0, g_activeDosePin != 0)) {
        reportDoseRefusal(pin, actName, outerSafe ? doseSafety.reason() : "dose_count_limit", durationMs);
        return;
    }
    g_activeDosePin = pin;
    g_activeDoseEndMs = millis() + durationMs;
    g_activeDoseIsManual = isManual;
    g_activeDoseCmdId = cmdId ? String(cmdId) : "";
    g_dailyDoseCount++;
    setRelay(pin, true);
    if (digitalRead(pin) != LOW) { g_activeDosePin = 0; return; }
    g_activeDoseEndMs = doseSafety.endsAt();
    lastDoseAccepted = true;
    activeAuditSource = auditSource; activeAuditCid = auditCid; activeAuditDuration = durationMs;
    recordActivity(pin, 2, auditSource, durationMs, auditCid);
    Serial.printf("[Dosing] %s: estimated %.2f mL, %lu ms at 800 ms/mL\n", actName, durationMs / 800.0f, durationMs);
}
void startAsyncDose(const char* actName, int durationSecOrVol, const char* cmdId, bool isManual) {
    unsigned long pulseMs = MICRO_DOSE_PULSE_MS;
    if (isManual) {
        if (durationSecOrVol <= 1) {
            pulseMs = MS_PER_ML;
        } else {
            pulseMs = constrain((unsigned long)durationSecOrVol * MS_PER_ML, MS_PER_ML, MANUAL_MAX_DOSE_MS);
        }
    } else {
        int vol = durationSecOrVol > 0 ? durationSecOrVol : 1;
        pulseMs = constrain((unsigned long)vol * MS_PER_ML, MS_PER_ML, 4000UL);
    }
    startAsyncDoseMs(actName, pulseMs, cmdId, isManual);
}

static void serviceActiveDose() {
    if (g_activeDosePin && (sensorCalibration.faultPH || sensorCalibration.faultEC)) {
        recordActivity(g_activeDosePin, 6, 6, activeAuditDuration, activeAuditCid, 1);
        uint8_t previousSource = auditSource; auditSource = 6;
        stopDosingPump(g_activeDosePin); auditSource = previousSource;
    }
    if (g_activeDoseEndMs > 0 && (int32_t)(millis() - g_activeDoseEndMs) >= 0) {
        bool wasManual = g_activeDoseIsManual;
        recordActivity(g_activeDosePin, 3, activeAuditSource, activeAuditDuration, activeAuditCid);
    setRelay(g_activeDosePin, false);
        g_activeDosePin = 0;
        g_activeDoseEndMs = 0;
        g_activeDoseIsManual = false;
        if (!wasManual) {
            g_lastDoseCompletedMs = millis();
            Serial.println(F("[Dosing Auto] Micro-dose complete. Entering 15-minute pH / 10-minute EC mixing window."));
        } else {
            Serial.println(F("[Dosing Manual] Farmer dosing pulse complete -> Pump OFF."));
        }
        if (g_activeDoseCmdId.length() > 0) {
            sendDosingCallback(g_activeDoseCmdId.c_str(), "COMPLETED");
            g_activeDoseCmdId = "";
        }
        // Broadcast completed OFF state immediately over BLE and LoRa
        broadcastCompactJson(true);
        updateOledDisplay();
    }
}

// -------------------------------------------------------------
// LOCAL SOFTAP RELAY ROUTES (GLITCH-FREE HTTP 200 BEFORE ACTUATION)
// -------------------------------------------------------------
String localRelaysJson() {
    String json = "{";
    json += "\"waterPump\":" + String(relays.spraySystem ? "true" : "false") + ",";
    json += "\"spraySystem\":" + String(relays.spraySystem ? "true" : "false") + ",";
    json += "\"growLight\":" + String(relays.growLight ? "true" : "false") + ",";
    json += "\"circulationFan\":" + String(relays.circulationFan ? "true" : "false") + ",";
    json += "\"extractorFan\":" + String(relays.extractorFan ? "true" : "false") + ",";
    json += "\"phUp\":" + String(relays.phUp ? "true" : "false") + ",";
    json += "\"phDown\":" + String(relays.phDown ? "true" : "false") + ",";
    json += "\"ecUp\":" + String(relays.ecUp ? "true" : "false") + ",";
    json += "\"ecDown\":" + String(relays.ecDown ? "true" : "false") + ",";
    json += "\"mode\":" + String(g_operatingMode);
    json += "}";
    return json;
}

void registerLocalControlRoutes() {
    server.on("/components", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        String msg = "{\n  \"PHPump\": \"" + String(relays.phUp ? "1" : "0") +
                     "\",\n  \"PHDownPump\": \"" + String(relays.phDown ? "1" : "0") +
                     "\",\n  \"Light\": \"" + String(relays.growLight ? "1" : "0") +
                     "\",\n  \"ECPump\": \"" + String(relays.ecUp ? "1" : "0") +
                     "\",\n  \"ECDownPump\": \"" + String(relays.ecDown ? "1" : "0") +
                     "\",\n  \"WaterPump\": \"" + String(relays.spraySystem ? "1" : "0") +
                     "\",\n  \"SpraySystem\": \"" + String(relays.spraySystem ? "1" : "0") +
                     "\",\n  \"Extractor\": \"" + String(relays.extractorFan ? "1" : "0") +
                     "\",\n  \"Fan\": \"" + String(relays.circulationFan ? "1" : "0") +
                     "\",\n  \"Water\": \"" + String(currentTelemetry.waterOk ? "High" : "Low") + "\"\n}";
        server.send(200, "application/json", msg);
    });

    server.on("/light", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        manualOverrides.hold(1);
        bool next = !relays.growLight;
        server.send(200, "text/plain", next ? "ON" : "OFF");
        delay(10);
        manualOverrideUntilMs = millis() + MANUAL_OVERRIDE_HOLD_MS;
        setRelay(PIN_RELAY_LIGHT, next);
        broadcastCompactJson(true);
    });

    server.on("/fan", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        manualOverrides.hold(2);
        bool next = !relays.circulationFan;
        server.send(200, "text/plain", next ? "ON" : "OFF");
        delay(10);
        manualOverrideUntilMs = millis() + MANUAL_OVERRIDE_HOLD_MS;
        setRelay(PIN_RELAY_FAN_CIRC, next);
        broadcastCompactJson(true);
    });

    server.on("/extract", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        manualOverrides.hold(3);
        bool next = !relays.extractorFan;
        server.send(200, "text/plain", next ? "ON" : "OFF");
        delay(10);
        manualOverrideUntilMs = millis() + MANUAL_OVERRIDE_HOLD_MS;
        setRelay(PIN_RELAY_FAN_EXT, next);
        broadcastCompactJson(true);
    });

    server.on("/pump", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        manualOverrides.hold(0);
        bool next = !relays.spraySystem;
        server.send(200, "text/plain", next ? "ON" : "OFF");
        delay(10);
        manualOverrideUntilMs = millis() + MANUAL_OVERRIDE_HOLD_MS;
        setRelay(PIN_RELAY_SPRAY, next);
        broadcastCompactJson(true);
    });

    server.on("/spray", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        manualOverrides.hold(0);
        bool next = !relays.spraySystem;
        server.send(200, "text/plain", next ? "ON" : "OFF");
        delay(10);
        manualOverrideUntilMs = millis() + MANUAL_OVERRIDE_HOLD_MS;
        setRelay(PIN_RELAY_SPRAY, next);
        broadcastCompactJson(true);
    });

    server.on("/phUp", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
    startAsyncDoseMs("PH_UP", MS_PER_ML, "", true);
    server.send(lastDoseAccepted ? 200 : 409, "application/json",
        lastDoseAccepted ? "{\"ok\":true}" : "{\"ok\":false,\"reason\":\"safety_blocked\"}");
  });

    server.on("/phDown", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
    startAsyncDoseMs("PH_DOWN", MS_PER_ML, "", true);
    server.send(lastDoseAccepted ? 200 : 409, "application/json",
        lastDoseAccepted ? "{\"ok\":true}" : "{\"ok\":false,\"reason\":\"safety_blocked\"}");
  });

    server.on("/phOff", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        server.send(200, "text/plain", "pH Pumps OFF");
        delay(10);
        stopDosingPump(PIN_DOSE_PH_UP);
        stopDosingPump(PIN_DOSE_PH_DOWN);
        broadcastCompactJson(true);
    });

    server.on("/ecUp", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
    startAsyncDoseMs("NUTRIENT_UP", MS_PER_ML, "", true);
    server.send(lastDoseAccepted ? 200 : 409, "application/json",
        lastDoseAccepted ? "{\"ok\":true}" : "{\"ok\":false,\"reason\":\"safety_blocked\"}");
  });

    server.on("/ecDown", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
    startAsyncDoseMs("DILUTION_DOWN", MS_PER_ML, "", true);
    server.send(lastDoseAccepted ? 200 : 409, "application/json",
        lastDoseAccepted ? "{\"ok\":true}" : "{\"ok\":false,\"reason\":\"safety_blocked\"}");
  });

    server.on("/ecOff", HTTP_GET, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        server.send(200, "text/plain", "Nutrient Pumps OFF");
        delay(10);
        stopDosingPump(PIN_DOSE_EC_UP);
        stopDosingPump(PIN_DOSE_EC_DOWN);
        broadcastCompactJson(true);
    });

    server.on("/api/control", HTTP_POST, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
    StaticJsonDocument<512> doc;
    if (deserializeJson(doc, server.arg("plain")) != DeserializationError::Ok) {
        server.send(400, "application/json", "{\"ok\":false,\"reason\":\"invalid_json\"}"); return;
    }
    JsonObject command = doc.as<JsonObject>();
    applyIncomingCommandJson(command, "SoftAP", false);
    server.send(lastCommandAccepted ? 200 : 409, "application/json",
        String("{\"ok\":") + (lastCommandAccepted ? "true" : "false") +
        ",\"r\":" + String(computeRelayBitmask()) + "}");
  });

    server.on("/api/dose", HTTP_POST, []() {
        struct SourceScope { uint8_t previous = auditSource; SourceScope() { auditSource = 2; } ~SourceScope() { auditSource = previous; } } sourceScope;
        String body = server.arg("plain");
        StaticJsonDocument<256> doc;
        if (deserializeJson(doc, body) != DeserializationError::Ok) {
            server.send(400, "application/json", "{\"ok\":false}");
            return;
        }
        const char* type = doc["type"] | (doc["actuatorName"] | (doc["dt"] | ""));
        uint32_t durMs;
    if (!shRequestedDoseMs(doc.as<JsonObject>(), durMs)) {
        server.send(400, "application/json", "{\"ok\":false,\"reason\":\"invalid_dose\"}"); return;
    }
        startAsyncDoseMs(type, durMs, "", true);
        server.send(lastDoseAccepted ? 200 : 409, "application/json", lastDoseAccepted ? "{\"ok\":true}" : "{\"ok\":false,\"reason\":\"safety_blocked\"}");
    });
}

// -------------------------------------------------------------
// MODE TRANSITION ENGINE (TRI-MODE: LORA+BLE, EMERGENCY SOFTAP, WIFI CLOUD)
// -------------------------------------------------------------
static void applyOperatingMode(int newMode, bool persistToFlash = true) {
    requestConnectivityMode(newMode);
}

// Check PRG/BOOT button (GPIO 0) for 3-second hold to toggle Emergency SoftAP
static unsigned long g_bootPressStartMs = 0;
static bool g_bootHandled = false;
static void checkBootButtonModeToggle() {
    if (digitalRead(MANUAL_TRIGGER_PIN) == LOW) {
        if (g_bootPressStartMs == 0) {
            g_bootPressStartMs = millis();
            g_bootHandled = false;
        } else if (!g_bootHandled && (millis() - g_bootPressStartMs >= BOOT_HOLD_TRIGGER_MS)) {
            g_bootHandled = true;
            if (g_operatingMode == MODE_EMERGENCY_SOFTAP) {
                Serial.println(F("[BOOT Trigger] 3s Hold detected -> Returning to Primary MODE_LORA_BLE!"));
                applyOperatingMode(MODE_LORA_BLE, true);
            } else {
                Serial.println(F("[BOOT Trigger] 3s Hold detected -> Activating MODE_EMERGENCY_SOFTAP!"));
                applyOperatingMode(MODE_EMERGENCY_SOFTAP, true);
            }
        }
    } else {
        g_bootPressStartMs = 0;
        g_bootHandled = false;
    }
}

// -------------------------------------------------------------
// HYBRID COMPACT MINIFIED JSON BUILDERS & LORA / BLE BROADCASTER
// -------------------------------------------------------------
// Canonical SmartHydro API 8-Bit Relay Bitmask ("r"):
// Bit 0 (1):   SpraySystem / MainWaterPump
// Bit 1 (2):   GrowLight
// Bit 2 (4):   CirculationFan
// Bit 3 (8):   ExtractorFan
// Bit 4 (16):  PhUpPump
// Bit 5 (32):  PhDownPump
// Bit 6 (64):  NutrientUpPump
// Bit 7 (128): DilutionDownPump
static uint8_t g_lastBroadcastBitmask = 0xFF;




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

String buildLoraCompactJson() {
    return telemetryLoraJson(captureTelemetrySnapshot());
}

String buildCompactJson() {
    StaticJsonDocument<320> doc;
    doc["m"] = deviceMac;
    doc["p"] = serialized(String(currentTelemetry.ph, 2));
    doc["e"] = serialized(String(currentTelemetry.ec, 2));
    doc["t"] = serialized(String(currentTelemetry.temp, 1));
    doc["h"] = serialized(String(currentTelemetry.hum, 1));
    doc["l"] = (int)currentTelemetry.lux;
    doc["w"] = currentTelemetry.waterOk ? 1 : 0;
    bool puActive = (digitalRead(PIN_DOSE_PH_UP) == LOW);
    bool pdActive = (digitalRead(PIN_DOSE_PH_DOWN) == LOW);
    bool nuActive = (digitalRead(PIN_DOSE_EC_UP) == LOW);
    bool ndActive = (digitalRead(PIN_DOSE_EC_DOWN) == LOW);

    doc["s"] = relays.spraySystem ? 1 : 0;
    doc["fc"] = relays.circulationFan ? 1 : 0;
    doc["fe"] = relays.extractorFan ? 1 : 0;
    doc["gl"] = relays.growLight ? 1 : 0;
    doc["lux"] = (int)currentTelemetry.lux;
    doc["pu"] = puActive ? 1 : 0;
    doc["pd"] = pdActive ? 1 : 0;
    doc["nu"] = nuActive ? 1 : 0;
    doc["nd"] = ndActive ? 1 : 0;
    doc["md"] = g_operatingMode;
    doc["pm"] = g_connectivity.preferred; doc["mr"] = g_connectivity.reason;
    doc["sf"] = (currentTelemetry.ph < 0 ? 1 : 0) | (currentTelemetry.ec < 0 ? 2 : 0)
        | (currentTelemetry.temp == -99 ? 4 : 0) | (currentTelemetry.hum < 0 ? 8 : 0)
        | (currentTelemetry.lux < 0 ? 16 : 0);
    doc["r"] = computeRelayBitmask();

    if (g_phRunawayAbort) {
        doc["al"] = "PH_RUNAWAY_ABORT";
    } else if (g_sensorFault) {
        doc["al"] = "SENSOR_FAULT";
    }

    String out;
    serializeJson(doc, out);
    return out;
}

void broadcastCompactJson(bool forceImmediateLoRa) {
    String bleJson = buildCompactJson();
    notifyFodderBLE(bleJson);
    uint8_t currentMask = computeRelayBitmask();
    unsigned long now = millis();
    bool wantsLoRaTx = forceImmediateLoRa || (currentMask != g_lastBroadcastBitmask);
    if (!wantsLoRaTx || !g_isLoRaActive) {
        return;
    }

    bool minCooldownElapsed = (g_lastLoraTxMs == 0 || (now - g_lastLoraTxMs >= 2500UL)) &&
                              (g_lastRelaySwitchMs == 0 || (now - g_lastRelaySwitchMs >= 150UL));
    if (minCooldownElapsed) {
        g_pendingImmediateLoraTx = false;
        g_lastLoraTxMs = now;
        g_lastTxAcked = false;
        g_lastBroadcastBitmask = currentMask;
        String loraJson = buildLoraCompactJson();
        broadcastLoRaCompactJson(loraJson);
        markTelemetrySent(loraJson);
        updateOledDisplay();
    } else {
        // Latch deferred TX so rapid pump/relay transitions are never silently dropped!
        g_pendingImmediateLoraTx = true;
    }
}

void sendActuatorStateReport(const char* actKey, bool state, const char* reason) {
    if (g_operatingMode == MODE_WIFI_CLOUD && isWsConnected && isWsHandshakeDone) {
        String payload = "{\"type\":1,\"target\":\"ReportActuatorState\",\"arguments\":[\"" + deviceMac + "\",{\"" + String(actKey) + "\":" + (state ? "true" : "false") + ",\"changedBy\":\"" + String(reason) + "\"}]}\x1e";
        wsActuator.sendTXT(payload);
    }
}

// Unified Downlink Command Processor (LoRa Gateway + Android BLE + Cloud)
static bool startCommandDose(const char* name, uint32_t duration, const char* doseId) {
    startAsyncDoseMs(name, duration, doseId, true);
    return lastDoseAccepted;
}
static void stopCommandDose(const char* name) { stopDosingPump(resolveDosePin(name)); }
static int requestedCommandMode(JsonObject cmd) {
    JsonVariant value = cmd.containsKey("operatingMode")
        ? cmd["operatingMode"].as<JsonVariant>() : cmd["mode"].as<JsonVariant>();
    if (value.is<int>()) return value.as<int>();
    if (value.is<const char*>()) return shParseModeVariant(value.as<String>());
    return -1;
}

static void applyCommandBody(JsonObject cmd, const char* sourceName, bool shouldBroadcastLoRa) {
    String calibrationCommand = cmd["cmd"] | (cmd["command"] | "");
    if (sensorCalibration.command(calibrationCommand, PIN_PH, PIN_EC, currentTelemetry.temp)) return;

    // 1. Check for operational mode switch request ("mode": 0=LoRa+BLE, 1=WiFi Cloud, 2=SoftAP, or string)
    if (cmd.containsKey("operatingMode") || cmd.containsKey("mode")) {
        int requestedMode = requestedCommandMode(cmd);
        if (requestedMode >= 0 && requestedMode <= 2) {
            applyOperatingMode(requestedMode, true);
            return;
        }
    }

    // 2. Check for dynamic user threshold updates (Supports p0/p1, e0/e1 or p2/p3, t1 or p5, h1 or p7)
    bool thresholdsUpdated = false;
    if (cmd.containsKey("p0") || cmd.containsKey("minPh")) {
        float v = cmd.containsKey("p0") ? cmd["p0"].as<float>() : cmd["minPh"].as<float>();
        if (v >= 0.0f && v <= 14.0f) { targetMinPh = v; thresholdsUpdated = true; }
    }
    if (cmd.containsKey("p1") || cmd.containsKey("maxPh")) {
        float v = cmd.containsKey("p1") ? cmd["p1"].as<float>() : cmd["maxPh"].as<float>();
        if (v >= 0.0f && v <= 14.0f) { targetMaxPh = v; thresholdsUpdated = true; }
    }
    if (cmd.containsKey("e0") || cmd.containsKey("p2") || cmd.containsKey("minEc")) {
        float v = cmd.containsKey("e0") ? cmd["e0"].as<float>()
                : (cmd.containsKey("p2") ? cmd["p2"].as<float>() : cmd["minEc"].as<float>());
        if (v >= 0.0f && v <= 99999.0f) { targetMinEc = v; thresholdsUpdated = true; }
    }
    if (cmd.containsKey("e1") || cmd.containsKey("p3") || cmd.containsKey("maxEc")) {
        float v = cmd.containsKey("e1") ? cmd["e1"].as<float>()
                : (cmd.containsKey("p3") ? cmd["p3"].as<float>() : cmd["maxEc"].as<float>());
        if (v >= 0.0f && v <= 99999.0f) { targetMaxEc = v; thresholdsUpdated = true; }
    }
    if (cmd.containsKey("t1") || cmd.containsKey("p5") || cmd.containsKey("maxTemp")) {
        float v = cmd.containsKey("t1") ? cmd["t1"].as<float>()
                : (cmd.containsKey("p5") ? cmd["p5"].as<float>() : cmd["maxTemp"].as<float>());
        if (v >= -40.0f && v <= 80.0f) { targetMaxTemp = v; thresholdsUpdated = true; }
    }
    if (cmd.containsKey("h1") || cmd.containsKey("p7") || cmd.containsKey("maxHum")) {
        float v = cmd.containsKey("h1") ? cmd["h1"].as<float>()
                : (cmd.containsKey("p7") ? cmd["p7"].as<float>() : cmd["maxHum"].as<float>());
        if (v >= 0.0f && v <= 100.0f) { targetMaxHum = v; thresholdsUpdated = true; }
    }
    if (thresholdsUpdated) {
        prefs.begin("smarthydro", false);
        prefs.putFloat("min_ph", targetMinPh);
        prefs.putFloat("max_ph", targetMaxPh);
        prefs.putFloat("min_ec", targetMinEc);
        prefs.putFloat("max_ec", targetMaxEc);
        prefs.putFloat("max_temp", targetMaxTemp);
        prefs.putFloat("max_hum", targetMaxHum);
        prefs.end();
        Serial.printf("[Recipe] Thresholds updated via %s: pH[%.1f-%.1f] EC[%.1f-%.1f] Temp<=%.1fC Hum<=%.0f%%\n",
                      sourceName, targetMinPh, targetMaxPh, targetMinEc, targetMaxEc, targetMaxTemp, targetMaxHum);
    }

    // 3. Check for relay state changes (supports bit-packed "r" or individual keys)
    bool relayChanged = false;
    if (cmd.containsKey("r") && !cmd.containsKey("ack")) {
        uint8_t rMask = (uint8_t)cmd["r"].as<int>();
        bool targetSpray = ((rMask & 1) != 0);
        bool targetLight = (rMask & 2) != 0;
        bool targetCirc  = (rMask & 4) != 0;
        bool targetExt   = (rMask & 8) != 0;

        if (relays.spraySystem != targetSpray || relays.growLight != targetLight ||
            relays.circulationFan != targetCirc || relays.extractorFan != targetExt) {
            relayChanged = true;
        }
        setRelay(PIN_RELAY_SPRAY, targetSpray);
        if (targetSpray) {
            unsigned long durSec = 10;
            if (cmd.containsKey("st")) durSec = cmd["st"].as<unsigned long>();
            else if (cmd.containsKey("d")) durSec = cmd["d"].as<unsigned long>();
            else if (cmd.containsKey("durationSeconds")) durSec = cmd["durationSeconds"].as<unsigned long>();
            else if (cmd.containsKey("duration")) durSec = cmd["duration"].as<unsigned long>();
            if (durSec == 0) durSec = 10;
            timedSprayEndMs = millis() + (durSec * 1000UL);
            g_lastSprayStartMs = millis();
            Serial.printf("[Fodder] Spray turned ON for %lu s timed burst.\n", durSec);
        } else {
            timedSprayEndMs = 0;
            Serial.println(F("[Fodder] Spray turned OFF."));
        }
        setRelay(PIN_RELAY_LIGHT, targetLight);
        setRelay(PIN_RELAY_FAN_CIRC, targetCirc);
        setRelay(PIN_RELAY_FAN_EXT, targetExt);
    }

    if (cmd.containsKey("s") || cmd.containsKey("w") || cmd.containsKey("spraySystem") || cmd.containsKey("waterPump") || cmd.containsKey("mainWaterPump")) {
        bool target = cmd.containsKey("s") ? (cmd["s"] == 1 || cmd["s"] == true) :
                      (cmd.containsKey("spraySystem") ? cmd["spraySystem"].as<bool>() :
                      (cmd.containsKey("w") ? (cmd["w"] == 1 || cmd["w"] == true) :
                      (cmd.containsKey("mainWaterPump") ? cmd["mainWaterPump"].as<bool>() : cmd["waterPump"].as<bool>())));
        if (relays.spraySystem != target) relayChanged = true;
        setRelay(PIN_RELAY_SPRAY, target);
        if (target) {
            unsigned long durSec = 10;
            if (cmd.containsKey("st")) durSec = cmd["st"].as<unsigned long>();
            else if (cmd.containsKey("wt")) durSec = cmd["wt"].as<unsigned long>();
            else if (cmd.containsKey("d")) durSec = cmd["d"].as<unsigned long>();
            else if (cmd.containsKey("durationSeconds")) durSec = cmd["durationSeconds"].as<unsigned long>();
            else if (cmd.containsKey("duration")) durSec = cmd["duration"].as<unsigned long>();
            if (durSec == 0) durSec = 10;
            timedSprayEndMs = millis() + (durSec * 1000UL);
            g_lastSprayStartMs = millis();
            Serial.printf("[Fodder] Spray turned ON for %lu s timed burst.\n", durSec);
        } else {
            timedSprayEndMs = 0;
            Serial.println(F("[Fodder] Spray manually turned OFF."));
        }
        sendActuatorStateReport("spraySystem", relays.spraySystem, sourceName);
    }
    if (cmd.containsKey("fc") || cmd.containsKey("circulationFan")) {
        bool target = cmd.containsKey("fc") ? (cmd["fc"] == 1 || cmd["fc"] == true) : cmd["circulationFan"].as<bool>();
        if (relays.circulationFan != target) relayChanged = true;
        setRelay(PIN_RELAY_FAN_CIRC, target);
        sendActuatorStateReport("circulationFan", relays.circulationFan, sourceName);
    }
    if (cmd.containsKey("fe") || cmd.containsKey("extractorFan")) {
        bool target = cmd.containsKey("fe") ? (cmd["fe"] == 1 || cmd["fe"] == true) : cmd["extractorFan"].as<bool>();
        if (relays.extractorFan != target) relayChanged = true;
        setRelay(PIN_RELAY_FAN_EXT, target);
        sendActuatorStateReport("extractorFan", relays.extractorFan, sourceName);
    }
    if (cmd.containsKey("l") || cmd.containsKey("gl") || cmd.containsKey("growLight")) {
        bool target = cmd.containsKey("gl") ? (cmd["gl"] == 1 || cmd["gl"] == true) :
                      (cmd.containsKey("l") ? (cmd["l"] == 1 || cmd["l"] == true) : cmd["growLight"].as<bool>());
        if (relays.growLight != target) relayChanged = true;
        setRelay(PIN_RELAY_LIGHT, target);
        sendActuatorStateReport("growLight", relays.growLight, sourceName);
    }

    commandDoseResult = shApplyDose(cmd, startCommandDose, stopCommandDose);
    bool hasDosingKey = commandDoseResult != 0;

    bool hasActuatorKey = (cmd.containsKey("r") && !cmd.containsKey("ack")) ||
                          cmd.containsKey("s") || cmd.containsKey("w") ||
                          cmd.containsKey("spraySystem") || cmd.containsKey("waterPump") || cmd.containsKey("mainWaterPump") ||
                          cmd.containsKey("fc") || cmd.containsKey("circulationFan") ||
                          cmd.containsKey("fe") || cmd.containsKey("extractorFan") ||
                          cmd.containsKey("l") || cmd.containsKey("gl") || cmd.containsKey("growLight") ||
                          hasDosingKey;

    if (relayChanged || hasActuatorKey) {
        manualOverrideUntilMs = millis() + MANUAL_OVERRIDE_HOLD_MS;
    }

    broadcastCompactJson(true);
    updateOledDisplay();
}

#include "CommandAckOutbox.h"
static SHCommandAckOutbox<String> commandAckOutbox;
static uint8_t commandPhaseCode(const char* phase) {
    return strcmp(phase, "completed") == 0 ? 1 : strcmp(phase, "cancelled") == 0 ? 2 : 0;
}

static void sendCommandAck(SHCommandResult result) {
    notifyBleCommandAck(result.cid, result.ok, result.reason, result.phase);
    if (!result.cid) return;
    commandHistory.save(result);
    StaticJsonDocument<256> ack;
    ack["m"] = deviceMac; ack["cack"] = 1; ack["cid"] = result.cid;
    ack["ar"] = 1; // Ask the gateway to confirm that Azure accepted this reply.
    ack["ok"] = result.ok ? 1 : 0; ack["r"] = computeRelayBitmask();
    ack["md"] = g_operatingMode; ack["pm"] = g_connectivity.preferred; ack["mr"] = g_connectivity.reason; ack["reason"] = result.reason; ack["phase"] = result.phase;
    String json; serializeJson(ack, json);
    if (!commandAckOutbox.put(result.cid, commandPhaseCode(result.phase), json, millis()))
        Serial.printf("[Command ACK] Queue full; CID %u will be recovered on command retry.\n", result.cid);
    Serial.printf("[Command ACK] CID %u %s: %s, relay mask %u.\n", result.cid, result.phase, result.reason, computeRelayBitmask());
    if (g_operatingMode == MODE_WIFI_CLOUD && isWsConnected && isWsHandshakeDone) {
        StaticJsonDocument<512> message;
        message["type"] = 1; message["target"] = "ReportCommandAck";
        message["invocationId"] = "ack-" + String(result.cid) + "-" + String(commandPhaseCode(result.phase));
        message.createNestedArray("arguments").add(ack.as<JsonObject>());
        String wire; serializeJson(message, wire); wire += '\x1e'; wsActuator.sendTXT(wire);
    }
}

static void cancelActiveCommand() {
    if (activeDoseCid) {
        sendCommandAck({activeDoseCid, true, "cancelled", "cancelled"});
        activeDoseCid = 0;
    }
}

static void serviceCommandCompletion() {
    if (activeDoseCid && (!g_activeDosePin || digitalRead(g_activeDosePin) == HIGH)) {
        sendCommandAck({activeDoseCid, true, "timer_finished", "completed"});
        activeDoseCid = 0;
    }
    auto* pending = commandAckOutbox.next(millis());
    if (!pending) return;
    bool sent = false;
    if (g_operatingMode == MODE_WIFI_CLOUD && WiFi.status() == WL_CONNECTED && !g_activeDosePin) {
        HTTPClient http; WiFiClientSecure secure; WiFiClient plain;
        String url = getApiBaseUrl() + "/api/telemetry/lora";
        secure.setCACert(GATEWAY_ROOT_CA);
        bool opened = url.startsWith("https://") ? http.begin(secure, url) : http.begin(plain, url);
        if (opened) {
            http.setTimeout(1500); http.addHeader("Content-Type", "application/json"); http.addHeader("X-SmartHydro-Device-Key", shDeviceKey());
            int status = http.POST(pending->payload);
            StaticJsonDocument<128> reply;
            sent = status == 200 && deserializeJson(reply, http.getString()) == DeserializationError::Ok && (reply["success"] | false);
            http.end();
        }
    } else {
        broadcastLoRaCompactJson(pending->payload, SH_PRIO_CRITICAL_ACK);
    }
    if (sent) commandAckOutbox.confirm(pending->cid, pending->phase);
    else commandAckOutbox.sent(*pending, millis());
}

static void applyIncomingCommandJson(JsonObject cmd, const char* sourceName, bool shouldBroadcastLoRa) {
    lastCommandAccepted = false;
    if (cmd["command"].is<JsonObject>()) {
        JsonObject nested = cmd["command"].as<JsonObject>();
        applyIncomingCommandJson(nested, sourceName, shouldBroadcastLoRa);
        return;
    }
    String calibration = cmd["cmd"] | (cmd["command"] | "");
    if (sensorCalibration.command(calibration, PIN_PH, PIN_EC, currentTelemetry.temp)) return;
    if (cmd.containsKey("m") && !isMatchingNodeMac(cmd["m"].as<String>())) return;
    if (cmd.containsKey("cid") && !cmd["cid"].is<unsigned int>()) {
        if (strstr(sourceName, "BLE")) notifyBleCommandAck(0, false, "invalid_command", "accepted");
        return;
    }
    uint32_t requestedCid = cmd["cid"] | 0UL;
    if (requestedCid > 65535) {
        if (strstr(sourceName, "BLE")) notifyBleCommandAck(0, false, "invalid_command", "accepted");
        return;
    }
    uint16_t cid = (uint16_t)requestedCid;
    if (auto* previous = commandHistory.find(cid)) { lastCommandAccepted = previous->ok; sendCommandAck(*previous); return; }
    if (!shNormaliseCommand(cmd, computeRelayBitmask()) || !shHasCommandAction(cmd)) {
        sendCommandAck({cid, false, "invalid_command", "accepted"}); return;
    }
    if (cmd.containsKey("operatingMode") || cmd.containsKey("mode")) {
        int mode = requestedCommandMode(cmd);
        if (mode < MODE_LORA_BLE || mode > MODE_EMERGENCY_SOFTAP) {
            sendCommandAck({cid, false, "invalid_command", "accepted"}); return;
        }
    }
    uint8_t previousSource = auditSource; uint16_t previousCid = auditCid;
    auditSource = strstr(sourceName, "BLE") ? 1 : strstr(sourceName, "SoftAP") ? 2 : 0; auditCid = cid;
    if (!shThresholdsValid(cmd, targetMinPh, targetMaxPh, targetMinEc, targetMaxEc)) {
        auditSource = previousSource; auditCid = previousCid;
        sendCommandAck({cid, false, "invalid_thresholds", "accepted"}); return;
    }
    manualOverrides.apply(cmd);
    commandDoseResult = 0;
    lastDoseRefusal = "invalid_dose";
    applyCommandBody(cmd, sourceName, shouldBroadcastLoRa);
    auditSource = previousSource; auditCid = previousCid;
    if (g_requestedConnectivityMode >= 0) serviceConnectivity();
    bool ok = commandDoseResult >= 0;
    lastCommandAccepted = ok;
    if (commandDoseResult > 0 && g_activeDosePin) activeDoseCid = cid;
    sendCommandAck({cid, ok, ok ? "executed" : lastDoseRefusal, "accepted"});
}
void handleLoraDownlinkCommand(const String& payload) {
    StaticJsonDocument<512> doc;
    DeserializationError err = deserializeJson(doc, payload);
    if (err != DeserializationError::Ok) {
        Serial.printf("[LoRa Downlink] Failed to parse JSON: %s\n", err.c_str());
        return;
    }

    // Filter by target MAC if present (colon-agnostic) so Vertical Tower commands are ignored by Fodder
    if (doc.containsKey("m")) {
        String targetMac = doc["m"].as<String>();
        if (targetMac.length() > 0 && !isMatchingNodeMac(targetMac)) {
            return;
        }
    }

    if ((doc["ack"] | 0) == 1) {
        if (doc["ac"].is<unsigned int>() && doc["ap"].is<unsigned int>()) {
            uint16_t cid = doc["ac"] | 0;
            commandAckOutbox.confirm(cid, doc["ap"] | 0);
            Serial.printf("[Command ACK] API storage confirmed for CID %u.\n", cid);
            return;
        }
        handleTelemetryReceipt(doc.as<JsonObject>());
    int upFlag = doc["up"] | 0;
        g_gatewayRssi = doc["r"] | (int)getLoRaLastRssi();
        if (upFlag == 1) {
            g_lastTxAcked = true;
            g_lastGatewayCloudUpMs = millis();
            g_connectivity.loraAck(true, millis());
            if (g_consecutiveGoodAcks < 250) g_consecutiveGoodAcks++;
            Serial.printf("[LoRa ACK] Gateway confirmed reception & cloud UP (up=1)! Gateway RSSI: %d dBm (Good ACKs: %u)\n",
                          g_gatewayRssi, (unsigned int)g_consecutiveGoodAcks);
        } else {
            g_lastTxAcked = false;
            g_connectivity.loraAck(false, millis());
            g_consecutiveGoodAcks = 0;
            Serial.printf("[LoRa ACK] Gateway RF link OK (RSSI: %d dBm), but 4G cloud uplink is DOWN (up=0).\n",
                          g_gatewayRssi);
        }
        // Check if Piggybacked sync object is included in ACK or command
        if (doc.containsKey("sync") && doc["sync"].is<JsonObject>()) {
            applyIncomingCommandJson(doc["sync"].as<JsonObject>(), "LoRa-Gateway", true);
        }
        updateOledDisplay();
        return;
    }

    applyIncomingCommandJson(doc.as<JsonObject>(), "LoRa-Gateway", true);
}

void handleBleCommandPayload(const String& payload) {
    if (sensorCalibration.command(payload, PIN_PH, PIN_EC, currentTelemetry.temp)) return;
    StaticJsonDocument<512> doc;
    DeserializationError err = deserializeJson(doc, payload);
    if (err != DeserializationError::Ok) {
        doc.clear(); doc["cmd"] = payload;
    }
    applyIncomingCommandJson(doc.as<JsonObject>(), "AndroidBLE", true);
}

// -------------------------------------------------------------
// SIGNALR WEBSOCKET CLIENT (STANDBY MODE_WIFI_CLOUD)
// -------------------------------------------------------------
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
                    isWsConnected = true;
                    isWsHandshakeDone = true;
                    String regPayload = "{\"type\":1,\"target\":\"RegisterDevice\",\"arguments\":[\"" + deviceMac + "\"]}\x1e";
                    wsActuator.sendTXT(regPayload);
                    continue;
                }

                StaticJsonDocument<1024> doc;
                if (deserializeJson(doc, record) == DeserializationError::Ok) {
                    int msgType = doc["type"] | 0;
                    if (msgType == 3 && doc["result"].is<bool>() && doc["result"].as<bool>()) {
                        String invocation = doc["invocationId"] | "";
                        int separator = invocation.lastIndexOf('-');
                        if (invocation.startsWith("ack-") && separator > 4) {
                            uint16_t cid = invocation.substring(4, separator).toInt();
                            commandAckOutbox.confirm(cid, invocation.substring(separator + 1).toInt());
                            Serial.printf("[Command ACK] API confirmed CID %u over Wi-Fi.\n", cid);
                        }
                    } else if (msgType == 6) {
                        wsActuator.sendTXT("{\"type\":6}\x1e");
                    } else if (msgType == 1) {
                        const char* target = doc["target"];
                        if (target && (strcmp(target, "ReceiveActuatorState") == 0 || strcmp(target, "ReceiveCommand") == 0)) {
                            JsonObject state = doc["arguments"][0];
                            if (!state.isNull()) applyIncomingCommandJson(state, "SignalR-Cloud", false);
                        } else if (target && (strcmp(target, "ReceiveOfflineTransition") == 0 || strcmp(target, "receiveOfflineTransition") == 0)) {
                            applyOperatingMode(MODE_EMERGENCY_SOFTAP, true);
                        } else if (target && (strcmp(target, "ReceiveOnlineTransition") == 0 || strcmp(target, "receiveOnlineTransition") == 0)) {
                            applyOperatingMode(MODE_WIFI_CLOUD, true);
                        } else if (target && (strcmp(target, "ReceiveLoraTransition") == 0 || strcmp(target, "receiveLoraTransition") == 0)) {
                            applyOperatingMode(MODE_LORA_BLE, true);
                        } else if (target && strcmp(target, "ReceiveTimedCommand") == 0) {
                            JsonObject cmd = doc["arguments"][0];
                            if (!cmd.isNull()) {
                                const char* actName = cmd["actuatorName"] | (cmd["ActuatorName"] | (cmd["dosingType"] | ""));
                                unsigned long durMs = cmd["durationMs"] | (cmd["DurationMs"] | 0UL);
                                int duration = cmd["durationSeconds"] | (cmd["DurationSeconds"] | 10);
                                const char* cmdId = cmd["commandId"] | (cmd["CommandId"] | "");
                                if (strcasecmp(actName, "SpraySystem") == 0 || strcasecmp(actName, "Spray") == 0 || strcasecmp(actName, "MainWaterPump") == 0) {
                                    setRelay(PIN_RELAY_SPRAY, true);
                                    timedSprayEndMs = millis() + ((unsigned long)duration * 1000UL);
                                    broadcastCompactJson(true);
                                } else {
                                    if (durMs == 0) durMs = (unsigned long)duration * 1000UL;
                                    startAsyncDoseMs(actName, durMs, cmdId, true);
                                }
                            }
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
    bool isHttps = getApiBaseUrl().startsWith("https://");
    if (isHttps) {
        wsActuator.beginSslWithCA(host.c_str(), port, "/hubs/actuators", GATEWAY_ROOT_CA);
    } else {
        wsActuator.begin(host.c_str(), port, "/hubs/actuators");
    }
    static String deviceHeaders = String("X-SmartHydro-Device-Key: ") + shDeviceKey();
    wsActuator.setExtraHeaders(deviceHeaders.c_str());
    wsActuator.onEvent(onActuatorWsEvent);
    wsActuator.setReconnectInterval(5000);
}

// -------------------------------------------------------------
// OLED DISPLAY & ADC HELPERS
// -------------------------------------------------------------
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

static int analogReadAvg(int pin, uint8_t samples = 10) {
    long sum = 0;
    for (uint8_t i = 0; i < samples; i++) {
        sum += analogRead(pin);
        delayMicroseconds(250);
    }
    return (int)(sum / samples);
}





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
        display.print(F("FODDER"));
        display.setCursor(44, 30);
        display.print(!SH_FODDER_LORA_ENABLED ? F("LORA OFF") :
                      !SH_FODDER_LORA_TX_ENABLED ? F("LORA RX ONLY") :
                      SH_FODDER_BLE_ENABLED ? F("LORA+BLE") : F("LORA"));
        display.drawRoundRect(4, 45, 120, 7, 2, SSD1306_WHITE);
        int barWidth = (116 * step) / totalSteps;
        if (barWidth > 0) {
            display.fillRoundRect(6, 47, barWidth, 3, 1, SSD1306_WHITE);
        }
        display.setCursor(10, 55);
        display.print(F("BOOTING LORA 868M"));
        display.display();
        feedWatchdog();
        delay(50);
    }
}

// -------------------------------------------------------------
// HARDWARE INITIALIZATION (GLITCH-FREE ACTIVE-LOW RELAY LATCH)
// -------------------------------------------------------------
static void logBootStage(const char* stage) {
    Serial.printf("[Boot] %lu ms: %s; free heap=%lu, minimum=%lu\n",
                  (unsigned long)millis(), stage,
                  (unsigned long)ESP.getFreeHeap(), (unsigned long)ESP.getMinFreeHeap());
}

static void initHardware() {
    uint8_t outputPins[] = {
        PIN_RELAY_SPRAY, PIN_RELAY_FAN_CIRC, PIN_RELAY_FAN_EXT, PIN_RELAY_LIGHT,
        PIN_DOSE_PH_UP, PIN_DOSE_PH_DOWN, PIN_DOSE_EC_UP, PIN_DOSE_EC_DOWN,
        47, 48
    };
    for (uint8_t pin : outputPins) {
        pinMode(pin, INPUT_PULLUP);
        digitalWrite(pin, HIGH); // Pre-load HIGH output latch before switching to OUTPUT
        pinMode(pin, OUTPUT);
        digitalWrite(pin, HIGH); // OFF (Active-LOW)
    }

    pinMode(MANUAL_TRIGGER_PIN, INPUT_PULLUP);

    // 1. Heltec V3 Vext Power Control (Active-LOW: Powers on-board OLED and external sensors)
    //    Enable Vext and initialize the SSD1306 OLED FIRST so the splash screen turns on immediately.
    pinMode(PIN_VEXT, OUTPUT);
    digitalWrite(PIN_VEXT, LOW);
    delay(80);

    // 2. Clean Hardware Reset pulse (HIGH -> LOW -> HIGH) for on-board SSD1306 OLED (GPIO 21)
    pinMode(PIN_OLED_RST, OUTPUT);
    digitalWrite(PIN_OLED_RST, HIGH);
    delay(20);
    digitalWrite(PIN_OLED_RST, LOW);
    delay(40);
    digitalWrite(PIN_OLED_RST, HIGH);
    delay(50);

    // 3. Initialize I2C Bus for SSD1306 at 100 kHz for maximum noise immunity
    pinMode(PIN_OLED_SDA, INPUT_PULLUP);
    pinMode(PIN_OLED_SCL, INPUT_PULLUP);
    Wire.begin(PIN_OLED_SDA, PIN_OLED_SCL);
    Wire.setClock(100000);
    Wire.setTimeOut(50);

    hasOled = display.begin(SSD1306_SWITCHCAPVCC, 0x3C, false, false);
    if (hasOled) {
        display.ssd1306_command(SSD1306_DISPLAYON);
        display.clearDisplay();
        display.display();
        showSplashScreen();
        Serial.println(F("[OLED] On-board SSD1306 (0x3C) initialized and splash screen displayed."));
    } else {
        Serial.println(F("[OLED] Warning: SSD1306 allocation failed."));
    }

    // 4. Configure 12-bit ADC1 and DHT22
    digitalWrite(PIN_ADC_CTRL, LOW);
    pinMode(PIN_ADC_CTRL, OUTPUT);
    sensorCalibration.begin();
    analogReadResolution(12);
    analogSetAttenuation(ADC_11db);
    analogRead(PIN_PH); analogRead(PIN_EC); analogRead(PIN_LIGHT);
    analogSetPinAttenuation(PIN_PH, ADC_11db);
    analogSetPinAttenuation(PIN_EC, ADC_11db);
    analogSetPinAttenuation(PIN_LIGHT, ADC_11db); // Lock 12-bit ADC resolution (0 - 4095)
    analogSetAttenuation(ADC_11db); // 11dB attenuation for full 0 - 3.1V eFuse-calibrated range across all ADC channels
    dht.begin();

    // Start BLE before putting LoRa into continuous receive.
    deviceMac = getDeviceMacAddress();
    logBootStage("before BLE initialization");
    initFodderBLE(deviceMac);
    logBootStage("after BLE setup or bypass, before LoRa");
    initNativeSX1262();
    logBootStage("after LoRa initialization");

    readSensors();
    updateOledDisplay();
}

// -------------------------------------------------------------
// FODDER MISTING (10s ON / 3h OFF), STRICT 16h/8h PHOTOPERIOD, & HYBRID ML + THRESHOLD AUTONOMY
// -------------------------------------------------------------
static void serviceFodderMistingSchedule() {
    unsigned long now = millis();

    // Low-water informational log only (never stops spray system, water pump, or dosing pumps)
    static unsigned long lastWaterWarnMs = 0;
    if (!currentTelemetry.waterOk && (now - lastWaterWarnMs >= 60000UL)) {
        lastWaterWarnMs = now;
        Serial.println(F("[Fodder Info] Reservoir water level is LOW, continuing spray & pump operations as normal."));
    }

    // Handle completion of manual/SignalR timed spray
    if (timedSprayEndMs > 0 && now >= timedSprayEndMs) {
        timedSprayEndMs = 0;
        setRelay(PIN_RELAY_SPRAY, false);
        g_lastSprayStartMs = now;
        Serial.println(F("[Fodder] Timed spray cycle finished -> Spray OFF."));
        broadcastCompactJson(true);
        updateOledDisplay();
        return;
    }

    // Handle completion of automatic 10-second misting burst
    if (g_autoSprayActive && now >= g_sprayAutoOffAtMs) {
        g_autoSprayActive = false;
        g_sprayAutoOffAtMs = 0;
        setRelay(PIN_RELAY_SPRAY, false);
        g_lastSprayStartMs = now;
        Serial.println(F("[Fodder Mist] 10-second misting burst complete -> Entering 3-hour rest period."));
        broadcastCompactJson(true);
        updateOledDisplay();
        return;
    }

    // Trigger 10-second misting burst every 3 hours (non-blocking, works 100% locally offline and regardless of water level)
    if (!g_autoSprayActive && timedSprayEndMs == 0 && (!manualOverrides.active(0))) {
        if (now - g_lastSprayStartMs >= FODDER_SPRAY_INTERVAL_MS) {
            g_lastSprayStartMs = now;
            g_autoSprayActive = true;
            g_sprayAutoOffAtMs = now + FODDER_SPRAY_DURATION_MS; // 10,000 ms (10 seconds)
            setRelay(PIN_RELAY_SPRAY, true);
            Serial.println(F("[Fodder Mist] Starting 10-second Fodder Misting Burst (3-hour schedule)..."));
            broadcastCompactJson(true);
            updateOledDisplay();
        }
    }
}

static void executeFodderAutonomy() {
    auditSource = 3;
    float ecFeature[1]   = { currentTelemetry.ec };
    float phFeature[1]   = { currentTelemetry.ph };
    float tempFeature[1] = { currentTelemetry.temp };
    float humFeature[1]  = { currentTelemetry.hum };

    currentTelemetry.mlEc   = modelEC.predict(ecFeature);
    currentTelemetry.mlPh   = modelPH.predict(phFeature);
    currentTelemetry.mlTemp = modelTemp.predict(tempFeature);
    currentTelemetry.mlHum  = modelHum.predict(humFeature);



    bool stateChanged = false;
    unsigned long now = millis();

    auditSource = 5;
    // 1. Strict 16 Hours ON / 8 Hours OFF Non-Dynamic Grow Light Schedule
    //    (Never dynamically turned off by the LDR sensor)
    unsigned long cyclePos = (now - g_lightScheduleStartMs) % LIGHT_CYCLE_MS;
    bool targetLight = (cyclePos < LIGHT_ON_DURATION_MS);
    if (!manualOverrides.active(1) && relays.growLight != targetLight) {
        setRelay(PIN_RELAY_LIGHT, targetLight);
        stateChanged = true;
    }

    auditSource = 3;
    // 2. Hybrid User Threshold + ML Climate Control (Anti-Mold Extractor & Circulation Fan)
    bool wantExtractor = (currentTelemetry.hum > targetMaxHum) ||
                         (currentTelemetry.temp > (targetMaxTemp + 2.0f)) ||
                         (currentTelemetry.mlHum == 0 && currentTelemetry.hum >= (targetMaxHum - 3.0f));
    if (!manualOverrides.active(3) && currentTelemetry.temp != -99.0f && currentTelemetry.hum >= 0.0f && relays.extractorFan != wantExtractor) {
        setRelay(PIN_RELAY_FAN_EXT, wantExtractor);
        stateChanged = true;
    }

    bool wantCircFan = (currentTelemetry.temp > targetMaxTemp) ||
                       (currentTelemetry.hum > (targetMaxHum - 5.0f)) ||
                       (currentTelemetry.mlTemp == 0 && currentTelemetry.temp >= (targetMaxTemp - 1.5f));
    if (!manualOverrides.active(2) && currentTelemetry.temp != -99.0f && currentTelemetry.hum >= 0.0f && relays.circulationFan != wantCircFan) {
        setRelay(PIN_RELAY_FAN_CIRC, wantCircFan);
        stateChanged = true;
    }

  // The edge owns dosing in every mode; cloud recommendations never add a second dose.
  if (g_activeDosePin == 0) {
    SHDosePlan plan = shPlanDose(currentTelemetry.ph, targetMinPh, targetMaxPh, currentTelemetry.mlPh, true);
    if (!plan.pump) plan = shPlanDose(currentTelemetry.ec, targetMinEc, targetMaxEc, currentTelemetry.mlEc, false);
    if (plan.pump && doseSafety.remaining() == 0) {
      const char* pumps[] = {"", "PH_UP", "PH_DOWN", "NUTRIENT_UP", "DILUTION_DOWN"};
      auditSource = plan.source;
      startAsyncDoseMs(pumps[plan.pump], plan.duration, "", false);
      auditSource = 5;
    }
  }

    if (stateChanged) {
        broadcastCompactJson(true);
        updateOledDisplay();
    }
}

// -------------------------------------------------------------
// SENSOR READING & PLAUSIBILITY VALIDATION
// -------------------------------------------------------------
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
    g_sensorFault = currentTelemetry.ph < 0.0f || currentTelemetry.ec < 0.0f;
    static unsigned long lastSensorDiagnosticMs = 0;
    if (millis() - lastSensorDiagnosticMs >= 15000UL) {
        lastSensorDiagnosticMs = millis();
        Serial.printf("[Sensors] pH %.2f (%.1f mV) | EC %.3f mS/cm (%.1f mV) | Temp %.1f C | Hum %.1f %% | Light %.1f %% | Water %s\n",
            currentTelemetry.ph, sensorCalibration.phMv, currentTelemetry.ec, sensorCalibration.ecMv,
            currentTelemetry.temp, currentTelemetry.hum, currentTelemetry.lux,
            currentTelemetry.waterOk ? "OK" : "LOW");
    }

}

// -------------------------------------------------------------
// LIVE OLED DASHBOARD RENDERING (0.96" SSD1306 128x64)
// -------------------------------------------------------------
static void updateOledDisplay() {
    if (!hasOled) return;
    if (blePairingVisible()) {
        display.clearDisplay(); display.setTextColor(SSD1306_WHITE);
        display.setTextSize(1); display.setCursor(0, 0);
        display.print(g_bleDeviceName); display.setCursor(0, 20);
        display.print("BLE pairing code"); display.setCursor(0, 36);
        display.printf("%06lu", (unsigned long)g_blePasskey);
        display.display(); return;
    }

    display.clearDisplay();
    display.setTextColor(SSD1306_WHITE);
    display.setTextSize(1);
    display.setTextWrap(false);
    display.setCursor(0, 0);
    display.print(F("SMARTHYDRO FODDER"));
    display.setCursor(0, 10);
    display.print(deviceMac.c_str());
    display.setCursor(0, 22);
    display.print(F("pH:"));
    if (currentTelemetry.ph >= 0) display.print(currentTelemetry.ph, 2); else display.print(F("--"));
    display.print(F(" EC:"));
    if (currentTelemetry.ec >= 0) display.print(currentTelemetry.ec, 2); else display.print(F("--"));
    display.print(F("mS/cm"));
    display.setCursor(0, 33);
    display.print(F("T:"));
    if (currentTelemetry.temp >= -40) display.print(currentTelemetry.temp, 1); else display.print(F("--"));
    display.print(F("C H:"));
    if (currentTelemetry.hum >= 0) display.print(currentTelemetry.hum, 0); else display.print(F("--"));
    display.print(F("%"));
    display.setCursor(0, 44);
    display.print(currentTelemetry.waterOk ? F("H2O:OK Lgt:") : F("H2O:LOW Lgt:"));
    if (currentTelemetry.lux >= 0) display.print(currentTelemetry.lux, 0); else display.print(F("--"));
    display.print(F("%"));
    display.setCursor(0, 54);
    if (g_operatingMode == MODE_WIFI_CLOUD && WiFi.status() == WL_CONNECTED)
        display.printf("WiFi:%ddBm", WiFi.RSSI());
    else if (g_gatewayRssi != 0)
        display.printf("GW:%ddBm (last)", g_gatewayRssi);
    else
        display.print(F("GW: waiting for ACK"));
    display.display();
}
// -------------------------------------------------------------
// CLOUD INGESTION (STANDBY MODE_WIFI_CLOUD)
// -------------------------------------------------------------
#define SH_HANDLE_CAPTURE_COMMAND(command) applyIncomingCommandJson(command, "HTTP", false)
#define SH_SEND_STORED_LORA(wire) do { g_lastLoraTxMs = millis(); broadcastLoRaCompactJson(wire, SH_PRIO_BACKLOG); } while (0)
#include "TelemetryTransport.h"

// -------------------------------------------------------------
// SETUP & MAIN LOOP
// -------------------------------------------------------------
#include "ConnectivityManager.h"

void setup() {
    if (SH_FODDER_BLE_ENABLED) bleCommandQueue = xQueueCreate(4, 512);
    // Latch active-low relays OFF before peripheral initialization.
    latchFodderRelaysOff();

    // Scale ESP32-S3 dual-core clock from 240 MHz to 80 MHz (~77 mA continuous current savings on 3.3V LDO rail)
    setCpuFrequencyMhz(80);

    Serial.begin(115200);
    delay(250);
    Serial.println(F("\n=== SmartHydro Fodder Sprouting Node (Heltec WiFi LoRa 32 V3) Initializing ==="));
    esp_reset_reason_t resetReason = esp_reset_reason();
    Serial.printf("[Boot] Reset reason=%d%s\n", (int)resetReason,
                  resetReason == ESP_RST_BROWNOUT ? " (brownout: check the power supply)" : "");
    logBootStage("starting hardware initialization");

    initHardwareWatchdog(30);
    initHardware();
    logBootStage("before outbox storage");
    beginTelemetryStorage();
    doseSafety.begin();
  beginActivityStorage();

    // Load saved operational mode and thresholds from NVS Flash
    // Default on first boot is MODE_LORA_BLE (0): Wi-Fi OFF, 868.1 MHz LoRa + BLE active
    prefs.begin("smarthydro", true);
    targetMinPh   = prefs.getFloat("min_ph", 5.8f);
    targetMaxPh   = prefs.getFloat("max_ph", 6.4f);
    targetMinEc   = prefs.getFloat("min_ec", 0.8f);
    targetMaxEc   = prefs.getFloat("max_ec", 1.6f);
    targetMaxTemp = prefs.getFloat("max_temp", 24.0f);
    targetMaxHum  = prefs.getFloat("max_hum", 75.0f);
    prefs.end();

    prefs.begin("smarthydro", true);
    bool pumpOn = prefs.getBool("pump_on", false);
    bool lightOn = prefs.getBool("light_on", false);
    bool fanOn = prefs.getBool("fan_on", false);
    bool extOn = prefs.getBool("ext_on", false);
    prefs.end();
    setRelay(PIN_RELAY_SPRAY, pumpOn);
    setRelay(PIN_RELAY_LIGHT, lightOn);
    setRelay(PIN_RELAY_FAN_CIRC, fanOn);
    setRelay(PIN_RELAY_FAN_EXT, extOn);
    g_dailyWindowStartMs = millis();
    g_lightScheduleStartMs = millis() - (lightOn ? 0 : LIGHT_ON_DURATION_MS);
    g_lastSprayStartMs = millis();
    if (pumpOn) {
        g_autoSprayActive = true;
        g_sprayAutoOffAtMs = millis() + FODDER_SPRAY_DURATION_MS;
    }
    loadConnectivityState();
    logBootStage("startup complete");

}

void loop() {
    static bool wasPairing = false;
    bool pairingNow = blePairingVisible();
    if (pairingNow != wasPairing) { wasPairing = pairingNow; updateOledDisplay(); }

    char bleMessage[512];
    if (bleCommandQueue && xQueueReceive(bleCommandQueue, bleMessage, 0) == pdTRUE)
        handleBleCommandPayload(String(bleMessage));
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

    // 1. Check 3-second PRG/BOOT button hold (GPIO 0) for Emergency SoftAP toggle
    checkBootButtonModeToggle();

    // 2. Service non-blocking dual-tier dosing & 10s ON / 3h OFF fodder misting state machine
    serviceActiveDose();
    serviceFodderMistingSchedule();

    // 3. Service SoftAP WebServer when in Emergency SoftAP or Setup Portal mode
    if (g_operatingMode == MODE_EMERGENCY_SOFTAP || portalActive) {
        handleProvisioningLoop();
    }

    // 4. Check for incoming 868.1 MHz LoRa downlink commands from Gateway (Continuous RX mode)
    String loraDownlinkPayload;
    if (checkLoraDownlink(loraDownlinkPayload)) {
        handleLoraDownlinkCommand(loraDownlinkPayload);
    }

    // 4b. Flush any deferred event-driven LoRa TX as soon as the 2.5s RF cooldown expires
    if (g_pendingImmediateLoraTx && g_isLoRaActive && (g_lastLoraTxMs == 0 || (now - g_lastLoraTxMs >= 2500UL))) {
        broadcastCompactJson(true);
    }

    // 5. Periodic 15-second LoRa sub-GHz telemetry broadcast (15s nominal +- 2000ms jitter)
    static unsigned long lastLoRaBroadcastMs = 0;
    static unsigned long currentLoraIntervalMs = LORA_REPORT_INTERVAL_MS;
    if (now - lastLoRaBroadcastMs >= currentLoraIntervalMs) {
        lastLoRaBroadcastMs = now;
        long jitter = g_operatingMode == MODE_LORA_BLE ? random(-2000, 2001) : 0;
        currentLoraIntervalMs = (unsigned long)((long)LORA_REPORT_INTERVAL_MS + jitter);
        broadcastCompactJson(true);
        updateOledDisplay();
    }

    // 6. Standby Cloud Wi-Fi + SignalR WebSocket service (only active in MODE_WIFI_CLOUD)
    if (g_operatingMode == MODE_WIFI_CLOUD && !portalActive && WiFi.status() == WL_CONNECTED) {
        if (!wsInitialized) {
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

    // 7. Sensor polling & Hybrid Threshold + ML autonomy evaluation every 5 seconds
    if (now - lastSensorPollMs >= 5000UL) {
        lastSensorPollMs = now;
        readSensors();
        executeFodderAutonomy();
        broadcastCompactJson(false);
        ensureFodderBleAdvertising();

        if (g_operatingMode == MODE_EMERGENCY_SOFTAP) {
            pushOfflineRecord(
                currentTelemetry.ph, currentTelemetry.ec, currentTelemetry.temp,
                currentTelemetry.hum, currentTelemetry.lux, currentTelemetry.waterRaw,
                currentTelemetry.waterOk
            );
        }
    }

    // 8. Store telemetry over HTTP every 15s; SignalR carries instant commands.
    static bool initialCloudSyncDone = false;
    if (g_operatingMode == MODE_WIFI_CLOUD && !portalActive && WiFi.status() == WL_CONNECTED) {
        if (!initialCloudSyncDone || (now - lastCloudSyncMs >= 15000UL)) {
            initialCloudSyncDone = true;
            lastCloudSyncMs = now;
            syncWithCloud();
        }
    }
    serviceStoredTelemetry();
    serviceConnectivity();
}
