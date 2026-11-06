// ==============================================================================
// MAKERFABS SENSELORA 4G LTE CAT1 GATEWAY - PRODUCTION FIRMWARE (MODEL: SL4GGY)
// MCU: ESP32-S3 (16MB Flash / 8MB PSRAM)
// LoRa Transceiver: Semtech SX1276 (868.1 MHz, SF7, BW 125 kHz, CR 4/5)
// 4G Cellular Modem: SIMCom; model checked over UART2 at startup.
// Carrier: Telkom South Africa (APN: "internet")
// Cloud Target: Azure ASP.NET 10 API (/api/telemetry/lora & /api/telemetry/lora/poll)
//
// HTTP service reuse and keep-alive avoid needless setup. Socket reuse, SIM data
// usage and end-to-end command latency still need measurements on the gateway.
// ==============================================================================

#include <Arduino.h>
#include <ArduinoJson.h>
#include <FS.h>
#include <LittleFS.h>
#include <RadioLib.h>
#include <SPI.h>
#include <esp_task_wdt.h>
#include "soc/soc.h"
#include "soc/rtc_cntl_reg.h"
#include "esp_err.h"
#include "esp_private/startup_internal.h"
#include "esp_private/brownout.h"
#include <vector>
#include "SmartHydroFrame.h"
#include "GatewayTrust.h"
#if __has_include("secrets.h")
#include "secrets.h"
#endif
#ifndef SMARTHYDRO_DEVICE_KEY
#define SMARTHYDRO_DEVICE_KEY ""
#endif
#include "GatewayRecovery.h"
#include "GatewayUplinkPolicy.h"
#include "GatewaySpool.h"
#include "TelemetryReceiptFrame.h"
#include <Preferences.h>
#include <sys/time.h>
#include <esp_timer.h>

// Early-boot brownout override (ESP-IDF 5.x) to prevent RF/modem surge brownout resets
static void IRAM_ATTR disableGatewayBrownoutHw() {
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
    disableGatewayBrownoutHw();
}

extern "C" void esp_brownout_disable(void) {
    disableGatewayBrownoutHw();
}

ESP_SYSTEM_INIT_FN(early_gateway_boot_stabilize, CORE, BIT(0), 1) {
    disableGatewayBrownoutHw();
    return ESP_OK;
}

__attribute__((constructor(101))) static void earlyDisableGatewayBrownout() {
    disableGatewayBrownoutHw();
}

// ------------------------------------------------------------------------------
// 1. HARDWARE PIN DEFINITIONS (Makerfabs SenseLoRa 4G Gateway)
// ------------------------------------------------------------------------------
#define IO_RXD2             1   // ESP32-S3 RX <- Modem TX
#define IO_TXD2             2   // ESP32-S3 TX -> Modem RX
#define IO_GSM_PWRKEY       42  // Modem Power Key
#define IO_GSM_RST          41  // Modem Hardware Reset
#define POWER_3V3_PIN       21  // Internal 3.3V Power Rail Switch
#define BAT_PIN             14  // Battery Voltage Sense ADC

// Semtech SX1276 LoRa (SPI Bus)
#define LORA_MOSI           11
#define LORA_MISO           13
#define LORA_SCK            12
#define LORA_CS             4
#define LORA_RST            5
#define LORA_DIO0           6
#define LORA_DIO1           7

// ------------------------------------------------------------------------------
// 2. LONG-RANGE RF PARAMETERS (Matched to Heltec V3 & XIAO S3 Wio-SX1262)
//    South Africa ICASA 868.0-868.6 MHz SRD Band: Max +14 dBm ERP, <=1% Duty Cycle
// ------------------------------------------------------------------------------
#define LORA_FREQUENCY      868.1f
#define LORA_BANDWIDTH      125.0f
#define LORA_SPREAD_FACTOR  7
#define LORA_CODING_RATE    5
#define LORA_SYNC_WORD      0x12
#define LORA_OUTPUT_POWER   14
#define LORA_PREAMBLE_LEN   8

// ------------------------------------------------------------------------------
// 3. TELKOM CELLULAR APN & AZURE CLOUD ENDPOINTS
// ------------------------------------------------------------------------------
#define CELLULAR_APN        "internet"
#define CLOUD_API_URL       "https://smarthydroapi-exhjg0b2f9bqe4e6.southafricanorth-01.azurewebsites.net/api/telemetry/lora?compact=true"
#define CLOUD_POLL_URL      "https://smarthydroapi-exhjg0b2f9bqe4e6.southafricanorth-01.azurewebsites.net/api/telemetry/lora/poll?waitMs=4000&compact=true"
#define GATEWAY_CLOUD_UP_WINDOW_MS 60000UL

// Fast Downlink Poll & Offline Flush Intervals
#define FAST_POLL_INTERVAL_MS      100UL   // Small gap; the server holds idle polls for four seconds
#define BACKLOG_FLUSH_INTERVAL_MS 15000UL  // Retry LittleFS offline backlog every 15s
#define MODEM_RECHECK_INTERVAL_MS 20000UL  // Verify cellular PDP attachment every 20s
#define LORA_REARM_INTERVAL_MS    60000UL  // Periodically re-arm SX1276 continuous RX every 60s
#define BUFFER_FILE_PATH          "/telemetry_queue.txt"
#define MAX_RAM_QUEUE_SIZE        24
#define MAX_DURABLE_RECORDS       256
#define MAX_DURABLE_PACKET_BYTES  2048
#define GATEWAY_WDT_TIMEOUT_SEC   60

// ------------------------------------------------------------------------------
// 4. GLOBAL OBJECTS & MULTI-NODE FIFO QUEUE
// ------------------------------------------------------------------------------
HardwareSerial SerialModem(2);
SX1276 loraRadio = new Module(LORA_CS, LORA_DIO0, LORA_RST, LORA_DIO1, SPI, SPISettings());

volatile bool g_loraPacketReceived = false;
static float g_lastRssi = 0.0f;
static float g_lastSnr = 0.0f;
static unsigned long g_lastPollMs = 0;
static unsigned long g_lastBacklogFlushMs = 0;
static unsigned long g_lastModemCheckMs = 0;
static unsigned long g_lastLoraRxActivityMs = 0;
static bool g_modemNetworkOk = false;
static bool g_httpSessionOpen = false;
static int g_consecutiveHttpFailures = 0;
static GatewayRecovery g_recovery;
static bool g_tlsConfigured = false;
static bool g_supportedModem = false;
static bool g_isSim7600 = false;
static uint32_t g_lastHttpDurationMs = 0;
static bool g_pollAfterUplink = false;
static bool g_ackBeforePollUsed = false;
#include "GatewayNodeRoutes.h"
static SHGatewayNodeRoutes g_nodeRoutes;
static uint32_t g_gatewayBootId = 0;
static uint64_t g_jsonUplinkBytes = 0, g_jsonDownlinkBytes = 0;
static uint32_t g_postCount = 0, g_pollCount = 0;
static std::vector<TelemetryReceiptFrame> g_telemetryReceipts;
static std::vector<String> g_towerCommandReceipts;
void flushTowerCommandReceipt();
void queueTelemetryReceipt(const String& payload, bool stored);
void flushTelemetryReceipt();
String prepareGatewayPayload(const String& payload);
static unsigned long g_totalUplinksOk = 0;
static unsigned long g_totalDownlinksSent = 0;
static unsigned long g_lastCloudSuccessMs = 0;
static SmartHydroAirtimeLedger g_airtimeLedger;
static bool g_commandTxActive = false;
static uint32_t g_commandTxStartedMs = 0, g_commandTxTimeoutMs = 0;

static inline bool isGatewayCloudUp() {
    if (!g_modemNetworkOk || g_lastCloudSuccessMs == 0) return false;
    return (millis() - g_lastCloudSuccessMs) <= GATEWAY_CLOUD_UP_WINDOW_MS;
}

// Multi-Node RAM FIFO Queue (prevents Fodder and Vertical Tower packets from colliding)
static std::vector<String> g_uplinkQueue;
static SHStoredCaptureCache g_storedCaptures;
static String g_pendingAckMac = "";
static int g_pendingAckRssi = 0;

// Interrupt Service Routine for LoRa RX
void IRAM_ATTR onLoraRxInterrupt() {
    g_loraPacketReceived = true;
}

// Forward declarations
void initGatewayWatchdog();
void feedGatewayWatchdog();
#include "GatewayFlash.h"
void initHardwarePins();
void hardwareResetModem();
bool initLoRaRadio();
bool initCellularModem();
bool ensurePdpContextActive();
bool ensureHttpSessionOpen();
void closeHttpSession();
void registerHttpOutcome(bool success, int httpStatus);
bool sendATCommand(const String &cmd, const char *expectedResp, unsigned long timeoutMs);
String modemResponse(const char* expected, unsigned long timeoutMs);
String queryModem(const String& command, unsigned long timeoutMs = 2000);
bool configureGatewayTls();
bool forwardPayloadToCloud(const String &jsonPayload);
void pollCloudDownlinkCommands();
void processCloudDownlinkResponse(const String &httpBody, const String &defaultMac);
String readModemHttpResponseBody(int contentLen);
void enqueueUplinkPacket(const String &jsonPayload);
static bool isCommandAcknowledgement(const String& payload);
void queueTelemetryReceipt(const String& payload, bool stored);
static String compactHexMacGateway(const String &mac);
void serviceImmediateLoRaRx(bool deferAckTx = false);
void flushPendingLoRaAck();
bool bufferPayloadToFlash(const String &jsonPayload);
String gatewaySpoolPath(const String& payload);
void removeStoredGatewayPacket(const String& payload);
void flushBufferedQueue();
void queueSmsNotification(const String &message);
void processSmsStateMachine();
void printOfflineBuffer();
void printGatewayStatus();
bool transmitLoraDownlink(String cmdJson);

