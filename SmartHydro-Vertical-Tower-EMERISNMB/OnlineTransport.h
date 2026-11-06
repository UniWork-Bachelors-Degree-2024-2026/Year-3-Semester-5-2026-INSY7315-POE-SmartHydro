#pragma once

// Keep the HTTP object alive too: its destructor closes even a reusable socket.
static WiFiClientSecure towerOnlineClient;
static HTTPClient towerOnlineHttp;
static bool onlineHttpFailed = false;
static bool onlineStoredOnce = false;
static uint32_t onlineLastStoredMs = 0;

static bool directOnlineHealthy() {
    return WiFi.status() == WL_CONNECTED && !onlineHttpFailed &&
        ((isWsConnected && isWsHandshakeDone) || (onlineStoredOnce && uint32_t(millis() - onlineLastStoredMs) < 30000UL));
}

static bool onlineTlsReady() {
    return WiFi.status() == WL_CONNECTED && getApiBaseUrl().startsWith("https://")
        && time(nullptr) >= 1704067200;
}

static bool beginOnlineRequest(const String& path, uint16_t timeoutMs = 4000) {
    if (!onlineTlsReady()) return false;
    towerOnlineClient.setCACert(GATEWAY_ROOT_CA);
    towerOnlineClient.setHandshakeTimeout(3);
    if (!towerOnlineHttp.begin(towerOnlineClient, getApiBaseUrl() + path)) return false;
    towerOnlineHttp.setReuse(true);
    towerOnlineHttp.setConnectTimeout(3000);
    towerOnlineHttp.setTimeout(timeoutMs);
    towerOnlineHttp.addHeader("Content-Type", "application/json");
    towerOnlineHttp.addHeader("X-SmartHydro-Device-Key", shDeviceKey());
    return true;
}

static void closeOnlineTransport() {
    towerOnlineHttp.setReuse(false);
    towerOnlineHttp.end();
    towerOnlineClient.stop();
    onlineHttpFailed = false;
    onlineStoredOnce = false;
}

static bool onlineCaptureStored(int code, const String& body, uint64_t sequence) {
    StaticJsonDocument<64> filter; filter["success"] = true; filter["seq"] = true;
    StaticJsonDocument<128> reply;
    return (code == 200 || code == 201) && sequence &&
        deserializeJson(reply, body, DeserializationOption::Filter(filter)) == DeserializationError::Ok &&
        (reply["success"] | false) && reply["seq"].is<uint64_t>() && reply["seq"].as<uint64_t>() == sequence;
}
