#include <Arduino.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <WebSocketsClient.h>
#include <ArduinoJson.h>
#include <RadioLib.h>
#include <SPI.h>
#include <vector>
#include <time.h>
#include "GatewayTrust.h"
#include "SmartHydroFrame.h"
#include "TelemetryReceiptFrame.h"
#if __has_include("secrets.h")
#include "secrets.h"
#else
#include "secrets.example.h"
#endif

// SenseLoRa SL4GGY / ESP32-S3, SX1276. The modem is not initialised.
static SX1276 radio = new Module(4, 6, 5, 7, SPI, SPISettings());
static WebSocketsClient socketClient;
static SmartHydroAirtimeLedger airtime;
static SemaphoreHandle_t queueLock;
static volatile bool radioFlag = false;
static bool txActive = false;
static bool radioReady = false;
static uint32_t txSince = 0, txTimeout = 0;
static bool wsReady = false, wsStarted = false;
static uint32_t disconnectedAt = 0, reconnectDelay = 1000, nextWifi = 0;
static String authHeader;
struct Packet { uint32_t id, received, sentAt = 0; String json; bool inflight = false; };
static std::vector<Packet> packets;
static std::vector<String> receipts;
static uint32_t nextPacketId = 1, storedCount = 0, jsonSent = 0, jsonReceived = 0, reconnects = 0;
struct PendingCommand { uint16_t cid = 0; String json; uint32_t received = 0, ttl = 0, lastTx = 0; uint8_t tries = 0; };
static PendingCommand command;
static void IRAM_ATTR radioEvent() { radioFlag = true; }
static String compactMac(String mac) { mac.replace(":", ""); mac.replace("-", ""); mac.toUpperCase(); return mac; }
static bool localNode(const char* mac) { return compactMac(String(mac)) == HOME_NODE_MAC; }

static void stageCommand(JsonObject value) {
    if (!localNode(value["m"] | "")) return;
    uint32_t cid = value["cid"] | 0UL, ttl = value["ttl"] | 0UL;
    if (!cid || cid > 65535 || !ttl) return;
    String wire; serializeJson(value, wire);
    if (wire.length() > 250) { Serial.println("[Command] RF command too large; not sent."); return; }
    xSemaphoreTake(queueLock, portMAX_DELAY);
    if (command.cid != cid) {
        command = PendingCommand{(uint16_t)cid, wire, millis(), ttl < 60000UL ? ttl : 60000UL, millis() - 2500UL, 0};
        Serial.printf("[Command] Received CID %u for the fodder controller.\n", (unsigned)cid);
    }
    xSemaphoreGive(queueLock);
}

static void returnedCommands(JsonObject result) {
    if (result["command"].is<JsonObject>()) stageCommand(result["command"].as<JsonObject>());
    for (JsonObject item : result["commands"].as<JsonArray>()) stageCommand(item);
}

static void queueStoredReceipt(const Packet& packet, JsonObject result) {
    StaticJsonDocument<1024> original;
    if (deserializeJson(original, packet.json) != DeserializationError::Ok) return;
    StaticJsonDocument<256> ack;
    ack["m"] = HOME_NODE_MAC; ack["ack"] = 1; ack["up"] = 1;
    uint64_t seq = original["seq"] | (uint64_t)0;
    if (seq) {
        ack["seq"] = seq; ack["stored"] = 1;
        ack["utc"] = result["utc"] | (uint32_t)time(nullptr);
        ack["r"] = original["gr"] | -127;
    } else if ((original["cack"] | 0) == 1 && (original["ar"] | 0) == 1) {
        ack["ac"] = original["cid"];
        String phase = original["phase"] | "accepted";
        ack["ap"] = phase == "completed" ? 1 : phase == "cancelled" ? 2 : 0;
    } else return;
    String wire; serializeJson(ack, wire);
    xSemaphoreTake(queueLock, portMAX_DELAY);
    bool duplicate = false;
    for (const auto& queued : receipts) if (queued == wire) duplicate = true;
    if (!duplicate && receipts.size() < 16) receipts.push_back(wire);
    xSemaphoreGive(queueLock);
}

