#pragma once
#include "TelemetryQueue.h"
#include <sys/time.h>
#include "OutboxStorage.h"

static TelemetryQueue telemetryQueue;
static Preferences telemetryPreferences;
static RingBufferSlot lastCapturedSample;
static uint32_t telemetryBootId = 0, lastCapturePollMs = UINT32_MAX;
static uint64_t telemetryNextSeq = 0, telemetryReservedEnd = 0;
static bool telemetryStorageReady = false;

static void telemetryKey(int index, char (&key)[5]) { snprintf(key, sizeof(key), "r%02d", index); }
static void persistTelemetrySlot(int index) {
    if (index < 0 || !telemetryStorageReady) return;
    char key[5]; telemetryKey(index, key);
    auto& sample = telemetryQueue.slots[index];
    sample.checksum = telemetryChecksum(sample);
    bool saved = outboxFilesReady ? writeOutboxFile(key, &sample, sizeof(sample)) : telemetryPreferences.putBytes(key, &sample, sizeof(sample)) == sizeof(sample);
    if (!saved)
        Serial.println(F("[History] Flash write failed; this reading is only safe in RAM."));
}
static void beginTelemetryStorage() {
    beginOutboxFiles();
    telemetryStorageReady = telemetryPreferences.begin("sh-telemetry", false);
    if (!telemetryStorageReady) { Serial.println(F("[History] Cannot open telemetry storage.")); return; }
    telemetryBootId = telemetryPreferences.getUInt("boot", 0) + 1;
    telemetryNextSeq = telemetryPreferences.getULong64("seq_end", 1);
    telemetryReservedEnd = telemetryNextSeq;
    for (int i = 0; i < (int)TelemetryQueue::Capacity; ++i) {
        char key[5]; telemetryKey(i, key);
        RingBufferSlot sample;
        bool fromFile = readOutboxFile(key, &sample, sizeof(sample));
        if (fromFile && (!sample.seq || sample.version != 1 || sample.checksum != telemetryChecksum(sample))) fromFile = false;
        if (!fromFile && (!telemetryPreferences.isKey(key) || telemetryPreferences.getBytesLength(key) != sizeof(sample) ||
            telemetryPreferences.getBytes(key, &sample, sizeof(sample)) != sizeof(sample))) continue;
        if (!sample.seq || sample.version != 1 || sample.checksum != telemetryChecksum(sample)) continue;
        if (outboxFilesReady && telemetryPreferences.isKey(key) && (fromFile || writeOutboxFile(key, &sample, sizeof(sample))))
            telemetryPreferences.remove(key);
        sample.lastSentMs = 0;
        telemetryQueue.slots[i] = sample;
    }
    if (!telemetryBootId || telemetryPreferences.putUInt("boot", telemetryBootId) != sizeof(uint32_t)) {
        telemetryStorageReady = false; Serial.println("[History] Cannot reserve a boot identity. Capture is paused; stored records were kept.");
    }
    Serial.printf("[History] Restored %u queued readings.\n", (unsigned)telemetryQueue.count());
}
static int64_t telemetryUtc() { int64_t value = time(nullptr); return value >= 1704067200LL ? value : 0; }
static void learnTelemetryClock(uint32_t utc) {
    if (utc < 1704067200UL) return;
    int64_t previous = telemetryUtc();
    if (!previous || llabs(previous - (int64_t)utc) > 3) {
        struct timeval clock = { (time_t)utc, 0 }; settimeofday(&clock, nullptr);
    }
    for (int i = 0; i < (int)TelemetryQueue::Capacity; ++i) {
        auto& sample = telemetryQueue.slots[i];
        if (sample.seq && !sample.capturedUnix && sample.bootId == telemetryBootId) {
            sample.capturedUnix = (int64_t)utc - uint32_t(millis() - sample.capturedMs) / 1000UL;
            persistTelemetrySlot(i);
        }
    }
}
static void acknowledgeTelemetry(uint64_t seq) {
    int i = telemetryQueue.find(seq);
    if (i < 0) return;
    char key[5]; telemetryKey(i, key);
    telemetryQueue.acknowledge(seq);
    bool removed = removeOutboxFile(key);
    if (telemetryStorageReady && telemetryPreferences.isKey(key)) removed = telemetryPreferences.remove(key) && removed;
    if (!removed)
        Serial.println(F("[History] Receipt saved in RAM; an old flash copy may replay after reboot."));
}
static uint64_t nextRelayReportSequence() {
    if (!telemetryStorageReady || !telemetryNextSeq || telemetryNextSeq >= (1ULL << 62)) return 0;
    if (telemetryNextSeq >= telemetryReservedEnd) {
        uint64_t nextEnd = telemetryNextSeq + 256;
        if (nextEnd >= (1ULL << 62) || telemetryPreferences.putULong64("seq_end", nextEnd) != sizeof(uint64_t)) return 0;
        telemetryReservedEnd = nextEnd;
    }
    return telemetryNextSeq++;
}

