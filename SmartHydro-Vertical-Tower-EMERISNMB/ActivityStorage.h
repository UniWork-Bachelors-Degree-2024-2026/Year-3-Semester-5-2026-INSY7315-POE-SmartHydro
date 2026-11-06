#pragma once

struct SHActivity {
    uint64_t seq = 0;
    int64_t utc = 0;
    uint32_t at = 0, boot = 0, duration = 0, sent = 0;
    float value = 0, low = 0, high = 0;
    uint16_t cid = 0;
    uint8_t phase = 0, source = 0, action = 0, prediction = 2, reason = 0, version = 1;
    uint32_t checksum = 0;
};
static SHActivity activityQueue[16];
static Preferences activityPreferences;
static bool activityReady = false;
static uint64_t activityNext = 0, activityEnd = 0;
static uint8_t auditSource = 5, activeAuditSource = 3;
static uint16_t auditCid = 0, activeAuditCid = 0;
static uint32_t activeAuditDuration = 0;
static const char* lastDoseRefusal = "invalid_dose";
static uint32_t activityChecksum(const SHActivity& value) {
    uint32_t hash = 2166136261UL;
    auto* bytes = reinterpret_cast<const uint8_t*>(&value);
    for (size_t i = 0; i < offsetof(SHActivity, checksum); ++i) hash = (hash ^ bytes[i]) * 16777619UL;
    return hash;
}
static void activityKey(int i, char (&key)[5]) { snprintf(key, sizeof(key), "e%02d", i); }
static void beginActivityStorage() {
    activityReady = activityPreferences.begin("sh-activity", false);
    if (!activityReady) { Serial.println("[Activity] Cannot open event storage."); return; }
    activityNext = activityPreferences.getULong64("end", 1); activityEnd = activityNext;
    for (int i = 0; i < 16; ++i) {
        char key[5]; activityKey(i, key); SHActivity event;
        bool fromFile = readOutboxFile(key, &event, sizeof(event));
        if (fromFile && (!event.seq || event.version != 1 || event.checksum != activityChecksum(event))) fromFile = false;
        if (!fromFile && (!activityPreferences.isKey(key) || activityPreferences.getBytesLength(key) != sizeof(event) ||
            activityPreferences.getBytes(key, &event, sizeof(event)) != sizeof(event))) continue;
        if (event.version != 1 ||
            !event.seq || event.checksum != activityChecksum(event)) continue;
        if (outboxFilesReady && activityPreferences.isKey(key) && (fromFile || writeOutboxFile(key, &event, sizeof(event)))) activityPreferences.remove(key);
        event.sent = 0; activityQueue[i] = event;
    }
}
static uint8_t auditAction(uint8_t pin) {
    if (pin == SH_AUDIT_WATER_PIN) return 0;
    if (pin == PIN_RELAY_LIGHT) return 1;
    if (pin == PIN_RELAY_FAN_CIRC) return 2;
    if (pin == PIN_RELAY_FAN_EXT) return 3;
    if (pin == PIN_DOSE_PH_UP) return 4;
    if (pin == PIN_DOSE_PH_DOWN) return 5;
    if (pin == PIN_DOSE_EC_UP) return 6;
    return 7;
}
static uint8_t auditReason(const char* reason) {
    const char* names[] = {"accepted", "sensor_fault", "pump_busy", "pulse_limit", "mixing_lockout", "ph_hourly_cap", "safety_storage", "cutoff_timer", "outer_safety_limit", "dose_count_limit", "sensor_stale", "sensor_recovering"};
    for (uint8_t i = 0; i < sizeof(names) / sizeof(names[0]); ++i) if (reason && strcmp(reason, names[i]) == 0) return i;
    return 8;
}
static void recordActivity(uint8_t pin, uint8_t phase, uint8_t source, uint32_t duration = 0, uint16_t cid = 0, uint8_t reason = 0) {
    if (!activityReady || !telemetryBootId) return;
    static uint32_t lastRefusal = 0;
    if (phase == 4 && lastRefusal && uint32_t(millis() - lastRefusal) < 60000UL) return;
    if (phase == 4) lastRefusal = millis();
    if (activityNext >= activityEnd) {
        uint64_t end = activityNext + 64;
        if (end >= (1ULL << 62) || activityPreferences.putULong64("end", end) != 8) return;
        activityEnd = end;
    }
    int slot = -1, oldest = 0;
    for (int i = 0; i < 16; ++i) {
        if (!activityQueue[i].seq && slot < 0) slot = i;
        if (activityQueue[i].seq < activityQueue[oldest].seq) oldest = i;
    }
    if (slot < 0) { slot = oldest; Serial.println("[Activity] Offline event queue full; replacing its oldest entry."); }
    SHActivity event{};
    event.seq = (1ULL << 62) | activityNext++; event.utc = telemetryUtc(); event.at = millis(); event.boot = telemetryBootId;
    event.phase = phase; event.source = source; event.action = auditAction(pin); event.duration = duration; event.cid = cid; event.reason = reason;
    bool ph = event.action == 4 || event.action == 5;
    event.value = ph ? currentTelemetry.ph : currentTelemetry.ec;
    event.low = ph ? targetMinPh : targetMinEc; event.high = ph ? targetMaxPh : targetMaxEc;
    event.prediction = ph ? currentTelemetry.mlPh : currentTelemetry.mlEc;
    event.checksum = activityChecksum(event); activityQueue[slot] = event;
    char key[5]; activityKey(slot, key);
    bool saved = outboxFilesReady ? writeOutboxFile(key, &event, sizeof(event)) : activityPreferences.putBytes(key, &event, sizeof(event)) == sizeof(event);
    if (!saved) Serial.println("[Activity] Flash write failed; event is only in RAM.");
    Serial.printf("[Activity] source=%u action=%u phase=%u value=%.2f range=%.2f..%.2f estimated=%.2f mL runtime=%lu ms cid=%u reason=%u\n",
        source, event.action, phase, event.value, event.low, event.high, duration / 800.0f, (unsigned long)duration, cid, reason);
}
static void reportDoseRefusal(uint8_t pin, const char* name, const char* reason, uint32_t duration) {
    lastDoseRefusal = reason;
    static uint32_t lastLog = 0; static uint8_t lastPin = 0; static const char* previous = "";
    if (!auditCid && pin == lastPin && strcmp(previous, reason) == 0 && uint32_t(millis() - lastLog) < 60000UL) return;
    lastLog = millis(); lastPin = pin; previous = reason;
    if (pin) recordActivity(pin, 4, 6, duration, auditCid, auditReason(reason));
    Serial.printf("[Dosing] Refused %s: %s\n", name ? name : "unknown", reason);
}
static void acknowledgeActivity(uint64_t seq) {
    for (int i = 0; i < 16; ++i) if (activityQueue[i].seq == seq) {
        char key[5]; activityKey(i, key); activityQueue[i] = SHActivity{};
        bool removed = removeOutboxFile(key);
        if (activityPreferences.isKey(key)) removed = activityPreferences.remove(key) && removed;
        if (!removed) Serial.println("[Activity] Old flash event may replay after reboot.");
        return;
    }
}
static String activityJson(SHActivity& event) {
    StaticJsonDocument<640> doc;
    String mac = deviceMac; mac.replace(":", "");
    doc["m"] = mac; doc["sys"] = SH_CAPTURE_SYSTEM; doc["seq"] = event.seq; doc["ev"] = event.phase;
    doc["as"] = event.source; doc["aa"] = event.action; doc["ad"] = event.duration;
    if (event.action >= 4) {
        doc["av"] = serialized(String(event.value, 2)); doc["an"] = serialized(String(event.low, 2)); doc["az"] = serialized(String(event.high, 2));
        doc["ap"] = event.prediction;
    }
    if (event.cid) doc["ac"] = event.cid;
    if (event.reason) doc["aw"] = event.reason;
    int64_t utc = telemetryUtc();
    if (event.utc) { doc["ts"] = event.utc; doc["age"] = utc >= event.utc ? utc - event.utc : 0; }
    else if (event.boot == telemetryBootId) { doc["age"] = uint32_t(millis() - event.at) / 1000UL; }
    else doc["tu"] = true;
    String wire; serializeJson(doc, wire); return wire;
}
