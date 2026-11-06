#pragma once

static void logConnectivityHeap(bool force);

static uint32_t g_lastWifiAttemptMs = 0;
static bool g_hotspotProbe = false;
static bool g_wifiTimeStarted = false;

static bool saveConnectivityState() {
    if (!prefs.begin("smarthydro", false)) return false;
    bool saved = prefs.putInt("pref_mode", g_connectivity.preferred) == sizeof(int32_t);
    saved = (prefs.putInt("op_mode", g_connectivity.active) == sizeof(int32_t)) && saved;
    saved = (prefs.putInt("mode_reason", g_connectivity.reason) == sizeof(int32_t)) && saved;
    saved = (prefs.putBool("is_offline", g_connectivity.active == MODE_EMERGENCY_SOFTAP) == sizeof(bool)) && saved;
    if (saved) saved = prefs.putUChar("mode_ver", 2) == sizeof(uint8_t);
    prefs.end();
    return saved;
}

static void exitMode(int oldMode) {
    closeOnlineTransport();
    if (wsInitialized) wsActuator.disconnect();
    wsInitialized = false;
    isWsConnected = false;
    isWsHandshakeDone = false;
    dnsServer.stop();
    server.stop();
    isCaptivePortalActive = false;
    auto wifiMode = WiFi.getMode();
    if ((wifiMode == WIFI_AP || wifiMode == WIFI_AP_STA) &&
        (oldMode == MODE_EMERGENCY_SOFTAP || portalActive || isCaptivePortalActive))
        WiFi.softAPdisconnect(false);
    portalActive = false;
    g_hotspotProbe = false;
    g_wifiTimeStarted = false;
}

static void enterMode(int newMode, int reason) {
    bool needsCredentials = newMode == MODE_WIFI_CLOUD && !hasSavedWiFiCredentials();
    if (needsCredentials) { newMode = MODE_EMERGENCY_SOFTAP; reason = MODE_REASON_AUTO_FALLBACK; }
    bool setupRequired = newMode == MODE_EMERGENCY_SOFTAP && g_connectivity.preferred != MODE_EMERGENCY_SOFTAP;
    if (g_connectivityStarted && newMode == g_operatingMode) {
        // Keep the current socket/hotspot alive when the user selects it again.
        if (newMode == MODE_EMERGENCY_SOFTAP && reason == MODE_REASON_USER && g_hotspotProbe) {
            WiFi.setAutoReconnect(false);
            WiFi.disconnect(false, false);
            g_hotspotProbe = false;
        }
        if (newMode == MODE_EMERGENCY_SOFTAP) configureWiFiSetupPortal(setupRequired);
        g_connectivity.reason = reason;
        g_modeSwitchFailure = saveConnectivityState() ? (needsCredentials ? "wifi_setup_required" : nullptr) : "mode_not_saved";
        broadcastCompactJson(true);
        return;
    }
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
        if (!startOfflineSoftAP(name.c_str(), setupRequired)) {
            g_modeSwitchFailure = "mode_start_failed";
            g_connectivity.enter(MODE_LORA_BLE, MODE_REASON_AUTO_FALLBACK, millis());
            g_operatingMode = MODE_LORA_BLE;
            isOfflineMode = false;
            WiFi.mode(WIFI_OFF);
            Serial.println("[Mode] Hotspot could not start; keeping LoRa available.");
        }
    }
    g_connectivityStarted = true;
    if (!saveConnectivityState()) {
        g_modeSwitchFailure = "mode_not_saved";
        Serial.println("[Mode] Mode is active, but saving it failed. It may not survive a restart.");
    }
    if (needsCredentials && !g_modeSwitchFailure) g_modeSwitchFailure = "wifi_setup_required";
    logConnectivityHeap(true);
    Serial.printf("[Mode] Preferred %d, active %d, reason %s.\n", g_connectivity.preferred,
                  g_operatingMode, g_connectivity.reason == MODE_REASON_USER ? "user" : "fallback");
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
    if (g_staDisconnectDetected) {
        int reason = g_lastStaDisconnectReason;
        g_staDisconnectDetected = false;
        g_wifiTimeStarted = false;
        if (reason == 201)
            Serial.println("[WiFi] No suitable access point found. Check the exact network name and 2.4 GHz coverage. Retrying on the managed schedule; LoRa remains available.");
        else if (reason != 36)
            Serial.printf("[WiFi] Connection lost (reason %d). Retrying on the managed schedule; LoRa remains available.\n", reason);
    }
    if (WiFi.status() == WL_CONNECTED && !g_wifiTimeStarted) {
        configTime(0, 0, "pool.ntp.org", "time.google.com");
        g_wifiTimeStarted = true;
    }
    if (g_requestedConnectivityMode >= 0) {
        int mode = g_requestedConnectivityMode;
        g_requestedConnectivityMode = -1;
        g_modeSwitchFailure = nullptr;
        g_connectivity.preferred = mode;
        enterMode(mode, MODE_REASON_USER);
        return;
    }
    bool savedWifi = true;
    if (g_operatingMode == MODE_LORA_BLE && uint32_t(now - g_connectivity.enteredMs) >= 300000UL &&
        (!g_connectivity.hasGoodLoraAck || uint32_t(now - g_connectivity.lastGoodLoraMs) >= 300000UL)) {
        savedWifi = hasSavedWiFiCredentials();
    }
    int next = g_connectivity.next(now, savedWifi);
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