static RingBufferSlot captureTelemetrySnapshot() {
    uint8_t mask = computeRelayBitmask();
    if (lastCapturedSample.seq && lastCapturePollMs == lastSensorPollMs &&
        lastCapturedSample.relays == mask && lastCapturedSample.mode == g_operatingMode &&
        lastCapturedSample.preferred == g_connectivity.preferred && lastCapturedSample.reason == g_connectivity.reason) return lastCapturedSample;
    RingBufferSlot sample{};
    sample.seq = nextRelayReportSequence();
    if (!sample.seq) return sample;
    sample.capturedMs = millis(); sample.bootId = telemetryBootId;
    sample.capturedUnix = telemetryUtc();
    sample.ph = currentTelemetry.ph; sample.ec = currentTelemetry.ec;
    sample.temp = currentTelemetry.temp; sample.hum = currentTelemetry.hum; sample.light = currentTelemetry.lux;
    sample.waterRaw = currentTelemetry.waterRaw; sample.flags = (currentTelemetry.waterOk ? 1 : 0) | (g_phRunawayAbort ? 2 : 0);
    sample.relays = mask; sample.mode = g_operatingMode;
    sample.preferred = g_connectivity.preferred; sample.reason = g_connectivity.reason;
    sample.mlPh = currentTelemetry.mlPh; sample.mlEc = currentTelemetry.mlEc;
    sample.mlTemp = currentTelemetry.mlTemp; sample.mlHum = currentTelemetry.mlHum;
    if (telemetryQueue.count() == TelemetryQueue::Capacity)
        Serial.println(F("[History] Queue full; replacing the oldest of 64 readings."));
    int index = telemetryQueue.insert(sample); persistTelemetrySlot(index);
    lastCapturedSample = sample; lastCapturePollMs = lastSensorPollMs;
    return sample;
}
template<typename T> static void addCaptureMetadata(T& doc, const RingBufferSlot& sample) {
    if (!sample.seq) return;
    doc["seq"] = sample.seq;
    doc["age"] = TelemetryQueue::age(sample, millis(), telemetryBootId, telemetryUtc());
    if (sample.capturedUnix) doc["ts"] = sample.capturedUnix;
    if (TelemetryQueue::uncertain(sample, telemetryBootId)) doc["tu"] = true;
    doc["sys"] = SH_CAPTURE_SYSTEM;
}
static String telemetryLoraJson(const RingBufferSlot& sample) {
    if (!sample.seq) return "";
    StaticJsonDocument<640> doc;
    String mac = deviceMac; mac.replace(":", ""); doc["m"] = mac;
    if (sample.ph >= 0) doc["p"] = serialized(String(sample.ph, 2));
    if (sample.ec >= 0) doc["e"] = serialized(String(sample.ec, 2));
    if (sample.temp != -99) doc["t"] = serialized(String(sample.temp, 1));
    if (sample.hum >= 0) doc["h"] = serialized(String(sample.hum, 1));
    if (sample.light >= 0) doc["l"] = (int)sample.light;
    doc["w"] = sample.flags & 1; doc["wr"] = sample.waterRaw; doc["r"] = sample.relays;
    doc["mp"] = (sample.mlPh & 3) | ((sample.mlEc & 3) << 2) | ((sample.mlTemp & 3) << 4) | ((sample.mlHum & 3) << 6);
    doc["md"] = sample.mode; doc["pm"] = sample.preferred; doc["mr"] = sample.reason;
    if (sample.flags & 2) doc["al"] = "PH_RUNAWAY_ABORT";
    addCaptureMetadata(doc, sample);
    String wire; serializeJson(doc, wire); return wire;
}
static void markTelemetrySent(const String& json) {
    StaticJsonDocument<640> doc;
    if (deserializeJson(doc, json) != DeserializationError::Ok) return;
    uint64_t seq = doc["seq"] | (uint64_t)0;
    int index = telemetryQueue.find(seq);
    if (index >= 0) telemetryQueue.slots[index].lastSentMs = millis();
}
static void acknowledgeActivity(uint64_t seq);
static void handleTelemetryReceipt(JsonObject doc) {
    learnTelemetryClock(doc["utc"] | 0UL);
    if ((doc["stored"] | 0) == 1 && (doc["up"] | 0) == 1)
    {
        uint64_t seq = doc["seq"] | (uint64_t)0;
        acknowledgeTelemetry(seq); acknowledgeActivity(seq);
    }
}
