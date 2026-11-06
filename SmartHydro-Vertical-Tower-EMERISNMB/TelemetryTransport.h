#pragma once

static bool sendTelemetryWifi(const RingBufferSlot& sample) {
    if (!sample.seq || WiFi.status() != WL_CONNECTED) return false;
    StaticJsonDocument<1024> doc;
    doc["deviceMac"] = deviceMac; doc["localIp"] = WiFi.localIP().toString();
    doc["ph"] = sample.ph; doc["ec"] = sample.ec; doc["temperature"] = sample.temp;
    doc["humidity"] = sample.hum; doc["light"] = sample.light;
    doc["waterLevelRaw"] = sample.waterRaw; doc["waterLevelDetected"] = (sample.flags & 1) != 0;
    doc["operatingMode"] = sample.mode; doc["preferredMode"] = sample.preferred; doc["modeReason"] = sample.reason;
    doc["ml_ph"] = sample.mlPh; doc["ml_ec"] = sample.mlEc;
    doc["ml_temp"] = sample.mlTemp; doc["ml_humidity"] = sample.mlHum;
    doc["mainWaterPump"] = (sample.relays & 1) != 0; doc["spraySystem"] = (sample.relays & 1) != 0;
    doc["growLight"] = (sample.relays & 2) != 0; doc["circulationFan"] = (sample.relays & 4) != 0;
    doc["extractorFan"] = (sample.relays & 8) != 0; doc["phUpPump"] = (sample.relays & 16) != 0;
    doc["phDownPump"] = (sample.relays & 32) != 0; doc["nutrientUpPump"] = (sample.relays & 64) != 0;
    doc["dilutionDownPump"] = (sample.relays & 128) != 0;
    addCaptureMetadata(doc, sample);
    String body; serializeJson(doc, body);
    markTelemetrySent(telemetryLoraJson(sample));
    if (!beginOnlineRequest("/api/telemetry?compact=true")) { onlineHttpFailed = true; return false; }
    feedWatchdog(); int code = towerOnlineHttp.POST(body); feedWatchdog();
    String response = towerOnlineHttp.getString();
    towerOnlineHttp.end(); // Finish this request before a piggybacked command changes mode.
    bool stored = onlineCaptureStored(code, response, sample.seq);
    onlineHttpFailed = !stored;
    if (stored) { onlineStoredOnce = true; onlineLastStoredMs = millis(); }
    if (stored) {
        acknowledgeTelemetry(sample.seq);
        g_connectivity.cloudSuccess(millis());
        StaticJsonDocument<1536> reply;
        if (deserializeJson(reply, response) == DeserializationError::Ok) {
            learnTelemetryClock(reply["utc"] | 0UL);
            if (reply["command"].is<JsonObject>()) {
                JsonObject command = reply["command"].as<JsonObject>();
                SH_HANDLE_CAPTURE_COMMAND(command);
            }
        }
    }
    return stored;
}

static void syncWithCloud() {
    if ((g_operatingMode == MODE_WIFI_CLOUD || g_operatingMode == MODE_EMERGENCY_SOFTAP) && WiFi.status() == WL_CONNECTED)
        sendTelemetryWifi(captureTelemetrySnapshot());
}

static void serviceActivityOutbox() {
    static uint32_t lastAttempt = 0;
    uint32_t now = millis();
    if (uint32_t(now - lastAttempt) < 5000UL) return;
    bool wifi = g_operatingMode == MODE_WIFI_CLOUD && onlineTlsReady() && !onlineHttpFailed;
    bool lora = g_lastGatewayCloudUpMs && uint32_t(now - g_lastGatewayCloudUpMs) < 60000UL && uint32_t(now - g_lastLoraTxMs) >= 2500UL;
    if (!wifi && !lora) return;
    int slot = -1;
    for (int i = 0; i < 16; ++i) if (activityQueue[i].seq &&
        (!activityQueue[i].sent || uint32_t(now - activityQueue[i].sent) >= 15000UL) &&
        (slot < 0 || activityQueue[i].seq < activityQueue[slot].seq)) slot = i;
    if (slot < 0) return;
    lastAttempt = now; auto& event = activityQueue[slot]; event.sent = now;
    String wire = activityJson(event);
    if (wire.length() > 250) { Serial.println("[Activity] Event exceeds radio packet size."); return; }
    if (!wifi) { SH_SEND_STORED_LORA(wire); return; }
    StaticJsonDocument<640> directEvent;
    if (deserializeJson(directEvent, wire) != DeserializationError::Ok) return;
    directEvent["via"] = "wifi"; wire = ""; serializeJson(directEvent, wire);
    if (!beginOnlineRequest("/api/telemetry/lora?compact=true")) return;
    feedWatchdog(); int code = towerOnlineHttp.POST(wire); feedWatchdog();
    if (onlineCaptureStored(code, towerOnlineHttp.getString(), event.seq)) acknowledgeActivity(event.seq);
    towerOnlineHttp.end();
}

static void serviceStoredTelemetry() {
    serviceActivityOutbox();
    static uint32_t cycleMs = 0, lastAttemptMs = 0;
    static uint8_t remaining = 0;
    uint32_t now = millis();
    if (uint32_t(now - cycleMs) >= 15000UL) { cycleMs = now; remaining = 2; }
    if (!remaining || uint32_t(now - lastAttemptMs) < 2500UL) return;
    bool wifi = g_operatingMode == MODE_WIFI_CLOUD && onlineTlsReady() && !onlineHttpFailed;
    bool lora = g_lastGatewayCloudUpMs && uint32_t(now - g_lastGatewayCloudUpMs) < 60000UL;
    if (!wifi && (!lora || uint32_t(now - g_lastLoraTxMs) < 2500UL)) return;
    int index = telemetryQueue.oldest(now, true);
    if (index < 0) return;
    RingBufferSlot sample = telemetryQueue.slots[index];
    lastAttemptMs = now; --remaining;
    if (wifi) sendTelemetryWifi(sample);
    else {
        String wire = telemetryLoraJson(sample);
        SH_SEND_STORED_LORA(wire);
        markTelemetrySent(wire);
    }
}