// ------------------------------------------------------------------------------
// NON-BLOCKING SMS ALERTING PIPELINE (SIMCom A7670E)
// ------------------------------------------------------------------------------
#define SMS_RECIPIENT_DEFAULT ""
#define ENABLE_SMS_ALERTS     false
static String g_smsRecipient = SMS_RECIPIENT_DEFAULT;
static bool g_smsPending = false;
static String g_smsBody = "";
static unsigned long g_smsStepMs = 0;
static int g_smsState = 0;

void initGatewayWatchdog() {
#if defined(ESP_IDF_VERSION_VAL) && ESP_IDF_VERSION >= ESP_IDF_VERSION_VAL(5, 0, 0)
    esp_task_wdt_config_t wdt_config = {
        .timeout_ms = GATEWAY_WDT_TIMEOUT_SEC * 1000,
        .idle_core_mask = 0,
        .trigger_panic = true
    };
    if (esp_task_wdt_reconfigure(&wdt_config) != ESP_OK) {
        esp_task_wdt_init(&wdt_config);
    }
#else
    esp_task_wdt_init(GATEWAY_WDT_TIMEOUT_SEC, true);
#endif
    esp_task_wdt_add(NULL);
}

inline void feedGatewayWatchdog() {
    esp_task_wdt_reset();
}

// ------------------------------------------------------------------------------
// 5. SETUP
// ------------------------------------------------------------------------------
void setup() {
    WRITE_PERI_REG(RTC_CNTL_BROWN_OUT_REG, 0);
    Serial.begin(115200);
    delay(1500);
    Serial.println(F("\n======================================================="));
    Serial.println(F("  SmartHydro SenseLoRa 4G LTE CAT1 Gateway Starting... "));
    Serial.println(F("  Carrier: Telkom SA | APN: internet | LoRa: 868.1MHz  "));
    Serial.println(F("  Mode: Persistent HTTPS Keep-Alive + Long-Poll        "));
    Serial.println(F("======================================================="));

    initGatewayWatchdog();
    Preferences bootPreferences;
    if (bootPreferences.begin("sh-gateway", false)) {
        g_gatewayBootId = bootPreferences.getUInt("boot", 0) + 1;
        if (bootPreferences.putUInt("boot", g_gatewayBootId) != sizeof(uint32_t)) g_gatewayBootId = 0;
        bootPreferences.end();
    }

    if (!mountGatewayFilesystem()) {
        Serial.println(F("[Flash] LittleFS mount failed. Existing flash was preserved; inspect or recover the backlog before formatting."));
    } else {
        Serial.println(F("[Flash] LittleFS mounted. Store-and-Forward active."));
    }

    initHardwarePins();

    if (!initLoRaRadio()) {
        Serial.println(F("[LoRa] CRITICAL: SX1276 Initialization Failed! Check SPI wiring."));
    }

    if (!initCellularModem()) {
        Serial.println(F("[4G] WARNING: Initial Telkom registration incomplete. Will auto-recover in loop."));
    } else {
        Serial.println(F("[4G] [SUCCESS] Telkom 4G LTE CAT1 Attached & PDP Context Active!"));
    }

    g_lastLoraRxActivityMs = millis();
    Serial.println(F("[Gateway] Listening for Fodder (Heltec V3) & Vertical Tower (XIAO S3) on 868.1 MHz..."));
}

// ------------------------------------------------------------------------------
// 6. MAIN LOOP
// ------------------------------------------------------------------------------
void loop() {
    feedGatewayWatchdog();

    // A. Immediately service any incoming LoRa packet into the FIFO queue + send RF ACK
    serviceImmediateLoRaRx(false);
    flushPendingLoRaAck();

    // B. Non-blocking Cellular SMS State Machine
    processSmsStateMachine();
    flushTelemetryReceipt();
    flushTowerCommandReceipt();

    unsigned long now = millis();

    // C. Periodic SX1276 Continuous Receive Watchdog (re-arms RX if idle for 60s)
    if (!g_commandTxActive && !g_loraPacketReceived && now - g_lastLoraRxActivityMs >= LORA_REARM_INTERVAL_MS) {
        g_lastLoraRxActivityMs = now;
        loraRadio.startReceive();
    }

    bool acknowledgementWaiting = false;
    for (const auto& packet : g_uplinkQueue) {
        if (isCommandAcknowledgement(packet)) { acknowledgementWaiting = true; break; }
    }
    // Allow one waiting receipt ahead of the next poll; then give downlinks a turn.
    if (!g_uplinkQueue.empty() && shUploadBeforePoll(g_pollAfterUplink, acknowledgementWaiting, g_ackBeforePollUsed)
        && g_modemNetworkOk && g_recovery.ready(now)) {
        if (g_pollAfterUplink) g_ackBeforePollUsed = true;
        // Keep new relay reports responsive while still draining older history.
        static unsigned completedSlots = 0;
        size_t selected = shSelectUplink(g_uplinkQueue.size(), ++completedSlots, [](size_t i) {
                StaticJsonDocument<1024> candidate;
                if (deserializeJson(candidate, g_uplinkQueue[i]) != DeserializationError::Ok) return 2;
                bool ack = (candidate["cack"] | 0) == 1;
                int64_t captured = candidate["ts"] | (int64_t)0;
                int64_t age = -1, utc = time(nullptr);
                if (captured && utc >= 1704067200LL) age = utc - captured;
                else if ((candidate["gb"] | 0UL) == g_gatewayBootId)
                    age = (candidate["age"] | 0UL) + ((uint64_t)(esp_timer_get_time() / 1000) - (candidate["gq"] | (uint64_t)0)) / 1000ULL;
                return shUplinkPriority(ack, candidate.containsKey("ev"), candidate["tu"] | false, age);
        });
        String nextPacket = g_uplinkQueue[selected];
        g_uplinkQueue.erase(g_uplinkQueue.begin() + selected);

        Serial.printf("[Uplink] >>> Persistent HTTPS POST to Azure (%d bytes, %u remaining in queue)...\n",
                      (int)nextPacket.length(), (unsigned int)g_uplinkQueue.size());

        bool success = forwardPayloadToCloud(nextPacket);
        if (success) removeStoredGatewayPacket(nextPacket);
        g_pollAfterUplink = true;
        flushPendingLoRaAck();

        if (success) {
            g_totalUplinksOk++;
            Serial.printf("[Uplink] [OK] API confirmed database storage (Total OK: %lu)\n",
                          g_totalUplinksOk);
        } else {
            Serial.println(F("[Uplink] No storage confirmation; the durable copy remains queued."));
        }

        // Check if another LoRa packet arrived while the modem was busy
        serviceImmediateLoRaRx(false);
        flushPendingLoRaAck();
        return;
    }

    // E. Priority 2: Persistent Keep-Alive Long-Poll for Instant MVC Commands
    if (now - g_lastPollMs >= FAST_POLL_INTERVAL_MS) {
        if (g_modemNetworkOk && g_recovery.ready(now)) {
            pollCloudDownlinkCommands();
            g_lastPollMs = millis();
            g_pollAfterUplink = false;
            g_ackBeforePollUsed = false;
            flushPendingLoRaAck();
        }
        serviceImmediateLoRaRx(false);
        flushPendingLoRaAck();
    }

    // Give persistent history a turn even while live readings keep arriving.
    if ((!acknowledgementWaiting || millis() - g_lastBacklogFlushMs >= 60000UL)
        && (millis() - g_lastBacklogFlushMs >= BACKLOG_FLUSH_INTERVAL_MS)) {
        g_lastBacklogFlushMs = now;
        if (g_modemNetworkOk && g_recovery.ready(millis())) {
            flushBufferedQueue();
            flushPendingLoRaAck();
        }
    }

    // G. Periodic Cellular PDP Context Health Check & Auto-Recovery (every 20 seconds)
    if (uint32_t(millis() - g_lastModemCheckMs) >= MODEM_RECHECK_INTERVAL_MS) {
        g_lastModemCheckMs = millis();
        if (!g_modemNetworkOk && g_recovery.ready(millis())) {
            Serial.println(F("[4G] Attempting automatic Telkom 4G LTE reconnection..."));
            closeHttpSession();
            if (!g_supportedModem) initCellularModem();
            else if (!ensurePdpContextActive()) {
                if (g_recovery.attachFailures < 10) ++g_recovery.attachFailures;
                g_recovery.failed(millis());
                if (g_recovery.shouldReset(millis())) {
                    g_recovery.resetStarted(millis());
                    hardwareResetModem();
                    initCellularModem();
                }
            }
        }
    }

    // H. Interactive Serial Terminal Commands (Diagnostics & Manual Downlink Testing)
    if (Serial.available()) {
        String userCmd = Serial.readStringUntil('\n');
        userCmd.trim();
        String lowerCmd = userCmd;
        lowerCmd.toLowerCase();

        if (lowerCmd == "dump" || lowerCmd == "cat" || lowerCmd == "log") {
            printOfflineBuffer();
        } else if (lowerCmd == "sms") {
            queueSmsNotification("SmartHydro Gateway manual test SMS! Cellular link verified.");
        } else if (lowerCmd == "clear") {
            File root = LittleFS.open("/");
            std::vector<String> paths;
            for (File file = root.openNextFile(); file; file = root.openNextFile()) {
                String name = file.name(); file.close();
                if (name.startsWith("up_") && name.endsWith(".rec")) paths.push_back("/" + name);
            }
            root.close();
            bool cleared = true;
            for (const auto& path : paths) if (!LittleFS.remove(path)) cleared = false;
            if (LittleFS.exists(BUFFER_FILE_PATH) && !LittleFS.remove(BUFFER_FILE_PATH)) cleared = false;
            g_uplinkQueue.clear();
            Serial.println(cleared ? F("[Flash] Queue cleared by serial request; controllers may repeat unconfirmed packets.")
                                   : F("[Flash] Some queue files could not be cleared."));
        } else if (lowerCmd == "poll") {
            pollCloudDownlinkCommands();
        } else if (lowerCmd.startsWith("send ") || lowerCmd.startsWith("cmd ")) {
            String json = userCmd.substring(userCmd.indexOf(' ') + 1);
            transmitLoraDownlink(json);
        } else if (lowerCmd == "status" || lowerCmd == "info") {
            printGatewayStatus();
        }
    }
}

