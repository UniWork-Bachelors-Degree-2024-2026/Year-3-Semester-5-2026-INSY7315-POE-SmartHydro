#pragma once
#include <Arduino.h>
#include "Provisioning.h"

#if SH_FODDER_BLE_ENABLED
#include <NimBLEDevice.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include <esp_random.h>
#include "Config.h"

// Forward declaration of command handler implemented in main sketch
bool queueBleCommandPayload(const String& payload);

static NimBLEServer* pBleServer = nullptr;
static NimBLECharacteristic* pBleTelemetryChar = nullptr;
static NimBLECharacteristic* pBleCommandChar = nullptr;
static bool g_bleDeviceConnected = false;
static NimBLECharacteristic* pBleAckChar = nullptr;
static String g_bleDeviceName;
static uint32_t g_blePasskey = 0;
static volatile uint32_t g_blePairingUntilMs = 0;

static bool blePairingVisible() {
    return g_blePairingUntilMs && (int32_t)(g_blePairingUntilMs - millis()) > 0;
}

static void loadBlePasskey() {
    Preferences pairing;
    if (!pairing.begin("sh_ble", false)) {
        Serial.println("[BLE] Cannot open pairing settings; BLE stays disabled.");
        return;
    }
    g_blePasskey = pairing.getUInt("passkey", 0);
    if (g_blePasskey < 100000 || g_blePasskey > 999999) {
        // Rejection sampling avoids a biased six-digit random value.
        uint32_t random;
        do { random = esp_random(); } while (random >= 4294800000UL);
        g_blePasskey = 100000 + random % 900000;
        if (pairing.putUInt("passkey", g_blePasskey) != sizeof(uint32_t)) {
            g_blePasskey = 0;
            Serial.println("[BLE] Cannot save pairing passkey; BLE stays disabled.");
        }
    }
    pairing.end();
}

static void notifyBleCommandAck(uint16_t cid, bool ok, const char* reason, const char* phase) {
    if (!pBleAckChar) return;
    StaticJsonDocument<192> ack;
    ack["cid"] = cid; ack["ok"] = ok;
    ack["reason"] = reason; ack["phase"] = phase;
    String json; serializeJson(ack, json);
    pBleAckChar->setValue(json.c_str());
    if (!pBleServer) return;
    for (uint8_t i = 0; i < pBleServer->getConnectedCount(); ++i) {
        auto peer = pBleServer->getPeerInfo(i);
        if (!peer.isEncrypted() || !peer.isAuthenticated()) continue;
        if (peer.getMTU() < json.length() + 3) {
            Serial.println("[BLE] ACK available by FF03 read; request MTU 185 for notifications.");
            continue;
        }
        if (!pBleAckChar->notify(peer.getConnHandle()))
            Serial.println("[BLE] ACK notification failed; result remains readable on FF03.");
    }
}

// Server connection callbacks
class FodderServerCallbacks : public NimBLEServerCallbacks {
    uint32_t onPassKeyDisplay() override {
        g_blePairingUntilMs = millis() + 60000UL;
        Serial.printf("[BLE Pairing] %s passkey: %06lu (also shown on OLED).\n",
                      g_bleDeviceName.c_str(), (unsigned long)g_blePasskey);
        return g_blePasskey;
    }
    void onAuthenticationComplete(NimBLEConnInfo& connInfo) override {
        g_blePairingUntilMs = 0;
        if (!connInfo.isEncrypted() || !connInfo.isAuthenticated())
            NimBLEDevice::getServer()->disconnect(connInfo.getConnHandle());
    }
    void onConnect(NimBLEServer* pServer, NimBLEConnInfo& connInfo) override {
        g_bleDeviceConnected = true;
        Serial.printf("[BLE] Android client connected to Fodder GATT server (Peer: %s).\n",
                      connInfo.getAddress().toString().c_str());
    }
    void onDisconnect(NimBLEServer* pServer, NimBLEConnInfo& connInfo, int reason) override {
        g_bleDeviceConnected = false;
        g_blePairingUntilMs = 0;
        Serial.printf("[BLE] Android client disconnected (reason=%d). Restarting advertising...\n", reason);
        NimBLEDevice::startAdvertising();
    }
};

// Characteristic write callbacks for manual commands from Android app
class FodderCommandCallbacks : public NimBLECharacteristicCallbacks {
    void onWrite(NimBLECharacteristic* pCharacteristic, NimBLEConnInfo& connInfo) override {
        if (!connInfo.isEncrypted() || !connInfo.isAuthenticated()) return;
        std::string rxVal = pCharacteristic->getValue();
        if (rxVal.empty()) {
            notifyBleCommandAck(0, false, "invalid_command", "accepted");
            return;
        }
        String payload = String(rxVal.c_str());
        if (!queueBleCommandPayload(payload)) {
            StaticJsonDocument<512> command;
            deserializeJson(command, payload);
            uint32_t cid = command["cid"].is<unsigned int>() ? command["cid"].as<uint32_t>() : 0;
            notifyBleCommandAck(cid <= 65535 ? (uint16_t)cid : 0, false,
                                payload.length() >= 512 ? "command_too_large" : "busy", "accepted");
        }
    }
};