static void completePacket(uint32_t id, JsonObject result) {
    returnedCommands(result);
    if (!(result["success"] | false)) return;
    Packet saved{}; bool found = false;
    xSemaphoreTake(queueLock, portMAX_DELAY);
    for (auto item = packets.begin(); item != packets.end(); ++item) if (item->id == id) {
        saved = *item; packets.erase(item); found = true; break;
    }
    xSemaphoreGive(queueLock);
    if (found) {
        ++storedCount; queueStoredReceipt(saved, result);
        Serial.printf("[Stored] API accepted packet %lu after %lu ms; returning radio receipt.\n", (unsigned long)id, (unsigned long)(millis() - saved.received));
    }
}

static void receiveSocket(WStype_t type, uint8_t* bytes, size_t length) {
    if (type == WStype_CONNECTED) {
        String hello = "{\"protocol\":\"json\",\"version\":1}\x1e";
        socketClient.sendTXT(hello); jsonSent += hello.length(); return;
    }
    if (type == WStype_DISCONNECTED) {
        wsReady = false; disconnectedAt = millis(); ++reconnects;
        reconnectDelay = min(reconnectDelay * 2UL, 30000UL);
        socketClient.setReconnectInterval(reconnectDelay + random(0, 500));
        xSemaphoreTake(queueLock, portMAX_DELAY);
        for (auto& packet : packets) packet.inflight = false;
        xSemaphoreGive(queueLock);
        return;
    }
    if (type != WStype_TEXT || length > 8192) return;
    jsonReceived += length;
    String data; data.concat((const char*)bytes, length);
    int start = 0;
    while (start < (int)data.length()) {
        int end = data.indexOf('\x1e', start); if (end < 0) end = data.length();
        StaticJsonDocument<3072> message;
        if (deserializeJson(message, data.substring(start, end)) == DeserializationError::Ok) {
            int kind = message["type"] | 0;
            String target = message["target"] | "";
            if (kind == 1 && target == "Ready") {
                const char* mac = message["arguments"][0]["mac"] | "";
                if (!localNode(mac)) { socketClient.disconnect(); return; }
                wsReady = true; reconnectDelay = 1000; socketClient.setReconnectInterval(1000);
                Serial.println("[WSS] Authenticated fodder connection ready.");
            } else if (kind == 1 && target == "Command") stageCommand(message["arguments"][0].as<JsonObject>());
            else if (kind == 3) {
                uint32_t id = strtoul(message["invocationId"] | "0", nullptr, 10);
                if (message["result"].is<JsonObject>()) completePacket(id, message["result"].as<JsonObject>());
                else Serial.printf("[WSS] Packet %lu not confirmed; retained for retry.\n", (unsigned long)id);
            } else if (kind == 6) { String ping = "{\"type\":6}\x1e"; socketClient.sendTXT(ping); jsonSent += ping.length(); }
            else if (kind == 7) socketClient.disconnect();
        }
        start = end + 1;
    }
}

static bool pickPacket(Packet& selected) {
    xSemaphoreTake(queueLock, portMAX_DELAY);
    int best = -1;
    unsigned uploading = 0;
    for (const auto& item : packets)
        if (item.inflight && uint32_t(millis() - item.sentAt) < 10000UL) ++uploading;
    for (size_t i = 0; i < packets.size(); ++i) {
        auto& item = packets[i];
        if (item.inflight && uint32_t(millis() - item.sentAt) < 10000UL) continue;
        // Leave room for a controller reply while an older reading is being saved.
        if (uploading >= 2 || (uploading && item.json.indexOf("\"cack\"") < 0)) continue;
        if (best < 0 || (packets[best].json.indexOf("\"cack\"") < 0 && item.json.indexOf("\"cack\"") >= 0)) best = i;
    }
    if (best >= 0) { packets[best].inflight = true; packets[best].sentAt = millis(); selected = packets[best]; }
    xSemaphoreGive(queueLock);
    return best >= 0;
}

static String agedPacket(const Packet& packet) {
    StaticJsonDocument<1024> data;
    if (deserializeJson(data, packet.json) != DeserializationError::Ok) return "";
    if (!data.containsKey("ts") && !(data["tu"] | false)) data["age"] = (data["age"] | 0UL) + uint32_t(millis() - packet.received) / 1000UL;
    String result; serializeJson(data, result); return result;
}