// ------------------------------------------------------------------------------
// 7. LORA RECEPTION & FIFO QUEUE MANAGEMENT
// ------------------------------------------------------------------------------
static bool isCommandAcknowledgement(const String& payload) {
    StaticJsonDocument<512> packet;
    return deserializeJson(packet, payload) == DeserializationError::Ok && (packet["cack"] | 0) == 1;
}

void enqueueUplinkPacket(const String &incomingPayload) {
    String jsonPayload = incomingPayload;
    StaticJsonDocument<1024> queued;
    StaticJsonDocument<1024> identity;
    if (deserializeJson(identity, incomingPayload) == DeserializationError::Ok && (identity["cack"] | 0) == 1 && (identity["ar"] | 0) == 1) {
        for (auto& item : g_uplinkQueue) {
            StaticJsonDocument<512> existing;
            if (deserializeJson(existing, item) == DeserializationError::Ok && (existing["ar"] | 0) == 1 &&
                (existing["cid"] | 0) == (identity["cid"] | 0) && compactHexMacGateway(existing["m"] | "") == compactHexMacGateway(identity["m"] | "")) {
                String oldPhase = existing["phase"] | "accepted", nextPhase = identity["phase"] | "accepted";
                uint64_t oldSequence = existing["rs"] | (uint64_t)0, nextSequence = identity["rs"] | (uint64_t)0;
                if ((oldPhase == "accepted") == (nextPhase == "accepted")
                    && oldSequence && nextSequence && oldSequence > nextSequence) return;
                if (oldPhase == "accepted" || nextPhase != "accepted") {
                    if (bufferPayloadToFlash(incomingPayload)) item = incomingPayload;
                }
                return;
            }
        }
    }
    if (deserializeJson(identity, incomingPayload) == DeserializationError::Ok && identity["seq"].is<uint64_t>()) {
        uint64_t sequence = identity["seq"].as<uint64_t>();
        String mac = compactHexMacGateway(identity["m"] | "");
        if (mac.length() == 12 && g_storedCaptures.contains(strtoull(mac.c_str(), nullptr, 16), sequence, millis())) {
            queueTelemetryReceipt(incomingPayload, true);
            return;
        }
        for (const auto& item : g_uplinkQueue) {
            StaticJsonDocument<1024> existing;
            if (sequence && mac.length() == 12 && deserializeJson(existing, item) == DeserializationError::Ok && (existing["seq"] | (uint64_t)0) == sequence &&
                compactHexMacGateway(existing["m"] | "") == mac) return;
        }
    }
    if (deserializeJson(queued, incomingPayload) == DeserializationError::Ok && (queued["cack"] | 0) != 1) {
        queued["gq"] = (uint64_t)(esp_timer_get_time() / 1000);
        queued["gb"] = g_gatewayBootId;
        time_t utc = time(nullptr);
        if (!(queued["tu"] | false) && !queued["ts"].is<int64_t>() && utc >= 1704067200)
            queued["ts"] = (int64_t)utc - (queued["age"] | 0UL);
        jsonPayload = ""; serializeJson(queued, jsonPayload);
    }
    if (!bufferPayloadToFlash(jsonPayload)) {
        Serial.println(F("[Flash] Packet not accepted: durable queue unavailable or full. No stored receipt sent; controller must retry."));
        return;
    }
    if (g_uplinkQueue.size() >= MAX_RAM_QUEUE_SIZE) {
        auto oldest = g_uplinkQueue.begin();
        for (auto packet = g_uplinkQueue.begin(); packet != g_uplinkQueue.end(); ++packet) {
            if (!isCommandAcknowledgement(*packet)) { oldest = packet; break; }
        }
        // Every RAM entry already has a checked copy on flash.
        g_uplinkQueue.erase(oldest);
    }
    if (isCommandAcknowledgement(jsonPayload)) {
        auto next = g_uplinkQueue.begin();
        while (next != g_uplinkQueue.end() && isCommandAcknowledgement(*next)) ++next;
        g_uplinkQueue.insert(next, jsonPayload);
    } else {
        g_uplinkQueue.push_back(jsonPayload);
    }
}

void flushPendingLoRaAck() {
    if (g_commandTxActive) return;
    if (g_pendingAckMac.length() > 0) {
        int upFlag = isGatewayCloudUp() ? 1 : 0;
        String ackCmd = "{\"m\":\"" + g_pendingAckMac + "\",\"ack\":1,\"up\":" + String(upFlag) + ",\"r\":" + String(g_pendingAckRssi) + "}";
        if (transmitLoraDownlink(ackCmd)) g_pendingAckMac = "";
    }
}

