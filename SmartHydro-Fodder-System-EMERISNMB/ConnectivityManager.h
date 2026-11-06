#pragma once

static void logConnectivityHeap(bool force);

static uint32_t g_lastWifiAttemptMs = 0;
static bool g_hotspotProbe = false;
static bool g_wifiTimeStarted = false;

static void saveConnectivityState() {
    prefs.begin("smarthydro", false);
    prefs.putInt("pref_mode", g_connectivity.preferred);
    prefs.putInt("op_mode", g_connectivity.active);
    prefs.putInt("mode_reason", g_connectivity.reason);
    prefs.putBool("is_offline", g_connectivity.active == MODE_EMERGENCY_SOFTAP);
    prefs.putUChar("mode_ver", 2);
    prefs.end();
}

static void exitMode(int oldMode) {
    if (wsInitialized) wsActuator.disconnect();
    wsInitialized = false;
    isWsConnected = false;
    isWsHandshakeDone = false;
    dnsServer.stop();
    server.stop();
    isCaptivePortalActive = false;
    // softAPdisconnect() starts AP internally; only call it for an active AP.
    if ((WiFi.getMode() & WIFI_MODE_AP) != 0) WiFi.softAPdisconnect(true);
    portalActive = false;
    g_hotspotProbe = false;
    g_wifiTimeStarted = false;
}

static void enterMode(int newMode, int reason) {
    exitMode(g_operatingMode);
    g_connectivity.enter(newMode, reason, millis());
    g_operatingMode = newMode;
    isOfflineMode = newMode == MODE_EMERGENCY_SOFTAP;
    if (newMode == MODE_LORA_BLE) {
        safeEnterpriseDisable();
        safeWiFiDisconnect();
        WiFi.mode(WIFI_OFF);
    } else if (newMode == MODE_WIFI_CLOUD) {
        beginSavedWiFi();
        g_lastWifiAttemptMs = millis();
        lastCloudSyncMs = millis() - 15000UL;
    } else {
        String name = String(SH_HOTSPOT_PREFIX) + getDeviceMacSuffix();
        startOfflineSoftAP(name.c_str());
        isCaptivePortalActive = true;
        dnsServer.start(DNS_PORT, "*", WiFi.softAPIP());
    }
    saveConnectivityState();
    logConnectivityHeap(true);
    Serial.printf("[Mode] Preferred %d, active %d, reason %s.\n", g_connectivity.preferred,
                  g_operatingMode, reason == MODE_REASON_USER ? "user" : "fallback");
    broadcastCompactJson(true);
}

static void loadConnectivityState() {
    preloadWiFiCredentials();
    prefs.begin("smarthydro", true);
    bool currentNumbering = prefs.getUChar("mode_ver", 0) >= 2 || prefs.isKey("pref_mode");
    int mode = prefs.getInt("op_mode", MODE_LORA_BLE);
    int preferred = prefs.getInt("pref_mode", mode);
    int reason = prefs.getInt("mode_reason", MODE_REASON_USER);
    prefs.end();
    // Older sketches used 1=SoftAP, 2=Online and did not save pref_mode.
    if (!currentNumbering) {
        if (mode == 1 || mode == 2) mode = 3 - mode;
        preferred = mode;
        Serial.println("[Mode] Migrated legacy mode numbering; other settings are unchanged.");
    }
    if (!SH_PRIMARY_WIFI_FALLBACK && preferred == MODE_LORA_BLE && reason == MODE_REASON_AUTO_FALLBACK) {
        mode = MODE_LORA_BLE;
        reason = MODE_REASON_USER;
        Serial.println("[Mode] Home pilot: returning to preferred LoRa + BLE after an earlier fallback.");
    }
    Serial.printf("[Mode] Automatic Wi-Fi fallback from Primary: %s.\n",
                  SH_PRIMARY_WIFI_FALLBACK ? "enabled" : "disabled for this home pilot");
    g_connectivity.preferred = preferred >= 0 && preferred <= 2 ? preferred : MODE_LORA_BLE;
    enterMode(mode >= 0 && mode <= 2 ? mode : MODE_LORA_BLE,
              reason == MODE_REASON_AUTO_FALLBACK ? reason : MODE_REASON_USER);
}

static void logConnectivityHeap(bool force = false) {
    static uint32_t lastLog = 0;
    if (!force && uint32_t(millis() - lastLog) < 60000UL) return;
    lastLog = millis();
    Serial.printf("[Heap] mode=%d free=%lu min=%lu SignalR=%s HTTP-sync=15s\n",
        g_operatingMode, (unsigned long)ESP.getFreeHeap(), (unsigned long)ESP.getMinFreeHeap(),
        isWsConnected && isWsHandshakeDone ? "connected" : "disconnected");
}

static void serviceConnectivity() {
    logConnectivityHeap(false);
    uint32_t now = millis();
    if (WiFi.status() == WL_CONNECTED && !g_wifiTimeStarted) {
        configTime(0, 0, "pool.ntp.org", "time.google.com");
        g_wifiTimeStarted = true;
    }
    if (g_requestedConnectivityMode >= 0) {
        int mode = g_requestedConnectivityMode;
        g_requestedConnectivityMode = -1;
        g_connectivity.preferred = mode;
        enterMode(mode, MODE_REASON_USER);
        return;
    }
    int next = g_connectivity.next(now, SH_PRIMARY_WIFI_FALLBACK);
    if (next != g_operatingMode) {
        enterMode(next, next == g_connectivity.preferred ? MODE_REASON_USER : MODE_REASON_AUTO_FALLBACK);
        return;
    }
    if (g_operatingMode == MODE_WIFI_CLOUD && WiFi.status() != WL_CONNECTED &&
        uint32_t(now - g_lastWifiAttemptMs) >= 30000UL) {
        g_lastWifiAttemptMs = now;
        beginSavedWiFi();
    }
    if (g_operatingMode == MODE_EMERGENCY_SOFTAP && g_connectivity.preferred != MODE_EMERGENCY_SOFTAP) {
        if (!g_hotspotProbe && uint32_t(now - g_connectivity.lastProbeMs) >= 180000UL) {
            g_connectivity.lastProbeMs = now;
            g_hotspotProbe = beginSavedWiFi(true);
        }
        if (g_hotspotProbe && WiFi.status() == WL_CONNECTED) {
            uint32_t before = g_connectivity.lastCloudMs;
            syncWithCloud();
            if (g_connectivity.lastCloudMs != before) {
                enterMode(MODE_WIFI_CLOUD, g_connectivity.preferred == MODE_WIFI_CLOUD ?
                          MODE_REASON_USER : MODE_REASON_AUTO_FALLBACK);
                g_connectivity.cloudSuccess(millis());
                return;
            }
            g_hotspotProbe = false;
            WiFi.disconnect(false, false);
        } else if (g_hotspotProbe && uint32_t(now - g_connectivity.lastProbeMs) >= 15000UL) {
            g_hotspotProbe = false;
            WiFi.disconnect(false, false);
        }
    }
}
