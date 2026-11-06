#pragma once
#include <stdint.h>
#include <stddef.h>
#include <string.h>

#pragma pack(push, 1)
struct TelemetryReceiptFrame {
    uint8_t type = 0x22;
    uint8_t mac[6] = {};
    uint64_t seq = 0;
    uint32_t utc = 0;
    int8_t rssi = 0;
    uint8_t stored = 0;
    uint8_t crc = 0;
};
struct TelemetrySampleFrame {
    uint8_t type = 0x13;
    uint8_t mac[6] = {};
    uint64_t seq = 0;
    uint32_t utc = 0, age = 0;
    uint16_t ph = UINT16_MAX, ec = UINT16_MAX;
    int16_t temperature = INT16_MIN;
    uint16_t humidity = UINT16_MAX;
    uint8_t light = UINT8_MAX;
    uint16_t waterRaw = 0;
    uint8_t relays = 0, modeFlags = 0, predictions = 0, system = 0, crc = 0;
};
#pragma pack(pop)
static_assert(sizeof(TelemetryReceiptFrame) == 22, "Receipt wire size changed");
static_assert(sizeof(TelemetrySampleFrame) == 39, "Telemetry wire size changed");
static inline uint8_t telemetryReceiptCrc(const uint8_t* data, size_t size) {
    uint8_t crc = 0;
    for (size_t i = 0; i < size; ++i) {
        crc ^= data[i];
        for (int n = 0; n < 8; ++n) crc = (crc & 0x80) ? uint8_t((crc << 1) ^ 0x07) : uint8_t(crc << 1);
    }
    return crc;
}
static inline bool decodeTelemetryReceipt(const uint8_t* data, size_t size, TelemetryReceiptFrame& frame) {
    if (size != sizeof(frame) || data[0] != 0x22 ||
        telemetryReceiptCrc(data, size - 1) != data[size - 1]) return false;
    memcpy(&frame, data, size);
    return frame.seq != 0;
}
static inline bool decodeTelemetrySample(const uint8_t* data, size_t size, TelemetrySampleFrame& frame) {
    if (size != sizeof(frame) || data[0] != 0x13 ||
        telemetryReceiptCrc(data, size - 1) != data[size - 1]) return false;
    memcpy(&frame, data, size); return frame.seq != 0;
}

#ifdef ARDUINO
#include <ArduinoJson.h>
#include <math.h>
static inline bool encodeTelemetrySample(JsonObject doc, TelemetrySampleFrame& frame) {
    if (doc.containsKey("ev")) return false;
    frame = TelemetrySampleFrame{};
    frame.seq = doc["seq"] | (uint64_t)0;
    String mac = doc["m"] | ""; mac.replace(":", "");
    if (!frame.seq || mac.length() != 12) return false;
    for (int i = 0; i < 6; ++i) frame.mac[i] = strtoul(mac.substring(i * 2, i * 2 + 2).c_str(), nullptr, 16);
    frame.utc = doc["ts"] | 0UL; frame.age = doc["age"] | 0UL;
    if (!doc["p"].isNull()) frame.ph = (uint16_t)lroundf(doc["p"].as<float>() * 100);
    if (!doc["e"].isNull()) frame.ec = (uint16_t)lroundf(doc["e"].as<float>() * 100);
    if (!doc["t"].isNull()) frame.temperature = (int16_t)lroundf(doc["t"].as<float>() * 10);
    if (!doc["h"].isNull()) frame.humidity = (uint16_t)lroundf(doc["h"].as<float>() * 10);
    if (!doc["l"].isNull()) frame.light = doc["l"].as<uint8_t>();
    frame.waterRaw = doc["wr"] | 0; frame.relays = doc["r"] | 0;
    frame.modeFlags = (doc["md"] | 0) | ((doc["pm"] | 0) << 2) | ((doc["mr"] | 0) << 4)
        | ((doc["w"] | 0) << 5) | ((doc["tu"] | false) ? 0x40 : 0) | (doc.containsKey("al") ? 0x80 : 0);
    frame.predictions = doc["mp"] | 0xAA;
    String system = doc["sys"] | ""; frame.system = system == "fodder" ? 1 : 0;
    frame.crc = telemetryReceiptCrc((const uint8_t*)&frame, sizeof(frame) - 1);
    return true;
}
static inline bool telemetrySampleJson(const uint8_t* data, size_t size, String& output) {
    TelemetrySampleFrame frame;
    if (!decodeTelemetrySample(data, size, frame)) return false;
    char mac[13]; snprintf(mac, sizeof(mac), "%02X%02X%02X%02X%02X%02X", frame.mac[0], frame.mac[1], frame.mac[2], frame.mac[3], frame.mac[4], frame.mac[5]);
    StaticJsonDocument<768> doc;
    doc["m"] = mac; doc["seq"] = frame.seq; doc["age"] = frame.age;
    if (frame.utc) doc["ts"] = frame.utc;
    if (frame.ph != UINT16_MAX) doc["p"] = serialized(String(frame.ph / 100.0f, 2));
    if (frame.ec != UINT16_MAX) doc["e"] = serialized(String(frame.ec / 100.0f, 2));
    if (frame.temperature != INT16_MIN) doc["t"] = serialized(String(frame.temperature / 10.0f, 1));
    if (frame.humidity != UINT16_MAX) doc["h"] = serialized(String(frame.humidity / 10.0f, 1));
    if (frame.light != UINT8_MAX) doc["l"] = frame.light;
    doc["wr"] = frame.waterRaw; doc["r"] = frame.relays;
    doc["md"] = frame.modeFlags & 3; doc["pm"] = (frame.modeFlags >> 2) & 3;
    doc["mr"] = (frame.modeFlags >> 4) & 1; doc["w"] = (frame.modeFlags >> 5) & 1;
    if (frame.modeFlags & 0x40) doc["tu"] = true;
    if (frame.modeFlags & 0x80) doc["al"] = "PH_RUNAWAY_ABORT";
    doc["mp"] = frame.predictions; doc["sys"] = frame.system ? "fodder" : "hydroponics";
    output = ""; serializeJson(doc, output); return true;
}
static inline bool telemetryReceiptJson(const uint8_t* data, size_t size, String& output) {
    TelemetryReceiptFrame frame;
    if (!decodeTelemetryReceipt(data, size, frame)) return false;
    char mac[13];
    snprintf(mac, sizeof(mac), "%02X%02X%02X%02X%02X%02X", frame.mac[0], frame.mac[1], frame.mac[2], frame.mac[3], frame.mac[4], frame.mac[5]);
    StaticJsonDocument<256> doc;
    doc["m"] = mac; doc["ack"] = 1; doc["up"] = frame.stored ? 1 : 0;
    doc["stored"] = frame.stored ? 1 : 0; doc["seq"] = frame.seq;
    doc["utc"] = frame.utc; doc["r"] = frame.rssi;
    output = ""; serializeJson(doc, output); return true;
}
#endif