struct PendingNodeCommand {
    String mac, payload;
    uint16_t cid = 0;
    uint32_t lastAttempt = 0;
    uint32_t receivedAt = 0, ttlMs = 60000;
    uint8_t attempts = 0;
};
static PendingNodeCommand pendingNodeCommands[4];
static bool startCommandTransmission(String& payload) {
    uint32_t toaUs = (uint32_t)loraRadio.getTimeOnAir(payload.length());
    uint16_t toaMs = (uint16_t)((toaUs + 999UL) / 1000UL);
    if (!toaMs || !g_airtimeLedger.canTransmit(toaMs, SH_PRIO_CRITICAL_ACK)) return false;
    if (loraRadio.startTransmit(payload) != RADIOLIB_ERR_NONE) { loraRadio.startReceive(); return false; }
    g_airtimeLedger.recordTransmission(toaMs);
    g_commandTxActive = true; g_commandTxStartedMs = millis(); g_commandTxTimeoutMs = toaMs + 1000UL;
    return true;
}
static void serviceCommandRetries() {
    if (g_commandTxActive || g_loraPacketReceived) return;
    for (auto& pending : pendingNodeCommands) {
        if (pending.cid && (uint32_t)(millis() - pending.receivedAt) >= pending.ttlMs) {
            Serial.printf("[Command] CID %u expired before controller confirmation.\n", pending.cid);
            pending = PendingNodeCommand(); continue;
        }
        if (!pending.cid || (uint32_t)(millis() - pending.lastAttempt) < 2500UL) continue;
        if (pending.attempts >= 4) {
            Serial.printf("[Command] CID %u has no RF reply after four attempts; awaiting API retry.\n", pending.cid);
            pending = PendingNodeCommand(); continue;
        }
        pending.lastAttempt = millis();
        StaticJsonDocument<512> wireCommand;
        if (deserializeJson(wireCommand, pending.payload) != DeserializationError::Ok) { pending = PendingNodeCommand(); continue; }
        uint32_t elapsed = uint32_t(millis() - pending.receivedAt);
        if (elapsed >= pending.ttlMs) { pending = PendingNodeCommand(); continue; }
        wireCommand["ttl"] = pending.ttlMs - elapsed;
        String wire; serializeJson(wireCommand, wire);
        if (startCommandTransmission(wire)) {
            pending.attempts++; g_totalDownlinksSent++;
            Serial.printf("[Command] LoRa TX CID %u to %s, attempt %u.\n", pending.cid, pending.mac.c_str(), pending.attempts);
        }
        break;
    }
}
static void stageNodeCommand(const String& payload) {
    StaticJsonDocument<512> doc;
    if (deserializeJson(doc, payload) != DeserializationError::Ok) return;
    uint16_t cid = doc["cid"] | 0;
    if (!cid) { if (transmitLoraDownlink(payload)) g_totalDownlinksSent++; return; }
    String mac = doc["m"] | ""; mac.replace(":", ""); mac.toUpperCase();
    for (auto& pending : pendingNodeCommands) {
        if (pending.cid == cid && pending.mac == mac) return;
        if (pending.cid && pending.mac == mac) pending = PendingNodeCommand();
    }
    for (auto& pending : pendingNodeCommands) if (!pending.cid) {
        pending.mac = mac; pending.payload = payload; pending.cid = cid;
        pending.receivedAt = millis(); pending.ttlMs = min((uint32_t)(doc["ttl"] | 60000UL), (uint32_t)60000UL);
        pending.lastAttempt = millis() - 2500UL;
        Serial.printf("[Command] Queued CID %u for %s, expires in %lu ms.\n", cid, mac.c_str(), (unsigned long)pending.ttlMs);
        return;
    }
}
static void acknowledgeNodeCommand(JsonObject ack) {
    if ((ack["cack"] | 0) != 1) return;
    uint16_t cid = ack["cid"] | 0;
    String mac = ack["m"] | ""; mac.replace(":", ""); mac.toUpperCase();
    for (auto& pending : pendingNodeCommands)
        if (pending.cid == cid && pending.mac == mac) pending = PendingNodeCommand();
}
void serviceImmediateLoRaRx(bool deferAckTx) {
    if (g_commandTxActive) {
        if (g_loraPacketReceived || (uint32_t)(millis() - g_commandTxStartedMs) > g_commandTxTimeoutMs) {
            loraRadio.finishTransmit(); g_loraPacketReceived = false; g_commandTxActive = false;
            loraRadio.startReceive();
        }
        return;
    }
    if (!g_loraPacketReceived) { serviceCommandRetries(); return; }
    g_loraPacketReceived = false;
    g_lastLoraRxActivityMs = millis();

    String incomingPayload;
    size_t packetSize = loraRadio.getPacketLength();
    uint8_t bytes[256] = {};
    int state = packetSize && packetSize <= 255 ? loraRadio.readData(bytes, packetSize) : RADIOLIB_ERR_PACKET_TOO_LONG;
    if (state == RADIOLIB_ERR_NONE && !telemetrySampleJson(bytes, packetSize, incomingPayload))
        incomingPayload = String((const char*)bytes);

    if (state == RADIOLIB_ERR_NONE && incomingPayload.length() > 0) {
        g_lastRssi = loraRadio.getRSSI();
        g_lastSnr = loraRadio.getSNR();
        Serial.printf("[LoRa RX] <<< Packet received (%d bytes) [RSSI: %.1f dBm, SNR: %.1f dB]:\n%s\n",
                      (int)incomingPayload.length(), g_lastRssi, g_lastSnr, incomingPayload.c_str());

        StaticJsonDocument<1024> doc;
        DeserializationError err = deserializeJson(doc, incomingPayload);
        if (err == DeserializationError::Ok && doc.containsKey("m")) {
            // Ignore overheard Gateway ACK frames (which have "ack":1 and no telemetry/node keys),
            // but ALWAYS forward node Command ACK frames ("cack" or "cid" or telemetry "p"/"md")
            if (doc.containsKey("ack") && !doc.containsKey("p") && !doc.containsKey("cack") &&
                !doc.containsKey("cid") && !doc.containsKey("md")) {
                loraRadio.startReceive();
                return;
            }

            acknowledgeNodeCommand(doc.as<JsonObject>());
            String nodeMac = doc["m"].as<String>();
            String routeMac = compactHexMacGateway(nodeMac);
            if (routeMac.length() == 12) g_nodeRoutes.heard(strtoull(routeMac.c_str(), nullptr, 16), millis());

            // Inject Gateway reception RSSI ("gr") if not present so API knows link quality
            if (!doc.containsKey("gr")) {
                doc["gr"] = (int)g_lastRssi;
            }
            String enrichedPayload;
            serializeJson(doc, enrichedPayload);

            // 1. Push into multi-node FIFO queue immediately
            enqueueUplinkPacket(enrichedPayload);

            // 2. If inside a critical SIMCom UART wait loop, defer the LoRa ACK TX
            //    so we never drop or overflow the modem's UART response!
            if ((doc["seq"] | (uint64_t)0) != 0) {
                // The correlated storage receipt replaces the old optimistic ACK.
            } else if (deferAckTx) {
                g_pendingAckMac = nodeMac;
                g_pendingAckRssi = (int)g_lastRssi;
            } else {
                int upFlag = isGatewayCloudUp() ? 1 : 0;
                String ackCmd = "{\"m\":\"" + nodeMac + "\",\"ack\":1,\"up\":" + String(upFlag) + ",\"r\":" + String((int)g_lastRssi) + "}";
                transmitLoraDownlink(ackCmd);
            }
        } else {
            Serial.println(F("[LoRa] Non-JSON or foreign packet discarded."));
        }
    } else if (state != RADIOLIB_ERR_NONE) {
        Serial.printf("[LoRa] CRC / Receive error: %d\n", state);
    }

    loraRadio.startReceive();
}

// ------------------------------------------------------------------------------
// 8. HARDWARE, LORA & CELLULAR INITIALIZATION
// ------------------------------------------------------------------------------
void initHardwarePins() {
    pinMode(BAT_PIN, INPUT);
    pinMode(IO_GSM_RST, OUTPUT);
    pinMode(IO_GSM_PWRKEY, OUTPUT);
    pinMode(POWER_3V3_PIN, OUTPUT);

    digitalWrite(POWER_3V3_PIN, HIGH);
    hardwareResetModem();
}

void hardwareResetModem() {
    Serial.println(F("[4G] Executing hardware reset on SIMCom A7670E modem..."));
    g_httpSessionOpen = false;
    g_modemNetworkOk = false;
    g_tlsConfigured = false;
    g_supportedModem = false;

    digitalWrite(IO_GSM_RST, HIGH);
    delay(400);
    digitalWrite(IO_GSM_RST, LOW);
    delay(400);

    digitalWrite(IO_GSM_PWRKEY, HIGH);
    delay(1000);
    digitalWrite(IO_GSM_PWRKEY, LOW);
    for (int i = 0; i < 6; i++) {
        feedGatewayWatchdog();
        serviceImmediateLoRaRx(false);
        delay(500);
    }
}

bool initLoRaRadio() {
    Serial.println(F("[LoRa] Initializing SX1276 SPI Transceiver..."));
    SPI.begin(LORA_SCK, LORA_MISO, LORA_MOSI);

    int state = loraRadio.begin(
        LORA_FREQUENCY, LORA_BANDWIDTH, LORA_SPREAD_FACTOR, LORA_CODING_RATE,
        LORA_SYNC_WORD, LORA_OUTPUT_POWER, LORA_PREAMBLE_LEN);

    if (state != RADIOLIB_ERR_NONE) {
        Serial.printf("[LoRa] begin() failed with code %d\n", state);
        return false;
    }

    loraRadio.setDio0Action(onLoraRxInterrupt, RISING);
    loraRadio.startReceive();
    Serial.printf("[LoRa] [SUCCESS] SX1276 listening on %.1f MHz (SF%d, BW %.1fkHz, CR 4/%d, Sync 0x%02X)\n",
                  LORA_FREQUENCY, LORA_SPREAD_FACTOR, LORA_BANDWIDTH, LORA_CODING_RATE, LORA_SYNC_WORD);
    return true;
}

bool ensurePdpContextActive() {
    feedGatewayWatchdog();
    if (!g_supportedModem || !sendATCommand("AT+CPIN?", "READY", 3000) ||
        !gatewayRegistered(queryModem("AT+CEREG?").c_str())) {
        g_modemNetworkOk = false;
        g_httpSessionOpen = false;
        return false;
    }
    String context = queryModem("AT+CGACT?");
    if (context.indexOf("+CGACT: 1,1") < 0 && context.indexOf("+CGACT: 1, 1") < 0) {
        String apnCmd = String("AT+CGDCONT=1,\"IP\",\"") + CELLULAR_APN + "\"";
        if (!sendATCommand(apnCmd, "OK", 2000) ||
            !sendATCommand("AT+CGATT=1", "OK", 10000) ||
            !sendATCommand("AT+CGACT=1,1", "OK", 10000)) {
            g_modemNetworkOk = false;
            return false;
        }
    }
    g_modemNetworkOk = true;
    return true;
}

bool configureGatewayTls() {
    if (g_tlsConfigured) return true;
    if (!g_supportedModem) return false;
    String certs = queryModem("AT+CCERTLIST", 5000);
    String expectedFile = String("\"") + GATEWAY_CA_FILE + "\"";
    if (certs.indexOf(expectedFile) < 0) {
        String download = String("AT+CCERTDOWN=\"") + GATEWAY_CA_FILE + "\"," + strlen(GATEWAY_ROOT_CA);
        if (!sendATCommand(download, ">", 5000)) return false;
        SerialModem.write((const uint8_t*)GATEWAY_ROOT_CA, strlen(GATEWAY_ROOT_CA));
        if (modemResponse("\r\nOK\r\n", 10000).indexOf("\r\nOK\r\n") < 0) return false;
    }
    // Keep validity-date checks enabled. An unset clock must not bypass TLS checks.
    String clock = queryModem("AT+CCLK?");
    Serial.printf("[TLS] Modem clock: %s\n", clock.c_str());
    const char* clockOption = g_isSim7600 ? "AT+CSSLCFG=\"ignorlocaltime\",1,0"
                                        : "AT+CSSLCFG=\"ignorelocaltime\",1,0";
    String caCommand = String("AT+CSSLCFG=\"cacert\",1,\"") + GATEWAY_CA_FILE + "\"";
    g_tlsConfigured = sendATCommand("AT+CSSLCFG=\"sslversion\",1,3", "OK", 2000) &&
        sendATCommand(caCommand, "OK", 2000) &&
        sendATCommand("AT+CSSLCFG=\"authmode\",1,1", "OK", 2000) &&
        sendATCommand(clockOption, "OK", 2000) &&
        sendATCommand("AT+CSSLCFG=\"enableSNI\",1,1", "OK", 2000);
    if (!g_tlsConfigured) Serial.println(F("[TLS] Secure setup failed; HTTPS stays disabled."));
    return g_tlsConfigured;
}