inline void initFodderBLE(const String& macAddress) {
    Serial.println(F("[BLE] Initializing NimBLE 5.0 GATT Server for Fodder..."));
    loadBlePasskey();
    if (!g_blePasskey) return;
    String suffix = macAddress; suffix.replace(":", "");
    g_bleDeviceName = String(BLE_DEVICE_NAME) + "-" + suffix.substring(suffix.length() - 6);
    NimBLEDevice::init(g_bleDeviceName.c_str());
    // Use 0 dBm (~22 mA vs 115 mA at +9 dBm) to prevent 3.3V LDO collapse during simultaneous LoRa TX
    NimBLEDevice::setPower(ESP_PWR_LVL_N0);
    NimBLEDevice::setMTU(256);
    NimBLEDevice::setSecurityAuth(true, true, true);
    // Use onPassKeyDisplay: NimBLE skips that callback when a static key is set.
    NimBLEDevice::setSecurityIOCap(BLE_HS_IO_DISPLAY_ONLY);

    pBleServer = NimBLEDevice::createServer();
    pBleServer->setCallbacks(new FodderServerCallbacks());

    NimBLEService* pService = pBleServer->createService(BLE_SERVICE_UUID);

    // Characteristic 0xFF01: Live telemetry & true relay states (Read & Notify)
    pBleTelemetryChar = pService->createCharacteristic(
        BLE_CHAR_TELEMETRY_UUID,
        NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::NOTIFY
    );

    // Characteristic 0xFF02: Command receiver from Android app (Write)
    pBleCommandChar = pService->createCharacteristic(
        BLE_CHAR_COMMAND_UUID,
        NIMBLE_PROPERTY::WRITE | NIMBLE_PROPERTY::WRITE_NR | NIMBLE_PROPERTY::WRITE_ENC | NIMBLE_PROPERTY::WRITE_AUTHEN
    );
    pBleCommandChar->setCallbacks(new FodderCommandCallbacks());

    // Android subscribes here for command outcomes; FF01 stays telemetry-only.
    pBleAckChar = pService->createCharacteristic(
        "0000ff03-0000-1000-8000-00805f9b34fb",
        NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::READ_ENC |
        NIMBLE_PROPERTY::READ_AUTHEN | NIMBLE_PROPERTY::NOTIFY);
    pService->start();

    NimBLEAdvertising* pAdvertising = NimBLEDevice::getAdvertising();
    NimBLEAdvertisementData scanData;
    scanData.setName(g_bleDeviceName.c_str());
    pAdvertising->setScanResponseData(scanData);
    pAdvertising->addServiceUUID(BLE_SERVICE_UUID);
    pAdvertising->setMinInterval(160); // 100 ms
    pAdvertising->setMaxInterval(320); // 200 ms
    pAdvertising->enableScanResponse(true);
    pAdvertising->start();

    Serial.printf("[BLE] [SUCCESS] NimBLE Fodder GATT Service Active & Advertising as '%s'.\n", g_bleDeviceName.c_str());
    Serial.printf("[BLE] MAC: %s | Service UUID: %s\n", macAddress.c_str(), BLE_SERVICE_UUID);
    Serial.printf("[BLE] Telemetry Char (Notify/Read): %s | Command Char (Write): %s\n",
                  BLE_CHAR_TELEMETRY_UUID, BLE_CHAR_COMMAND_UUID);
}

inline void pauseFodderBleForLoraTx() {
    if (!g_bleDeviceConnected) {
        NimBLEAdvertising* pAdvertising = NimBLEDevice::getAdvertising();
        if (pAdvertising != nullptr && pAdvertising->isAdvertising()) {
            pAdvertising->stop();
        }
    }
}

inline void resumeFodderBleAfterLoraTx() {
    if (!g_bleDeviceConnected) {
        NimBLEAdvertising* pAdvertising = NimBLEDevice::getAdvertising();
        if (pAdvertising != nullptr && !pAdvertising->isAdvertising()) {
            pAdvertising->start();
        }
    }
}

inline void notifyFodderBLE(const String& compactJson) {
    if (pBleTelemetryChar != nullptr) {
        pBleTelemetryChar->setValue((uint8_t*)compactJson.c_str(), compactJson.length());
        if (g_bleDeviceConnected) {
            pBleTelemetryChar->notify();
            Serial.printf("[BLE] >>> Telemetry notified: %s\n", compactJson.c_str());
        }
    }
}

inline void ensureFodderBleAdvertising() {
    if (!g_bleDeviceConnected) {
        NimBLEAdvertising* pAdvertising = NimBLEDevice::getAdvertising();
        if (pAdvertising != nullptr && !pAdvertising->isAdvertising()) {
            Serial.println(F("[BLE] Advertising watchdog: restarting advertising..."));
            pAdvertising->start();
        }
    }
}

inline bool isFodderBleConnected() {
    return g_bleDeviceConnected;
}
#else
static String g_bleDeviceName;
static uint32_t g_blePasskey = 0;
static bool blePairingVisible() { return false; }
static void notifyBleCommandAck(uint16_t, bool, const char*, const char*) {}
inline void initFodderBLE(const String&) {
    Serial.println("[BLE] Disabled for the fodder pilot; LoRa and manual SoftAP remain available.");
}
inline void pauseFodderBleForLoraTx() {}
inline void resumeFodderBleAfterLoraTx() {}
inline void notifyFodderBLE(const String&) {}
inline void ensureFodderBleAdvertising() {}
inline bool isFodderBleConnected() { return false; }
#endif
