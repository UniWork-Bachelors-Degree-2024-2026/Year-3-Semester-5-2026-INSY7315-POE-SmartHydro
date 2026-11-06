#pragma once
#include <Arduino.h>
#include <NimBLEDevice.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include <esp_random.h>
#include "Config.h"
#include "RelayReports.h"

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
        if (!shBleNotificationFits(peer.getMTU(), json.length())) {
            Serial.println("[BLE] ACK remains readable on FF03; request MTU 517 for full telemetry and ACK notifications.");
            continue;
        }
        if (!pBleAckChar->notify(peer.getConnHandle()))
            Serial.println("[BLE] ACK notification failed; result remains readable on FF03.");
    }
}

// Server connection callbacks
class TowerServerCallbacks : public NimBLEServerCallbacks {
    uint32_t onPassKeyDisplay() override {
        g_blePairingUntilMs = millis() + 60000UL;
        Serial.printf("[BLE Pairing] %s passkey: %06lu (also shown on OLED).\n",
                      g_bleDeviceName.c_str(), (unsigned long)g_blePasskey);
        return g_blePasskey;
    }
    void onAuthenticationComplete(NimBLEConnInfo& connInfo) override {
        g_blePairingUntilMs = 0;
        if (!connInfo.isEncrypted() || !connInfo.isAuthenticated()) {
            Serial.println("[BLE] Pairing did not authenticate. Disconnecting; try pairing again using the code on the tower.");
            NimBLEDevice::getServer()->disconnect(connInfo.getConnHandle());
        } else {
            Serial.println("[BLE] Secure pairing complete. Authenticated commands and command results are available.");
        }
    }
    void onConnect(NimBLEServer* pServer, NimBLEConnInfo& connInfo) override {
        g_bleDeviceConnected = true;
        Serial.printf("[BLE] Android client connected to Tower GATT server (Peer: %s).\n",
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
class CommandCharCallbacks : public NimBLECharacteristicCallbacks {
    void onWrite(NimBLECharacteristic* pCharacteristic, NimBLEConnInfo& connInfo) override {
        if (!connInfo.isEncrypted() || !connInfo.isAuthenticated()) return;
        std::string rxVal = pCharacteristic->getValue();
        if (rxVal.length() > 0) {
            String payload = String(rxVal.c_str());
            Serial.printf("[BLE] Command received from Android (%u bytes).\n", (unsigned)payload.length());
            if (!queueBleCommandPayload(payload)) {
                StaticJsonDocument<256> command;
                uint16_t cid = 0;
                if (deserializeJson(command, payload) == DeserializationError::Ok && command["cid"].is<unsigned int>()
                    && command["cid"].as<uint32_t>() <= 65535) cid = command["cid"].as<uint16_t>();
                notifyBleCommandAck(cid, false, payload.length() >= 512 ? "command_too_large" : "controller_busy", "accepted");
            }
        }
    }
};

inline void initTowerBLE(const String& macAddress) {
    Serial.println(F("[BLE] Initializing NimBLE 5.0 GATT Server for Tower..."));
    loadBlePasskey();
    if (!g_blePasskey) return;
    String suffix = macAddress; suffix.replace(":", "");
    g_bleDeviceName = String(BLE_DEVICE_NAME) + "-" + suffix.substring(suffix.length() - 6);
    NimBLEDevice::init(g_bleDeviceName.c_str());
    // Use 0 dBm (~22 mA vs 115 mA at +9 dBm) to prevent 3.3V rail dips during relay switching
    NimBLEDevice::setPower(ESP_PWR_LVL_N0);
    NimBLEDevice::setMTU(517);
    NimBLEDevice::setSecurityAuth(true, true, true);
    // Use onPassKeyDisplay: NimBLE skips that callback when a static key is set.
    NimBLEDevice::setSecurityIOCap(BLE_HS_IO_DISPLAY_ONLY);

    pBleServer = NimBLEDevice::createServer();
    pBleServer->setCallbacks(new TowerServerCallbacks());

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
    pBleCommandChar->setCallbacks(new CommandCharCallbacks());

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

    Serial.printf("[BLE] [SUCCESS] NimBLE Tower GATT Service Active & Advertising as '%s'.\n", g_bleDeviceName.c_str());
    Serial.printf("[BLE] MAC: %s | Service UUID: %s\n", macAddress.c_str(), BLE_SERVICE_UUID);
    Serial.printf("[BLE] Telemetry Char (Notify/Read): %s | Command Char (Write): %s\n",
                  BLE_CHAR_TELEMETRY_UUID, BLE_CHAR_COMMAND_UUID);
}

inline void notifyTowerBLE(const String& compactJson) {
    if (pBleTelemetryChar != nullptr) {
        pBleTelemetryChar->setValue((uint8_t*)compactJson.c_str(), compactJson.length());
        if (!pBleServer) return;
        static uint32_t lastMtuWarning = 0;
        for (uint8_t i = 0; i < pBleServer->getConnectedCount(); ++i) {
            auto peer = pBleServer->getPeerInfo(i);
            if (!shBleNotificationFits(peer.getMTU(), compactJson.length())) {
                if (!lastMtuWarning || uint32_t(millis() - lastMtuWarning) >= 30000UL) {
                    lastMtuWarning = millis();
                    Serial.println("[BLE] Full telemetry remains readable on FF01; Android should request MTU 517 before subscribing.");
                }
                continue;
            }
            if (!pBleTelemetryChar->notify(peer.getConnHandle()))
                Serial.println("[BLE] Telemetry notification failed; GPIO state remains readable on FF01.");
        }
    }
}

inline void ensureTowerBleAdvertising() {
    if (!pBleServer || !pBleServer->getConnectedCount()) {
        NimBLEAdvertising* pAdvertising = NimBLEDevice::getAdvertising();
        if (pAdvertising != nullptr && !pAdvertising->isAdvertising()) {
            Serial.println(F("[BLE] Advertising watchdog: restarting advertising..."));
            pAdvertising->start();
        }
    }
}

inline bool isTowerBleConnected() {
    return pBleServer && pBleServer->getConnectedCount();
}