bool ensureHttpSessionOpen() {
    const char* deviceKey = SMARTHYDRO_DEVICE_KEY;
    if (strlen(deviceKey) != 64 || strspn(deviceKey, "0123456789abcdefABCDEF") != 64) {
        static bool reported = false;
        if (!reported) Serial.println(F("[Security] Configure a 64-character hexadecimal device key in secrets.h. HTTPS uploads are disabled."));
        reported = true;
        return false;
    }
    if (g_httpSessionOpen) {
        return true;
    }
    if (!configureGatewayTls()) return false;
    sendATCommand("AT+HTTPTERM", "OK", 400);
    if (!sendATCommand("AT+HTTPINIT", "OK", 2000)) {
        ensurePdpContextActive();
        if (!sendATCommand("AT+HTTPINIT", "OK", 2000)) {
            g_httpSessionOpen = false;
            return false;
        }
    }
    if (!sendATCommand("AT+HTTPPARA=\"SSLCFG\",1", "OK", 600) ||
        !sendATCommand("AT+HTTPPARA=\"CONNECTTO\",20", "OK", 500) ||
        !sendATCommand("AT+HTTPPARA=\"RECVTO\",8", "OK", 500) ||
        !sendATCommand(String("AT+HTTPPARA=\"USERDATA\",\"X-SmartHydro-Device-Key: ") + SMARTHYDRO_DEVICE_KEY + "\"", "OK", 600)) {
        sendATCommand("AT+HTTPTERM", "OK", 500);
        return false;
    }
    g_httpSessionOpen = true;
    return true;
}

void closeHttpSession() {
    if (g_httpSessionOpen) {
        sendATCommand("AT+HTTPTERM", "OK", 500);
        g_httpSessionOpen = false;
    }
}

void registerHttpOutcome(bool success, int httpStatus) {
    if (success) {
        g_recovery.connected();
        g_consecutiveHttpFailures = 0;
        g_modemNetworkOk = true;
        g_lastCloudSuccessMs = millis();
        return;
    }

    // A server refusal/outage is not a reason to detach a healthy SIM.
    bool transportFailure = httpStatus == 0 || httpStatus >= 600;
    if (transportFailure) g_recovery.failed(millis());
    else g_recovery.serverUnavailable(millis());
    g_consecutiveHttpFailures = g_recovery.failures;
    if (transportFailure) closeHttpSession();
    Serial.printf("[4G Health] HTTP transaction failed (status=%d, consecutive failures=%d)\n",
                  httpStatus, g_consecutiveHttpFailures);

    if (transportFailure && g_consecutiveHttpFailures >= 2) {
        g_modemNetworkOk = false;
    }
    if (transportFailure && g_recovery.shouldReset(millis())) {
        g_recovery.resetStarted(millis());
        hardwareResetModem();
        initCellularModem();
    }
}

bool initCellularModem() {
    SerialModem.begin(115200, SERIAL_8N1, IO_RXD2, IO_TXD2);
    SerialModem.setTimeout(100);
    delay(500);

    for (int i = 0; i < 3; i++) {
        feedGatewayWatchdog();
        if (sendATCommand("AT", "OK", 1500)) break;
        delay(500);
    }

    sendATCommand("ATE0", "OK", 1000);
    sendATCommand("AT+CMEE=2", "OK", 1000);
    String model = queryModem("AT+CGMM");
    Serial.printf("[4G] Installed modem: %s\n", model.c_str());
    g_isSim7600 = model.indexOf("SIM7600") >= 0;
    g_supportedModem = g_isSim7600 || model.indexOf("A7670") >= 0 || model.indexOf("SIM7670") >= 0;
    if (!g_supportedModem || !sendATCommand("AT+CPIN?", "READY", 3000)) {
        Serial.println(F("[4G] SIM not ready or modem model unsupported; not starting HTTPS."));
        g_modemNetworkOk = false;
        return false;
    }
    sendATCommand("AT+CTZU=1", "OK", 2000);
    sendATCommand("AT+CMGF=1", "OK", 1000);
    sendATCommand("AT+CSCS=\"GSM\"", "OK", 1000);

    unsigned long startMs = millis();
    while (millis() - startMs < 18000) {
        feedGatewayWatchdog();
        serviceImmediateLoRaRx(false);
        if (ensurePdpContextActive()) {
            break;
        }
        delay(1200);
        Serial.print(F("."));
    }
    Serial.println();

    if (g_modemNetworkOk) {
        while (SerialModem.available()) SerialModem.read();
        SerialModem.println(F("AT+CGPADDR=1"));
        delay(400);
        String ipResp = SerialModem.readString();
        ipResp.trim();
        Serial.printf("[4G] Carrier IP: %s\n", ipResp.c_str());
    }

    return g_modemNetworkOk;
}

// ------------------------------------------------------------------------------
// 9. MODEM HTTP RESPONSE READER & DOWNLINK COMMAND DISPATCHER
// ------------------------------------------------------------------------------
#include "ModemHttpRead.h"
String readModemHttpResponseBody(int contentLen) {
    if (contentLen <= 0 || contentLen > 4096) {
        Serial.printf("[Downlink] Unsupported HTTP body length: %d bytes. Command was not dispatched.\n", contentLen);
        return "";
    }

    while (SerialModem.available()) SerialModem.read();
    char cmdBuf[40];
    snprintf(cmdBuf, sizeof(cmdBuf), "AT+HTTPREAD=0,%d", contentLen);
    SerialModem.println(cmdBuf);

    unsigned long start = millis();
    SHHttpReadParser reader(contentLen);
    while (millis() - start < 8000 && !reader.done() && !reader.failed()) {
        feedGatewayWatchdog();
        serviceImmediateLoRaRx(true);
        while (SerialModem.available()) {
            reader.feed((char)SerialModem.read());
        }
        delay(5);
    }

    if (!reader.done()) {
        Serial.printf("[Downlink] HTTP body read %s (%u/%d bytes). Waiting for API retry.\n",
            reader.failed() ? "failed" : "timed out", (unsigned)reader.body().size(), contentLen);
        return "";
    }
    Serial.printf("[Downlink] HTTP response body received: %d bytes.\n", contentLen);
    return String(reader.body().c_str());
}