static void httpsExchange(const char* route, Packet* packet) {
    WiFiClientSecure tls; tls.setCACert(GATEWAY_ROOT_CA); tls.setHandshakeTimeout(5);
    HTTPClient http;
    String url = String("https://") + HOME_API_HOST + route;
    if (!http.begin(tls, url)) return;
    http.setConnectTimeout(3000); http.setTimeout(4000);
    http.addHeader("X-SmartHydro-Device-Key", HOME_GATEWAY_KEY);
    int status;
    if (packet) {
        http.addHeader("Content-Type", "application/json");
        String body = agedPacket(*packet); jsonSent += body.length(); status = http.POST(body);
    } else status = http.GET();
    if (status == 200 && http.getSize() <= 4096) {
        String body = http.getString(); jsonReceived += body.length();
        StaticJsonDocument<3072> result;
        if (deserializeJson(result, body) == DeserializationError::Ok) {
            if (packet) completePacket(packet->id, result.as<JsonObject>());
            else returnedCommands(result.as<JsonObject>());
        }
    } else {
        Serial.printf("[HTTPS] %s HTTP %d; unconfirmed data remains queued.\n", route, status);
        if (status == 400 && http.getSize() <= 4096) {
            StaticJsonDocument<2048> problem;
            String response = http.getString(); jsonReceived += response.length();
            if (deserializeJson(problem, response) == DeserializationError::Ok) {
                const char* message = problem["message"] | (problem["title"] | "Request rejected");
                Serial.printf("[API] %.240s\n", message);
                for (JsonPair error : problem["errors"].as<JsonObject>())
                    Serial.printf("[API] Invalid field: %.64s\n", error.key().c_str());
            }
        }
    }
    http.end();
}