static String compactHexMacGateway(const String &mac) {
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

// Parses response JSON from /api/telemetry/lora or /api/telemetry/lora/poll
// and radiates any pending downlink commands over 868.1 MHz LoRa to the edge node
void processCloudDownlinkResponse(const String &httpBody, const String &defaultMac) {
    if (httpBody.length() < 2) return;

    StaticJsonDocument<4096> doc;
    DeserializationError err = deserializeJson(doc, httpBody);
    if (err != DeserializationError::Ok) {
        Serial.printf("[Downlink] Invalid command response: %s. Waiting for API retry.\n", err.c_str());
        return;
    }

    uint32_t utc = doc["utc"] | 0UL;
    if (utc >= 1704067200UL) {
        struct timeval clock = { (time_t)utc, 0 }; settimeofday(&clock, nullptr);
    }

    // Flush any deferred ACK before radiating downlink command
    flushPendingLoRaAck();

    // Case 1: Response from /api/telemetry/lora/poll containing "commands": [ { "m": "...", ... } ]
    if (doc.containsKey("commands") && doc["commands"].is<JsonArray>()) {
        JsonArray arr = doc["commands"].as<JsonArray>();
        for (JsonObject cmdObj : arr) {
            if (cmdObj.containsKey("m")) {
                cmdObj["m"] = compactHexMacGateway(cmdObj["m"].as<String>());
            }
            String outCmd;
            serializeJson(cmdObj, outCmd);
            if (outCmd.length() > 2) {
                Serial.printf("[Cloud Poll] <<< Pending MVC Command for %s (%d bytes) -> Radiating over LoRa!\n",
                              (cmdObj["m"] | "NODE"), (int)outCmd.length());
                stageNodeCommand(outCmd);
                delay(120); // Brief inter-packet gap if multiple nodes have commands
            }
        }
        return;
    }

    // Case 2: Response from POST /api/telemetry/lora containing "hasPendingCommand": true or direct command object
    bool hasPending = doc["hasPendingCommand"] | false;
    JsonObject cmdTarget;
    if (hasPending && doc.containsKey("sync") && doc["sync"].is<JsonObject>()) {
        cmdTarget = doc["sync"].as<JsonObject>();
    } else if (hasPending && doc.containsKey("command") && doc["command"].is<JsonObject>()) {
        cmdTarget = doc["command"].as<JsonObject>();
    } else if (doc.containsKey("m") && (doc.containsKey("fc") || doc.containsKey("fe") || doc.containsKey("gl") ||
                                       doc.containsKey("w") || doc.containsKey("s") || doc.containsKey("dt") ||
                                       doc.containsKey("pu") || doc.containsKey("pd") || doc.containsKey("nu") ||
                                       doc.containsKey("nd") || doc.containsKey("mode") || doc.containsKey("operatingMode") ||
                                       doc.containsKey("p0") || doc.containsKey("minPh"))) {
        cmdTarget = doc.as<JsonObject>();
    }

    if (!cmdTarget.isNull()) {
        if (cmdTarget.containsKey("m")) {
            cmdTarget["m"] = compactHexMacGateway(cmdTarget["m"].as<String>());
        } else if (defaultMac.length() > 0) {
            cmdTarget["m"] = compactHexMacGateway(defaultMac);
        }
        String outCmd;
        serializeJson(cmdTarget, outCmd);
        if (outCmd.length() > 2) {
            Serial.printf("[Cloud Sync] <<< Downlink directive in POST response (%d bytes) -> Radiating over LoRa: %s\n",
                          (int)outCmd.length(), outCmd.c_str());
            stageNodeCommand(outCmd);
        }
    }
}

// ------------------------------------------------------------------------------
// 10. PERSISTENT KEEP-ALIVE HTTPS POST & LONG-POLL VIA SIMCOM A7670E AT COMMANDS
// ------------------------------------------------------------------------------
String prepareGatewayPayload(const String& payload) {
    StaticJsonDocument<1024> doc;
    if (deserializeJson(doc, payload) != DeserializationError::Ok || (doc["cack"] | 0) == 1) return payload;
    if (!doc["ts"].is<int64_t>()) {
        uint32_t boot = doc["gb"] | 0UL;
        if (boot && boot == g_gatewayBootId && doc["gq"].is<uint64_t>()) {
            uint64_t queuedAt = doc["gq"].as<uint64_t>();
            uint64_t now = (uint64_t)(esp_timer_get_time() / 1000);
            doc["age"] = (doc["age"] | 0UL) + (now >= queuedAt ? (now - queuedAt) / 1000 : 0);
        } else doc["tu"] = true;
    }
    doc.remove("gq"); doc.remove("gb");
    String wire; serializeJson(doc, wire); return wire;
}

void queueTelemetryReceipt(const String& payload, bool stored) {
    StaticJsonDocument<1024> doc;
    if (deserializeJson(doc, payload) != DeserializationError::Ok) return;
    uint64_t seq = doc["seq"] | (uint64_t)0;
    if (!seq) return;
    String mac = compactHexMacGateway(doc["m"] | "");
    if (mac.length() != 12) return;
    if (stored) g_storedCaptures.remember(strtoull(mac.c_str(), nullptr, 16), seq, millis());
    TelemetryReceiptFrame receipt;
    receipt.seq = seq; receipt.stored = stored ? 1 : 0;
    receipt.utc = time(nullptr) >= 1704067200 ? (uint32_t)time(nullptr) : 0;
    receipt.rssi = (int8_t)(doc["gr"] | -127);
    for (int i = 0; i < 6; ++i) receipt.mac[i] = strtoul(mac.substring(i * 2, i * 2 + 2).c_str(), nullptr, 16);
    receipt.crc = telemetryReceiptCrc((const uint8_t*)&receipt, sizeof(receipt) - 1);
    for (auto& pending : g_telemetryReceipts) {
        if (pending.seq == receipt.seq && memcmp(pending.mac, receipt.mac, 6) == 0) { pending = receipt; return; }
    }
    if (g_telemetryReceipts.size() >= 16) g_telemetryReceipts.erase(g_telemetryReceipts.begin());
    g_telemetryReceipts.push_back(receipt);
}

void flushTelemetryReceipt() {
    if (g_commandTxActive || g_loraPacketReceived || g_telemetryReceipts.empty()) return;
    auto& receipt = g_telemetryReceipts.front();
    uint16_t toaMs = (uint16_t)((loraRadio.getTimeOnAir(sizeof(receipt)) + 999UL) / 1000UL);
    if (!g_airtimeLedger.canTransmit(toaMs, SH_PRIO_CRITICAL_ACK)) return;
    int state = loraRadio.transmit((uint8_t*)&receipt, sizeof(receipt));
    loraRadio.startReceive(); g_loraPacketReceived = false;
    if (state == RADIOLIB_ERR_NONE) {
        g_airtimeLedger.recordTransmission(toaMs);
        g_telemetryReceipts.erase(g_telemetryReceipts.begin());
    }
}

void flushTowerCommandReceipt() {
    if (g_commandTxActive || g_loraPacketReceived || g_towerCommandReceipts.empty()) return;
    if (transmitLoraDownlink(g_towerCommandReceipts.front())) g_towerCommandReceipts.erase(g_towerCommandReceipts.begin());
}

bool forwardPayloadToCloud(const String &jsonPayload) {
    String wirePayload = prepareGatewayPayload(jsonPayload);
    feedGatewayWatchdog();
    if (!g_recovery.ready(millis())) return false;
    if (!g_modemNetworkOk && !ensurePdpContextActive()) {
        return false;
    }

    // Extract node MAC from payload for downlink correlation
    String nodeMac = "";
    {
        StaticJsonDocument<512> tmp;
        if (deserializeJson(tmp, jsonPayload) == DeserializationError::Ok && tmp.containsKey("m")) {
            nodeMac = tmp["m"].as<String>();
        }
    }

    if (!ensureHttpSessionOpen()) {
        registerHttpOutcome(false, 0);
        return false;
    }

    String urlCmd = String("AT+HTTPPARA=\"URL\",\"") + CLOUD_API_URL + "\"";
    if (!sendATCommand(urlCmd, "OK", 1500)) {
        closeHttpSession();
        if (!ensureHttpSessionOpen() || !sendATCommand(urlCmd, "OK", 1500)) {
            registerHttpOutcome(false, 0);
            return false;
        }
    }

    if (!sendATCommand("AT+HTTPPARA=\"CONTENT\",\"application/json\"", "OK", 600)) {
        registerHttpOutcome(false, 0);
        return false;
    }

    while (SerialModem.available()) SerialModem.read();
    String dataCmd = String("AT+HTTPDATA=") + wirePayload.length() + ",6000";
    SerialModem.println(dataCmd);

    unsigned long waitStart = millis();
    SHHttpUploadPrompt prompt;
    while (millis() - waitStart < 3500 && !prompt.done() && !prompt.failed()) {
        feedGatewayWatchdog();
        serviceImmediateLoRaRx(true);
        while (SerialModem.available() && !prompt.done() && !prompt.failed())
            prompt.feed((char)SerialModem.read());
        if (!prompt.done()) delay(1);
    }

    if (!prompt.done()) {
        Serial.println(F("[4G] No DOWNLOAD prompt for AT+HTTPDATA."));
        registerHttpOutcome(false, 0);
        return false;
    }

    SerialModem.print(wirePayload);
    g_jsonUplinkBytes += wirePayload.length(); ++g_postCount;
    if (modemResponse("\r\nOK\r\n", 6500).indexOf("\r\nOK\r\n") < 0) {
        registerHttpOutcome(false, 0);
        return false;
    }

    while (SerialModem.available()) SerialModem.read();
    SerialModem.println(F("AT+HTTPACTION=1"));

    unsigned long postStart = millis();
    bool postSuccess = false;
    int httpStatus = 0;
    int responseLen = 0;

    while (millis() - postStart < 30000UL) {
        feedGatewayWatchdog();
        serviceImmediateLoRaRx(true); // Queue LoRa FIFO without blocking UART!
        if (SerialModem.available()) {
            String line = SerialModem.readStringUntil('\n');
            line.trim();
            if (line.startsWith("+HTTPACTION:")) {
                Serial.printf("[4G] %s\n", line.c_str());
                // Format: +HTTPACTION: 1,<status>,<len>
                int firstComma = line.indexOf(',');
                int secondComma = line.indexOf(',', firstComma + 1);
                if (firstComma != -1 && secondComma != -1) {
                    httpStatus = line.substring(firstComma + 1, secondComma).toInt();
                    responseLen = line.substring(secondComma + 1).toInt();
                }
                if (httpStatus == 200 || httpStatus == 201) {
                    postSuccess = true;
                }
                break;
            }
        }
        delay(5);
    }

    // Read response body via AT+HTTPREAD to check for piggybacked downlink commands
    String respBody;
    if (postSuccess && responseLen > 0) {
        respBody = readModemHttpResponseBody(responseLen);
        if (respBody.length() > 0) {
            processCloudDownlinkResponse(respBody, nodeMac);
        }
    }

    StaticJsonDocument<64> receiptFilter; receiptFilter["success"] = true; receiptFilter["seq"] = true;
    StaticJsonDocument<128> storageReply;
    postSuccess = postSuccess && deserializeJson(storageReply, respBody, DeserializationOption::Filter(receiptFilter)) == DeserializationError::Ok
        && (storageReply["success"] | false);
    StaticJsonDocument<128> sentPacket;
    if (deserializeJson(sentPacket, jsonPayload, DeserializationOption::Filter(receiptFilter)) == DeserializationError::Ok && sentPacket["seq"].is<uint64_t>())
        postSuccess = postSuccess && storageReply["seq"].is<uint64_t>()
            && storageReply["seq"].as<uint64_t>() == sentPacket["seq"].as<uint64_t>();

    StaticJsonDocument<512> commandReply;
    if (deserializeJson(commandReply, jsonPayload) == DeserializationError::Ok &&
        (commandReply["cack"] | 0) == 1 && (commandReply["ar"] | 0) == 1) {
        bool accepted = postSuccess;
        if (accepted) {
            StaticJsonDocument<192> receipt;
            receipt["m"] = commandReply["m"]; receipt["ack"] = 1;
            receipt["ac"] = commandReply["cid"];
            String phase = commandReply["phase"] | "accepted";
            receipt["ap"] = phase == "completed" ? 1 : phase == "cancelled" ? 2 : 0;
            String wire; serializeJson(receipt, wire);
            bool queued = false;
            for (const auto& pending : g_towerCommandReceipts) if (pending == wire) queued = true;
            if (!queued && g_towerCommandReceipts.size() < 16) g_towerCommandReceipts.push_back(wire);
            Serial.printf("[Command] API accepted CID %u (%s); returning receipt.\n", commandReply["cid"] | 0, phase.c_str());
        }
        // A 200 with success:false is not confirmation of the controller's ACK.
        postSuccess = accepted;
    }

    g_lastHttpDurationMs = millis() - postStart;
    Serial.printf("[Latency] POST response %lu ms, HTTP %d\n", g_lastHttpDurationMs, httpStatus);
    g_jsonDownlinkBytes += responseLen;
    registerHttpOutcome(postSuccess, httpStatus);
    queueTelemetryReceipt(jsonPayload, postSuccess);
    flushTelemetryReceipt();
    return postSuccess;
}

// Persistent Keep-Alive Long-Poll to /api/telemetry/lora/poll?waitMs=4000
void pollCloudDownlinkCommands() {
    std::string nodes = g_nodeRoutes.nodes(millis());
    if (nodes.empty()) return; // Wait for a local node; never claim another gateway's commands.
    feedGatewayWatchdog();
    if (!ensureHttpSessionOpen()) {
        registerHttpOutcome(false, 0);
        return;
    }

    String pollUrl = CLOUD_POLL_URL;
    pollUrl += "&macs="; pollUrl += nodes.c_str();
    // Check for new commands between uploads without holding up queued ACKs.
    if (!g_uplinkQueue.empty()) pollUrl.replace("waitMs=4000", "waitMs=0");
    String urlCmd = String("AT+HTTPPARA=\"URL\",\"") + pollUrl + "\"";
    if (!sendATCommand(urlCmd, "OK", 1000)) {
        registerHttpOutcome(false, 0);
        return;
    }

    while (SerialModem.available()) SerialModem.read();
    ++g_pollCount;
    SerialModem.println(F("AT+HTTPACTION=0")); // 0 = HTTP GET

    unsigned long getStart = millis();
    int httpStatus = 0;
    int responseLen = 0;

    while (millis() - getStart < 30000UL) {
        feedGatewayWatchdog();
        serviceImmediateLoRaRx(true); // Keep LoRa RX FIFO responsive during long-poll
        if (SerialModem.available()) {
            String line = SerialModem.readStringUntil('\n');
            line.trim();
            if (line.startsWith("+HTTPACTION:")) {
                int firstComma = line.indexOf(',');
                int secondComma = line.indexOf(',', firstComma + 1);
                if (firstComma != -1 && secondComma != -1) {
                    httpStatus = line.substring(firstComma + 1, secondComma).toInt();
                    responseLen = line.substring(secondComma + 1).toInt();
                }
                break;
            }
        }
        delay(5);
    }

    g_jsonDownlinkBytes += responseLen;
    g_lastHttpDurationMs = millis() - getStart;
    Serial.printf("[Latency] Poll response %lu ms, HTTP %d (includes server wait)\n", g_lastHttpDurationMs, httpStatus);
    if (httpStatus == 200 || httpStatus == 204) {
        registerHttpOutcome(true, httpStatus);
        if (httpStatus == 200 && responseLen > 2) {
            String respBody = readModemHttpResponseBody(responseLen);
            if (respBody.length() > 0) {
                processCloudDownlinkResponse(respBody, "");
            }
        }
    } else {
        registerHttpOutcome(false, httpStatus);
    }
}

// ------------------------------------------------------------------------------
// 11. LITTLEFS OFFLINE STORE-AND-FORWARD BUFFER
// ------------------------------------------------------------------------------
// Stable capture identities avoid extra flash files when a node repeats an uplink.
static String gatewaySpoolIdentity(const String& payload) {
    StaticJsonDocument<2048> packet;
    if (deserializeJson(packet, payload) != DeserializationError::Ok) return "";
    String mac = compactHexMacGateway(packet["m"] | "");
    if (mac.length() != 12) return "";
    if ((packet["cack"] | 0) == 1)
        return mac + ":ack:" + String(packet["cid"] | 0UL) + ":" + String(packet["phase"] | "accepted")
            + ":" + String((unsigned long long)(packet["rs"] | (uint64_t)0));
    if (packet["seq"].is<uint64_t>() && packet["seq"].as<uint64_t>())
        return mac + ":seq:" + String((unsigned long long)packet["seq"].as<uint64_t>());
    return mac + ":legacy:" + payload;
}

String gatewaySpoolPath(const String& payload) {
    String identity = gatewaySpoolIdentity(payload);
    uint64_t hash = 14695981039346656037ULL;
    for (size_t i = 0; i < identity.length(); ++i) hash = (hash ^ (uint8_t)identity[i]) * 1099511628211ULL;
    char path[40]; snprintf(path, sizeof(path), "/up_%016llx.rec", (unsigned long long)hash);
    return String(path);
}

static bool readGatewaySpool(const String& path, String& payload) {
    File file = LittleFS.open(path, FILE_READ);
    if (!file || file.size() < 10 || file.size() > MAX_DURABLE_PACKET_BYTES + 9) { file.close(); return false; }
    String checksum = file.readStringUntil('\n');
    payload = file.readString(); file.close();
    return checksum.length() == 8 && gatewaySpoolIdentity(payload).length()
        && strtoul(checksum.c_str(), nullptr, 16) == shSpoolChecksum(payload.c_str(), payload.length());
}

bool bufferPayloadToFlash(const String &jsonPayload) {
    if (!jsonPayload.length() || jsonPayload.length() > MAX_DURABLE_PACKET_BYTES || !gatewaySpoolIdentity(jsonPayload).length()) return false;
    String path = gatewaySpoolPath(jsonPayload), existing;
    if (LittleFS.exists(path)) {
        if (readGatewaySpool(path, existing) && gatewaySpoolIdentity(existing) == gatewaySpoolIdentity(jsonPayload)) return true;
        Serial.println(F("[Flash] Damaged record or identity collision; preserving it for investigation."));
        return false;
    }
    File root = LittleFS.open("/");
    if (!root) return false;
    size_t count = 0;
    for (File file = root.openNextFile(); file; file = root.openNextFile()) {
        String name = file.name(); if (name.startsWith("up_") && name.endsWith(".rec")) ++count;
        file.close();
    }
    root.close();
    size_t limit = isCommandAcknowledgement(jsonPayload) ? MAX_DURABLE_RECORDS : MAX_DURABLE_RECORDS - 16;
    if (count >= limit) { Serial.println(F("[Flash] Durable queue capacity reached; 16 slots are reserved for command receipts. Controller must retry.")); return false; }
    char checksum[10]; snprintf(checksum, sizeof(checksum), "%08lx\n", (unsigned long)shSpoolChecksum(jsonPayload.c_str(), jsonPayload.length()));
    String record = String(checksum) + jsonPayload;
    bool saved = shCommitGatewayRecord(LittleFS, path.c_str(), "/uplink.tmp", record.c_str(), record.length());
    if (!saved) Serial.println(F("[Flash] Write/read-back/rename failed; packet was not accepted."));
    return saved;
}

void removeStoredGatewayPacket(const String& payload) {
    String path = gatewaySpoolPath(payload);
    if (LittleFS.exists(path) && !LittleFS.remove(path))
        Serial.println(F("[Flash] Could not retire confirmed packet; it may replay safely."));
}

void flushBufferedQueue() {
    // Move one legacy record per turn. Even a backlog larger than the new queue
    // can make progress without repeatedly importing its first 256 entries.
    if (LittleFS.exists(BUFFER_FILE_PATH)) {
        File legacy = LittleFS.open(BUFFER_FILE_PATH, FILE_READ);
        if (legacy && !legacy.size()) {
            legacy.close();
            if (!LittleFS.remove(BUFFER_FILE_PATH)) Serial.println(F("[Flash] Empty legacy backlog could not be retired."));
        } else if (legacy) {
            String line = legacy.readStringUntil('\n'); line.trim();
            size_t consumed = legacy.position(); legacy.close();
            if ((!line.length() || bufferPayloadToFlash(line)) &&
                !shRetireGatewayPrefix(LittleFS, BUFFER_FILE_PATH, "/legacy.tmp", consumed, feedGatewayWatchdog))
                Serial.println(F("[Flash] Legacy migration interrupted; original copy retained."));
        }
    }
    static unsigned turn = 0;
    static String cursor;
    ++turn;
    String chosenPath, chosenPayload, wrapPath, wrapPayload;
    int chosenPriority = 3, wrapPriority = 3;
    std::vector<String> ramPaths;
    ramPaths.reserve(g_uplinkQueue.size());
    for (const auto& item : g_uplinkQueue) ramPaths.push_back(gatewaySpoolPath(item));
    File root = LittleFS.open("/");
    if (!root) return;
    for (File file = root.openNextFile(); file; file = root.openNextFile()) {
        String name = file.name(); file.close();
        if (!name.startsWith("up_") || !name.endsWith(".rec")) continue;
        String path = "/" + name, payload;
        if (!readGatewaySpool(path, payload)) { Serial.printf("[Flash] Unreadable record retained: %s\n", path.c_str()); continue; }
        // Skip packets still in the fast RAM path; they already get upload turns.
        bool inRam = false;
        for (const auto& ramPath : ramPaths) if (ramPath == path) { inRam = true; break; }
        if (inRam) continue;
        int priority = isCommandAcknowledgement(payload) ? 0 : 1;
        if (turn % 4 == 0) priority = 1; // History cannot starve behind repeated receipts.
        if (priority < wrapPriority || (priority == wrapPriority && (wrapPath.isEmpty() || path < wrapPath))) {
            wrapPath = path; wrapPayload = payload; wrapPriority = priority;
        }
        if (path > cursor && (priority < chosenPriority || (priority == chosenPriority && (chosenPath.isEmpty() || path < chosenPath)))) {
            chosenPath = path; chosenPayload = payload; chosenPriority = priority;
        }
        feedGatewayWatchdog();
    }
    root.close();
    if (chosenPath.isEmpty() || wrapPriority < chosenPriority) { chosenPath = wrapPath; chosenPayload = wrapPayload; }
    if (chosenPath.isEmpty()) return;
    cursor = chosenPath;
    // Exactly one HTTP attempt per turn, including on failure. New arrivals save
    // separate immutable files while the modem is busy.
    if (forwardPayloadToCloud(chosenPayload)) {
        removeStoredGatewayPacket(chosenPayload);
        ++g_totalUplinksOk;
    }
}

// ------------------------------------------------------------------------------
// 12. MODEM AT HELPER & SMS STATE MACHINE
// ------------------------------------------------------------------------------
String modemResponse(const char* expectedResp, unsigned long timeoutMs) {
    unsigned long start = millis();
    String buffer = "";
    while (millis() - start < timeoutMs) {
        feedGatewayWatchdog();
        serviceImmediateLoRaRx(true);
        while (SerialModem.available()) {
            char c = SerialModem.read();
            buffer += c;
            if (buffer.indexOf(expectedResp) != -1) {
                return buffer;
            }
            if (buffer.indexOf("\r\nERROR\r\n") >= 0 || buffer.indexOf("+CME ERROR:") >= 0) return buffer;
            if (buffer.length() > 4096) return "";
        }
        delay(1);
    }
    return buffer;
}

String queryModem(const String& command, unsigned long timeoutMs) {
    while (SerialModem.available()) SerialModem.read();
    SerialModem.println(command);
    String response = modemResponse("\r\nOK\r\n", timeoutMs);
    return response.indexOf("\r\nOK\r\n") >= 0 ? response : String("");
}

bool sendATCommand(const String &cmd, const char *expectedResp, unsigned long timeoutMs) {
    if (strcmp(expectedResp, "READY") == 0) {
        return gatewaySimReady(queryModem(cmd, timeoutMs).c_str());
    }
    while (SerialModem.available()) SerialModem.read();
    SerialModem.println(cmd);
    const char* terminal = strcmp(expectedResp, "OK") == 0 ? "\r\nOK\r\n" : expectedResp;
    String response = modemResponse(terminal, timeoutMs);
    return response.indexOf(terminal) >= 0 && response.indexOf("ERROR") < 0;
}

void queueSmsNotification(const String &message) {
    if (!g_modemNetworkOk || g_smsRecipient.length() == 0) return;
    if (g_smsState == 0) {
        g_smsBody = message;
        g_smsPending = true;
        g_smsState = 1;
        g_smsStepMs = millis();
    }
}

void processSmsStateMachine() {
    if (!g_smsPending) return;
    unsigned long now = millis();

    switch (g_smsState) {
    case 1:
        while (SerialModem.available()) SerialModem.read();
        SerialModem.println(F("AT+CMGF=1"));
        g_smsState = 2;
        g_smsStepMs = now;
        break;
    case 2:
        if (SerialModem.available()) {
            String resp = SerialModem.readString();
            if (resp.indexOf("OK") != -1) {
                String cmd = "AT+CMGS=\"" + g_smsRecipient + "\"";
                SerialModem.println(cmd);
                g_smsState = 3;
                g_smsStepMs = now;
            }
        }
        if (now - g_smsStepMs > 3000) { g_smsPending = false; g_smsState = 0; }
        break;
    case 3: {
        bool gotPrompt = false;
        while (SerialModem.available()) {
            if (SerialModem.read() == '>') { gotPrompt = true; break; }
        }
        if (gotPrompt) {
            SerialModem.print(g_smsBody);
            SerialModem.write(0x1A);
            g_smsState = 4;
            g_smsStepMs = now;
        } else if (now - g_smsStepMs > 5000) { g_smsPending = false; g_smsState = 0; }
        break;
    }
    case 4:
        if (SerialModem.available()) {
            String resp = SerialModem.readString();
            if (resp.indexOf("+CMGS:") != -1 || resp.indexOf("ERROR") != -1) {
                g_smsPending = false;
                g_smsState = 0;
            }
        }
        if (now - g_smsStepMs > 15000) { g_smsPending = false; g_smsState = 0; }
        break;
    }
}

// ------------------------------------------------------------------------------
// 13. DIAGNOSTICS & LORA DOWNLINK TRANSMITTER
// ------------------------------------------------------------------------------
void printOfflineBuffer() {
    Serial.println(F("\n--- [Durable uplink queue] ---"));
    File root = LittleFS.open("/");
    size_t count = 0;
    for (File file = root.openNextFile(); file; file = root.openNextFile()) {
        String name = file.name(); file.close();
        if (!name.startsWith("up_") || !name.endsWith(".rec")) continue;
        String payload;
        bool readable = readGatewaySpool("/" + name, payload);
        ++count;
        Serial.printf("[%03u] %s: %s\n", (unsigned)count, name.c_str(), readable ? payload.c_str() : "DAMAGED; retained");
        feedGatewayWatchdog();
    }
    root.close();
    Serial.printf("Durable records: %u/%u; legacy backlog: %s\n", (unsigned)count, MAX_DURABLE_RECORDS,
                  LittleFS.exists(BUFFER_FILE_PATH) ? "awaiting import" : "none");
}

void printGatewayStatus() {
    Serial.println(F("\n========== GATEWAY STATUS REPORT =========="));
    Serial.printf("LoRa Radio: %.2f MHz | SF: %d | BW: %.1f kHz | CR: 4/%d | Power: +%d dBm\n",
                  LORA_FREQUENCY, LORA_SPREAD_FACTOR, LORA_BANDWIDTH, LORA_CODING_RATE, LORA_OUTPUT_POWER);
    Serial.printf("Last RSSI: %.1f dBm | Last SNR: %.1f dB\n", g_lastRssi, g_lastSnr);
    Serial.printf("Modem Registered: %s | HTTP Service Initialized: %s | Cloud Up Flag (<=60s): %s\n",
                  g_modemNetworkOk ? "YES (Telkom 4G Active)" : "NO",
                  g_httpSessionOpen ? "YES" : "NO",
                  isGatewayCloudUp() ? "UP (1)" : "DOWN (0)");
    Serial.printf("TLS certificate checks: %s | Last HTTP action: %lu ms | Retry delay: %lu ms\n",
                  g_tlsConfigured ? "CONFIGURED" : "NOT CONFIGURED",
                  (unsigned long)g_lastHttpDurationMs, (unsigned long)g_recovery.retryDelayMs);
    Serial.printf("ICASA 1h Airtime: %lu / %lu ms (%.2f%% duty cycle, %.1f%% of 1%% budget)\n",
                  (unsigned long)g_airtimeLedger.getUsedAirtimeMs(),
                  (unsigned long)ICASA_MAX_AIRTIME_MS_PER_HOUR,
                  g_airtimeLedger.getDutyCyclePercent(),
                  g_airtimeLedger.getBudgetUsedPercent());
    Serial.printf("RAM Queue Depth: %u | Uplinks OK: %lu | Downlinks Sent: %lu\n",
                  (unsigned int)g_uplinkQueue.size(), g_totalUplinksOk, g_totalDownlinksSent);
    Serial.printf("Application JSON only: TX %llu bytes, RX %llu bytes; POSTs %lu, polls %lu (excludes HTTP/TLS/network overhead).\n",
                  (unsigned long long)g_jsonUplinkBytes, (unsigned long long)g_jsonDownlinkBytes,
                  (unsigned long)g_postCount, (unsigned long)g_pollCount);
    Serial.println(F("===========================================\n"));
}

bool transmitLoraDownlink(String cmdJson) {
    if (g_commandTxActive || g_loraPacketReceived) return false;
    feedGatewayWatchdog();
    uint32_t toaUs = (uint32_t)loraRadio.getTimeOnAir(cmdJson.length());
    uint16_t toaMs = (toaUs > 0) ? (uint16_t)((toaUs + 999UL) / 1000UL)
                                 : (uint16_t)shCalculateLoRaToaMs(cmdJson.length(), LORA_SPREAD_FACTOR, LORA_BANDWIDTH, LORA_CODING_RATE, LORA_PREAMBLE_LEN);
    if (!g_airtimeLedger.canTransmit(toaMs, SH_PRIO_CRITICAL_ACK)) {
        Serial.printf("[ICASA Guard] Skipping LoRa TX (%u ms) - 1%% hourly airtime limit (%lu ms) reached!\n",
                      (unsigned int)toaMs, (unsigned long)g_airtimeLedger.getUsedAirtimeMs());
        return false;
    }

    Serial.printf("[LoRa TX] >>> Radiating Downlink (%d bytes, ToA ~%u ms): %s\n",
                  (int)cmdJson.length(), (unsigned int)toaMs, cmdJson.c_str());
    int state = loraRadio.transmit(cmdJson);
    delay(20);
    g_loraPacketReceived = false;
    g_lastLoraRxActivityMs = millis();
    loraRadio.startReceive();
    if (state == RADIOLIB_ERR_NONE) {
        g_airtimeLedger.recordTransmission(toaMs);
        return true;
    } else {
        Serial.printf("[LoRa TX] [FAIL] Error code: %d\n", state);
        return false;
    }
}