static void networkTask(void*) {
    WiFi.mode(WIFI_STA); WiFi.setSleep(false);
    uint32_t lastHttp = 0, lastStats = 0; bool ntpStarted = false;
    for (;;) {
        if (WiFi.status() != WL_CONNECTED) {
            wsReady = false;
            if (!nextWifi || (int32_t)(millis() - nextWifi) >= 0) { WiFi.begin(HOME_WIFI_SSID, HOME_WIFI_PASSWORD); nextWifi = millis() + 15000UL; }
            vTaskDelay(pdMS_TO_TICKS(20)); continue;
        }
        if (!ntpStarted) { configTime(0, 0, "pool.ntp.org", "time.google.com"); ntpStarted = true; }
        if (time(nullptr) < 1704067200) { vTaskDelay(pdMS_TO_TICKS(100)); continue; }
        if (!HOME_TEST_HTTPS_ONLY && !wsStarted) {
            authHeader = String("X-SmartHydro-Device-Key: ") + HOME_GATEWAY_KEY + "\r\n";
            socketClient.beginSslWithCA(HOME_API_HOST, 443, "/hubs/home-gateway", GATEWAY_ROOT_CA, "");
            socketClient.setExtraHeaders(authHeader.c_str()); socketClient.onEvent(receiveSocket);
            socketClient.setReconnectInterval(1000); socketClient.enableHeartbeat(30000, 10000, 2);
            wsStarted = true; disconnectedAt = millis();
        }
        if (wsStarted) socketClient.loop();
        if (wsReady) {
            Packet packet;
            if (pickPacket(packet)) {
                StaticJsonDocument<1536> invocation, body;
                deserializeJson(body, agedPacket(packet));
                invocation["type"] = 1; invocation["invocationId"] = String(packet.id); invocation["target"] = "Packet";
                invocation.createNestedArray("arguments").add(body.as<JsonObject>());
                String wire; serializeJson(invocation, wire); wire += '\x1e';
                socketClient.sendTXT(wire); jsonSent += wire.length();
            }
        } else if ((HOME_TEST_HTTPS_ONLY || uint32_t(millis() - disconnectedAt) >= 10000UL) && uint32_t(millis() - lastHttp) >= 2000UL) {
            Packet packet;
            if (pickPacket(packet)) httpsExchange("/api/home-gateway/packet", &packet);
            httpsExchange("/api/home-gateway/commands", nullptr); lastHttp = millis();
        }
        if (uint32_t(millis() - lastStats) >= 60000UL) {
            lastStats = millis();
            Serial.printf("[Stats] mode=%s stored=%lu appTx=%lu appRx=%lu reconnects=%lu (application bytes, excludes TLS/IP).\n",
                wsReady ? "WSS" : "HTTPS fallback", (unsigned long)storedCount, (unsigned long)jsonSent, (unsigned long)jsonReceived, (unsigned long)reconnects);
        }
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}

static void radioLoop() {
    if (txActive) {
        if (radioFlag || uint32_t(millis() - txSince) > txTimeout) { radio.finishTransmit(); txActive = false; radioFlag = false; radio.startReceive(); }
        return;
    }
    if (radioFlag) {
        radioFlag = false; uint8_t bytes[256] = {}; size_t size = radio.getPacketLength();
        int state = size && size <= 255 ? radio.readData(bytes, size) : RADIOLIB_ERR_PACKET_TOO_LONG;
        String wire;
        if (state == RADIOLIB_ERR_NONE && !telemetrySampleJson(bytes, size, wire)) wire = String((char*)bytes);
        if (state == RADIOLIB_ERR_NONE) {
            StaticJsonDocument<1024> data;
            if (deserializeJson(data, wire) == DeserializationError::Ok && localNode(data["m"] | "") && !data.containsKey("ack")) {
                data["gr"] = (int)radio.getRSSI();
                if (!data.containsKey("ts") && !(data["tu"] | false) && time(nullptr) >= 1704067200) data["ts"] = (int64_t)time(nullptr) - (data["age"] | 0UL);
                wire = ""; serializeJson(data, wire);
                xSemaphoreTake(queueLock, portMAX_DELAY);
                if ((data["cack"] | 0) == 1 && command.cid == (data["cid"] | 0)) command = PendingCommand{};
                bool duplicate = false;
                for (const auto& queued : packets) if (queued.json == wire) duplicate = true;
                if (!duplicate && packets.size() < 24) packets.push_back(Packet{nextPacketId++, millis(), 0, wire, false});
                else if (!duplicate) Serial.println("[Queue] Full; no storage receipt sent. Heltec retains its reading.");
                xSemaphoreGive(queueLock);
            }
        }
        radio.startReceive(); return;
    }
    String wire;
    xSemaphoreTake(queueLock, portMAX_DELAY);
    if (command.cid && (uint32_t(millis() - command.received) >= command.ttl || command.tries >= 4)) command = PendingCommand{};
    bool isCommand = command.cid && uint32_t(millis() - command.lastTx) >= 2500UL;
    if (isCommand) wire = command.json;
    else if (!receipts.empty()) wire = receipts.front();
    if (wire.length()) {
        uint16_t toa = (radio.getTimeOnAir(wire.length()) + 999UL) / 1000UL;
        if (airtime.canTransmit(toa, SH_PRIO_CRITICAL_ACK) && radio.startTransmit(wire) == RADIOLIB_ERR_NONE) {
            airtime.recordTransmission(toa); txActive = true; txSince = millis(); txTimeout = toa + 1000;
            if (isCommand) { command.lastTx = millis(); ++command.tries; Serial.printf("[LoRa TX] Command CID %u, attempt %u.\n", command.cid, command.tries); }
            else receipts.erase(receipts.begin());
        }
    }
    xSemaphoreGive(queueLock);
}

void setup() {
    Serial.begin(115200); delay(300);
    Serial.println("SmartHydro HOME tester: Wi-Fi only; no modem or SMS. Target 10:51:DB:29:3F:0C.");
    if (strlen(HOME_GATEWAY_KEY) != 64 || strspn(HOME_GATEWAY_KEY, "0123456789abcdefABCDEF") != 64 || !strlen(HOME_WIFI_SSID)) {
        Serial.println("Fill the ignored secrets.h before testing. Cloud connection is disabled."); return;
    }
    queueLock = xSemaphoreCreateMutex();
    if (!queueLock) { Serial.println("Queue allocation failed; tester stopped."); return; }
    pinMode(21, OUTPUT); digitalWrite(21, HIGH); delay(100);
    SPI.begin(12, 13, 11, 4);
    int state = radio.begin(868.1f, 125.0f, 7, 5, 0x12, 14, 8);
    if (state != RADIOLIB_ERR_NONE) { Serial.printf("LoRa init failed: %d.\n", state); return; }
    radio.setDio0Action(radioEvent, RISING); radio.startReceive();
    if (xTaskCreatePinnedToCore(networkTask, "home-network", 12288, nullptr, 1, nullptr, 0) != pdPASS) {
        Serial.println("Network task allocation failed; tester stopped."); return;
    }
    radioReady = true;
}
void loop() { if (radioReady) radioLoop(); delay(2); }
