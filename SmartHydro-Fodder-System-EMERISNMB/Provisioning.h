#pragma once
#include <time.h>
#include <WiFi.h>
#include <esp_mac.h>
#include <WebServer.h>
#include <DNSServer.h>
#include <Update.h>
#include <Preferences.h>
#include "esp_eap_client.h"

static int g_requestedConnectivityMode = -1;
static void requestConnectivityMode(int mode) {
    if (mode >= 0 && mode <= 2) g_requestedConnectivityMode = mode;
}


// Optional local test secrets (ignored by Git)
#if __has_include("secrets.h")
#include "secrets.h"
#endif

// Home pilots can keep Wi-Fi on the gateway instead of starting it on this node.
#ifndef SH_PRIMARY_WIFI_FALLBACK
#define SH_PRIMARY_WIFI_FALLBACK true
#endif

// Keep the BLE implementation available while isolating a failing radio startup.
#ifndef SH_FODDER_BLE_ENABLED
#define SH_FODDER_BLE_ENABLED true
#endif

#ifndef SH_FODDER_LORA_ENABLED
#define SH_FODDER_LORA_ENABLED true
#endif
#ifndef SH_FODDER_LORA_TX_ENABLED
#define SH_FODDER_LORA_TX_ENABLED true
#endif

#ifndef EAP_ID
#define EAP_ID ""
#endif
#ifndef EAP_USERNAME
#define EAP_USERNAME ""
#endif
#ifndef EAP_PASSWORD
#define EAP_PASSWORD ""
#endif

#ifndef NODE_BADGE_NAME
#define NODE_BADGE_NAME "Fodder Sprouting System"
#endif

// Struct for runtime telemetry snapshot shared with WebServer
struct TelemetrySnapshot {
    float ph = -1.0f;
    float ec = -1.0f;
    float temp = -99.0f;
    float hum = -1.0f;
    float lux = -1.0f;
    int waterRaw = 0;
    bool waterOk = false;
    int mlPh = 2;
    int mlEc = 2;
    int mlTemp = 2;
    int mlHum = 2;
};

// Forward declarations of relay and dosing functions implemented in main sketch
void setRelay(uint8_t pin, bool active);
void startAsyncDose(const char* actName, int durationSec, const char* cmdId = "", bool isManual = false);
String localRelaysJson();
void registerLocalControlRoutes();

// Global Preferences, WebServer and DNSServer handles
static Preferences prefs;
static WebServer server(80);
static DNSServer dnsServer;
static TelemetrySnapshot currentTelemetry;
static bool isCaptivePortalActive = false;
static const byte DNS_PORT = 53;

// -------------------------------------------------------------
// Enterprise 802.1X & Wi-Fi Lifecycle Guards (Prevents Panic / Load Access Fault)
// -------------------------------------------------------------
static bool g_enterpriseEnabled = false;
static bool g_isManualSoftApTriggered = false;

static inline void safeEnterpriseDisable() {
    if (g_enterpriseEnabled) {
        esp_wifi_sta_enterprise_disable();
        g_enterpriseEnabled = false;
    }
}

static inline void safeWiFiDisconnect() {
    if (WiFi.getMode() != WIFI_MODE_NULL) {
        WiFi.disconnect(true, false);
    }
}

// Default Edu-WiFi credentials (populated via WiFiManager portal or optional secrets.h)
static const char* DEFAULT_SSID = "edu-wifi";
static const char* DEFAULT_EAP_ID = EAP_ID;
static const char* DEFAULT_EAP_USER = EAP_USERNAME;
static const char* DEFAULT_EAP_PASS = EAP_PASSWORD;

// Helpers for reliable MAC address retrieval across all ESP32 cores
static inline String getDeviceMacAddress() {
    uint8_t mac[6];
    esp_read_mac(mac, ESP_MAC_WIFI_STA);
    char buf[18];
    snprintf(buf, sizeof(buf), "%02X:%02X:%02X:%02X:%02X:%02X",
             mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    return String(buf);
}

static inline String getDeviceMacSuffix() {
    uint8_t mac[6];
    esp_read_mac(mac, ESP_MAC_WIFI_STA);
    char buf[6];
    snprintf(buf, sizeof(buf), "%02X%02X", mac[4], mac[5]);
    return String(buf);
}

// ==============================================================================
// SMARTHYDRO API BACKEND ENDPOINTS (PRIMARY: AZURE CLOUD, FALLBACK: LOCALHOST)
// ==============================================================================
// Primary: Official Azure Cloud Production Published API
static const char* AZURE_PUBLISHED_API = "https://smarthydroapi-exhjg0b2f9bqe4e6.southafricanorth-01.azurewebsites.net";

// Fallback: Localhost / Lab Development API
static const char* LOCAL_DEV_API = "http://10.100.100.23:5043";

// Primary default endpoint is Azure Cloud
static const char* DEFAULT_API = AZURE_PUBLISHED_API;
static const char* FALLBACK_API = LOCAL_DEV_API;

static bool g_usingFallbackApi = false;

static inline void setUseFallbackApi(bool fallback) {
    g_usingFallbackApi = fallback;
    Serial.printf("[API Endpoint] Active: %s (%s)\n",
                  fallback ? "FALLBACK (Localhost)" : "PRIMARY (Azure Cloud)",
                  fallback ? LOCAL_DEV_API : AZURE_PUBLISHED_API);
}

static inline bool isUsingFallbackApi() {
    return g_usingFallbackApi;
}

static inline String getApiBaseUrl() {
    prefs.begin("smarthydro", false);
    String url = g_usingFallbackApi ? String(FALLBACK_API) : String(DEFAULT_API);
    if (prefs.isKey("api_url")) {
        url = prefs.getString("api_url", url);
    }
    // Clean up any stale/broken old URLs
    if (url.length() == 0 || url.indexOf("10.100.100.22") >= 0 || url.indexOf("smarthydro-apiv2") >= 0 || url.indexOf("smarthydro-api-") >= 0) {
        url = g_usingFallbackApi ? String(FALLBACK_API) : String(DEFAULT_API); prefs.putString("api_url", url);
    }
    prefs.end();
    if (!url.startsWith("https://")) return String(DEFAULT_API);
    return url;
}

// Generates dynamic per-box secret from hardware chip MAC
static String getPerBoxSecret() {
    uint8_t mac[6];
    esp_read_mac(mac, ESP_MAC_WIFI_STA);
    char sec[16];
    snprintf(sec, sizeof(sec), "SH-%02X%02X%02X", mac[3], mac[4], mac[5]);
    return String(sec);
}

// Buffer to hold cached historical records for offline data-mule syncing (RoomDB)
#define MAX_CACHED_RECORDS 50
struct CachedRecord {
    unsigned long timestamp;
    float ph;
    float ec;
    float temp;
    float hum;
    float lux;
    int waterRaw;
    bool waterOk;
};
static CachedRecord offlineCache[MAX_CACHED_RECORDS];
static int cacheCount = 0;

static void pushOfflineRecord(float ph, float ec, float temp, float hum, float lux, int waterRaw, bool waterOk) {
    if (cacheCount < MAX_CACHED_RECORDS) {
        offlineCache[cacheCount] = { millis() / 1000, ph, ec, temp, hum, lux, waterRaw, waterOk };
        cacheCount++;
    } else {
        for (int i = 0; i < MAX_CACHED_RECORDS - 1; i++) {
            offlineCache[i] = offlineCache[i + 1];
        }
        offlineCache[MAX_CACHED_RECORDS - 1] = { millis() / 1000, ph, ec, temp, hum, lux, waterRaw, waterOk };
    }
}

// -------------------------------------------------------------
// NTP Real-Time Clock Helpers (South Africa Standard Time UTC+2)
// -------------------------------------------------------------
static bool isClockSynced() {
    time_t now = time(nullptr);
    return now >= 1704067200; // Past Jan 1, 2024
}

static String getFormattedTime() {
    time_t now = time(nullptr);
    if (!isClockSynced()) {
        return "Syncing... (UTC+2 Pending)";
    }
    struct tm timeinfo;
    localtime_r(&now, &timeinfo);
    char buf[32];
    strftime(buf, sizeof(buf), "%Y-%m-%d %H:%M:%S", &timeinfo);
    return String(buf);
}

// -------------------------------------------------------------
// Hardware Task Watchdog Timer (15-Second Panic Recovery)
// -------------------------------------------------------------
#include <esp_task_wdt.h>

static bool g_wdtInitialized = false;

static void initHardwareWatchdog(uint32_t timeoutSeconds = 30) {
    if (g_wdtInitialized) return;
#if defined(ESP_IDF_VERSION_VAL) && ESP_IDF_VERSION >= ESP_IDF_VERSION_VAL(5, 0, 0)
    esp_task_wdt_config_t wdt_config = {
        .timeout_ms = timeoutSeconds * 1000,
        .idle_core_mask = 0,
        .trigger_panic = true
    };
    esp_err_t err = esp_task_wdt_reconfigure(&wdt_config);
    if (err != ESP_OK) {
        esp_task_wdt_init(&wdt_config);
    }
#else
    esp_task_wdt_init(timeoutSeconds, true);
#endif
    esp_task_wdt_add(NULL);
    g_wdtInitialized = true;
    Serial.printf("[WDT] Hardware Task Watchdog initialized (%u seconds timeout).\n", timeoutSeconds);
}

static inline void feedWatchdog() {
    esp_task_wdt_reset();
}

// -------------------------------------------------------------
// Wi-Fi Disconnect Interception & Failure Reason Tracking
// -------------------------------------------------------------
static volatile int g_lastStaDisconnectReason = 0;
static volatile bool g_staDisconnectDetected = false;

static void onWiFiStationDisconnected(WiFiEvent_t event, WiFiEventInfo_t info) {
    if (event == ARDUINO_EVENT_WIFI_STA_DISCONNECTED) {
        int reason = info.wifi_sta_disconnected.reason;
        g_lastStaDisconnectReason = reason;
        g_staDisconnectDetected = true;
        Serial.printf("[WiFi Event] Disconnected from AP. Reason Code: %d\n", reason);
    }
}

// -------------------------------------------------------------
// 1. Enterprise Wi-Fi Connection (Guarded with 12s Timeout)
// -------------------------------------------------------------
static void preloadWiFiCredentials() {
    prefs.begin("smarthydro", false);
    if (prefs.getString("ssid", "").isEmpty()) {
        prefs.putString("ssid", "edu-wifi");
        prefs.putString("eap_id", DEFAULT_EAP_ID);
        prefs.putString("eap_user", DEFAULT_EAP_USER);
        prefs.putString("eap_pass", DEFAULT_EAP_PASS);
    }
    prefs.end();
}

static bool beginSavedWiFi(bool keepHotspot = false) {
    prefs.begin("smarthydro", true);
    String ssid = prefs.getString("ssid", "");
    String identity = prefs.getString("eap_id", "");
    String email = prefs.getString("eap_user", "");
    String password = prefs.getString("eap_pass", "");
    prefs.end();
    if (ssid.isEmpty()) return false;
    safeEnterpriseDisable();
    // Do not turn off the AP while testing the saved router.
    if (keepHotspot) WiFi.disconnect(false, false);
    else safeWiFiDisconnect();
    WiFi.mode(keepHotspot ? WIFI_AP_STA : WIFI_STA);
    WiFi.setSleep(false);
    WiFi.setAutoReconnect(true);
    if (!email.isEmpty()) {
        if (identity.isEmpty()) identity = email;
        esp_eap_client_set_identity((const unsigned char*)identity.c_str(), identity.length());
        esp_eap_client_set_username((const unsigned char*)email.c_str(), email.length());
        esp_eap_client_set_password((const unsigned char*)password.c_str(), password.length());
        if (esp_wifi_sta_enterprise_enable() != ESP_OK) return false;
        g_enterpriseEnabled = true;
        WiFi.begin(ssid.c_str());
    } else {
        WiFi.begin(ssid.c_str(), password.c_str());
    }
    Serial.printf("[WiFi] Connecting to %s in the background.\n", ssid.c_str());
    return true;
}

// -------------------------------------------------------------
// Official SmartHydro Brand Logo (High-Res 200x200 PNG embedded)
// -------------------------------------------------------------
static const char SMARTHYDRO_LOGO_B64[] PROGMEM = 
    "data:image/png;base64,"
    "iVBORw0KGgoAAAANSUhEUgAAAMgAAADICAMAAACahl6sAAADAFBMVEX9/f0AAAAPOBccRyT+/v4jSiknVC4viqsUQhypt6sykbHO"
    "18/9/f24xbpLZ1Dl6uZxhnUILA+TpZb+/v79/f2HmYr9/f39/f39/f1jemja4tsdUijCzMMbPCE8WkJcdWLZ5+ZXdFu6y8ROmLB8"
    "k4JCXEeVxtM6Y0Pn5+drqLydq6GFuMcqg5a62uIiPiehraJ5kX6cs6IlfJmqqqq11N01YDx7tsjE3OPZ2dlsrMMbfZ5ToLgscm5/"
    "f38eh6d9joE0osOBjYSfsp1fgGTByr8gRB/MzMxHjqYcgpoQfKDg5N8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACN6dTAAABAHRSTlP+AP//"
    "BP/////////Q////////jiz/cbFJ////////////////////C/////////////8D/////wf/////Av//////////Bf////8AAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGMIx3wAAEgNJREFUeNrNXQd72zgShQESJk2xSaJ6sS07GzvJplw228v18v9/0M0A"
    "KpRMghyAUhbf7cXOemU+Dqa9GQzYVXfLg7X9ajiZjAbjXq/H9qvXG48Ho9Fk6J3+dCeLdQwCIIzG5ed/uQDRDk6HYFhHKNQfiIG1"
    "XIjGK/3HXx+IfpB/DUdjRl7jkZJMF1iYMwwlikGPWa7eYOLtPuerAVFvElAwx6WwOIqFOQpjaC+LY7kMHcXCXGB4kzHrbI21WC4M"
    "RMEY9VinqzdygMJsYQwH7AxrYA2F/ZlgKChDOyh0IPjOzgZjL5WzA4GX5Y3YmdfoNV0ojLyrJj129tWbkPcXI+6q4ZhdZI2HxP3F"
    "aLtqwC62QFUoQmEkcfTYBVePJBTWHsclxXEQSsdAQMj24igKB6G03V7tgPzl6sre5oacx/aWuO32YmffVtEy4Mv07NuLtcLhouUr"
    "LgRPnHTe6wQIbNKJg8JmfC6E5IXDR0zaKAprgcMlJOlz6fuIJHQJWa68X12BwKtwsboRF4gD/recudjhZpVnjWruEpOkPgcMCgtf"
    "OUUsjSrPzomD3YGiCx6HueCCT8+KhJndh+cUlPS5H/h5xtg09x3VhPUakDCzPJxw3AspfP6IXz6Czs+X0RmRsPPhSEEKMvCVL/yf"
    "/pqdDwk7Gw6WcN+XwUJ/E6MZ5uvzIakF8uvVazccGUe96O8jRw4SyftuSF7XI2H1bKhbLhii4S2LAAQUCL5wyxtf1/p4VuvP3XDM"
    "0Aty/0hlAvAn0jEDro1WWB0OxyxqBQoi+JGZWiwF6MzU7XMHVzQgV46UTwaOY87fn7gV5U0Kt08e1SBh1Tgmbr8NXHkw59npX085"
    "OJY8dvvsSTUSVp1/uP2uCOPEquBKBlIE36Zun16dn7AqBXE0vODHIcQSUVW2KNyyLO1OqhS+CoiroqMHEXlYbZTlP/389zMoPOte"
    "0UPICf18WgsSvEncvcKz7hWE1yjIVk0g1RJ+92rCXnr0Xgce5NvIlDPK4K5zNWGdK0iOAgmNplnIoOhaTVjHGyvMMcTKzLZAQkgf"
    "d7y5WLcbayYCk4JsrTPkJq5q8mJzsW4tVgKavuQNmWAEQKSrNzm1XKzTjfUAMYjI3zcbaMyB+51urmMgjjkIpIEQFbaIb6cqWYnc"
    "InqvFohrrIgZhwweWxLCvki6jB5Zh5qeoMXyo3YUPdKoDx3qO+tO04sceMW2WTlywtIxVDnSd1YWiHtokrfeLusAbfDM6VeWRcI6"
    "8+nfiLng7XONFGD4jja47N/ZQSBDx9gdiSsCSbJQwb5bqDI8iIR1JJAQ2ThaPl7kYulog0siYd1oSPot2NMgIRflfLdqQ8krsm4E"
    "AuybCKiqqyJ6Nxs8OAXiKJC+slihRV1OOtrgveFiXfiQmWJNqkOTRZFNp1m/Wlh3gfTdOPq9L2FdOPUEfYJMq42ZXqJfU3kQqhLk"
    "ztCzDqIsZBD96kwJk6g52DMpqnm5kEMCk7uUsibeMZCxi0tHJrTytaZLXdQFoDW1kSm2E7gkWeOyRNzykEd+wrsfYcQl6p24sttO"
    "ddKtBWbOtrevsvRq0xPlnAcoEM7zVT0X4bvUSQcHIE4c6WyJD1Kjr+l0Ok2Qx7qb3hWmJEs6R/PMVdXX9Rtrv72M/16lY/yDa4LF"
    "HHdWiLGi0ezEuRkofgS4xYXj3mKqfOtWgjbraswbgLCpckMO3h2RMLedlQWNFBUCkQ1vYy5dKlmTPRDrnXWvEtawiVoJZJteqMhp"
    "bzGnxoBHEAg/IqTTGNcirQcyW8RhGB6rRCJOP4cYpmggQ6fY5LjDpA+qjaFVUkR7IP4eSBpOJTCRsI67HKNlbWmorU8EINZN+6py";

// Scalable Vector Icons (Zero Emojis, Crisp Inline SVGs in Flash PROGMEM)
static const char SVG_ALERT[] PROGMEM = 
    "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' style='width:16px;height:16px;display:inline-block;vertical-align:-2px;margin-right:6px;flex-shrink:0;'><path d='M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z'></path><line x1='12' y1='9' x2='12' y2='13'></line><line x1='12' y1='17' x2='12.01' y2='17'></line></svg>";

static const char SVG_UPLOAD[] PROGMEM = 
    "<svg viewBox='0 0 24 24' fill='none' stroke='#1a5632' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' style='width:36px;height:36px;margin:0 auto 8px;display:block;'><path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'></path><polyline points='17 8 12 3 7 8'></polyline><line x1='12' y1='3' x2='12' y2='15'></line></svg>";

static const char SVG_CHECK[] PROGMEM = 
    "<svg viewBox='0 0 24 24' fill='none' stroke='#1a5632' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round' style='width:48px;height:48px;margin:0 auto 12px;display:block;'><path d='M22 11.08V12a10 10 0 1 1-5.93-9.14'></path><polyline points='22 4 12 14.01 9 11.01'></polyline></svg>";

static const char SVG_SYNC[] PROGMEM = 
    "<svg viewBox='0 0 24 24' fill='none' stroke='#1a5632' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round' style='width:42px;height:42px;margin:0 auto 10px;display:block;animation:spin 2s linear infinite;'><polyline points='23 4 23 10 17 10'></polyline><polyline points='1 20 1 14 7 14'></polyline><path d='M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15'></path></svg>";

static const char SVG_PLANT[] PROGMEM = 
    "<svg viewBox='0 0 24 24' fill='none' stroke='#1a5632' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' style='width:42px;height:42px;margin:0 auto 10px;display:block;'><path d='M7 20h10'></path><path d='M10 20c0-4.42 3-8 3-12'></path><path d='M13 8c1.5-2 4-2.5 6-2 0 3-1.5 5.5-4 6'></path><path d='M11 13c-2-1.5-4.5-1.5-6 0 .5 2.5 2.5 4 5 4'></path></svg>";

// Shared Base CSS for Portal & OTA
static String getSharedStyles() {
    return "<style>"
           ":root { --p:#1a5632; --p-h:#144427; --bg:#f0f4f1; --card:#ffffff; --txt:#1f2937; --muted:#6b7280; --border:#dce4de; --err-bg:#fef2f2; --err-border:#fecaca; --err-txt:#991b1b; }"
           "* { box-sizing:border-box; margin:0; padding:0; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; }"
           "body { background:var(--bg); color:var(--txt); margin:0; padding:24px 12px; min-height:100vh; display:flex; align-items:center; justify-content:center; }"
           ".card { background:var(--card); border:1px solid var(--border); border-radius:18px; max-width:420px; width:100%; padding:28px 24px; box-shadow:0 10px 25px -5px rgba(26,86,50,0.08),0 8px 10px -6px rgba(26,86,50,0.04); }"
           ".header { text-align:center; margin-bottom:20px; }"
           ".brand-logo { width:76px; height:76px; margin:0 auto 12px; display:block; border-radius:50%; box-shadow:0 4px 14px rgba(26,86,50,0.16); object-fit:cover; }"
           ".brand-title { font-size:24px; font-weight:800; color:var(--p); letter-spacing:-0.5px; margin:0; }"
           ".brand-slogan { font-size:12px; font-weight:600; color:#1e653d; text-transform:uppercase; letter-spacing:1.2px; margin-top:2px; }"
           ".node-badge { display:inline-block; margin-top:10px; padding:4px 12px; background:#e8f5e9; color:#1b5e20; border:1px solid #c8e6c9; border-radius:20px; font-size:12px; font-weight:700; }"
           ".alert-box { padding:14px 16px; border-radius:12px; margin-bottom:18px; text-align:left; font-size:13px; line-height:1.4; animation:slideDown 0.3s ease; }"
           ".alert-error { background:var(--err-bg); border:1px solid var(--err-border); color:var(--err-txt); }"
           ".alert-title { font-weight:700; margin-bottom:3px; display:flex; align-items:center; }"
           ".alert-desc { font-weight:400; opacity:0.95; }"
           "label { display:block; margin:14px 0 5px; font-weight:600; font-size:13px; color:#374151; text-align:left; }"
           "input[type=text], input[type=password] { width:100%; padding:12px 14px; background:#ffffff; border:1.5px solid #d1d5db; border-radius:9px; font-size:14px; color:#111827; transition:all 0.2s ease; }"
           "input:focus { border-color:var(--p); outline:none; box-shadow:0 0 0 3px rgba(26,86,50,0.14); }"
           ".btn-primary { width:100%; margin-top:20px; padding:13px; background:var(--p); color:#ffffff; border:none; border-radius:9px; font-weight:700; font-size:15px; cursor:pointer; box-shadow:0 3px 6px rgba(26,86,50,0.2); transition:all 0.2s ease; }"
           ".btn-primary:active { transform:scale(0.98); background:var(--p-h); }"
           ".divider { margin:24px 0 18px; border:0; border-top:1px solid #e5e7eb; }"
           ".offline-sec { text-align:center; background:#f9fafb; border:1px dashed #d1d5db; border-radius:12px; padding:16px 14px; }"
           ".offline-sec p { margin:0 0 10px; font-size:13px; color:#6b7280; font-weight:500; }"
           ".btn-offline { width:100%; padding:11px; background:#ffffff; color:var(--p); border:1.5px solid var(--p); border-radius:9px; font-weight:700; font-size:13px; cursor:pointer; transition:all 0.2s; }"
           ".footer { margin-top:16px; text-align:center; font-size:11px; color:#9ca3af; }"
           ".key-badge { display:inline-block; padding:2px 7px; background:#f3f4f6; border:1px solid #e5e7eb; border-radius:6px; font-family:monospace; font-size:11px; color:#374151; font-weight:600; }"
           ".link-action { display:inline-block; margin-top:8px; color:var(--p); font-size:12px; font-weight:600; text-decoration:none; }"
           ".link-action:hover { text-decoration:underline; }"
           ".upload-box { border:2px dashed #9ca3af; border-radius:12px; padding:22px 14px; text-align:center; background:#fafafa; margin:16px 0; cursor:pointer; transition:border 0.2s; }"
           ".upload-box:hover { border-color:var(--p); background:#f4f9f5; }"
           ".progress-wrap { display:none; margin-top:18px; background:#e5e7eb; border-radius:10px; height:14px; overflow:hidden; }"
           ".progress-fill { width:0%; height:100%; background:var(--p); transition:width 0.2s ease; }"
           ".progress-label { font-size:12px; font-weight:600; color:var(--p); margin-top:8px; display:none; text-align:center; }"
           "@keyframes slideDown { from { opacity:0; transform:translateY(-8px); } to { opacity:1; transform:translateY(0); } }"
           "@keyframes spin { from { transform:rotate(0deg); } to { transform:rotate(360deg); } }"
           "</style>";
}

// -------------------------------------------------------------
// 2. Web OTA Engine Setup (Secured with Matching UI & Progress)
// -------------------------------------------------------------
// -------------------------------------------------------------
static bool g_otaValidationFailed = false;
static String g_otaCustomError = "";

static void setupWebOtaHandlers() {
    server.on("/update", HTTP_GET, []() {
        if (!server.authenticate("admin", getPerBoxSecret().c_str())) {
            return server.requestAuthentication();
        }

        String html = "<!DOCTYPE html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'>"
                      "<title>Smart Hydro - OTA Update</title>" + getSharedStyles() + "</head><body>"
                      "<div class='card'>"
                      "<div class='header'>"
                      "<img class='brand-logo' src='" + String(FPSTR(SMARTHYDRO_LOGO_B64)) + "' alt='SmartHydro Logo'>"
                      "<div class='brand-title'>Smart Hydro</div>"
                      "<div class='brand-slogan'>Farming Made Smart</div>"
                      "<span class='node-badge'>Firmware Update (OTA v2.1)</span>"
                      "</div>"
                      "<div class='alert-box alert-error' id='otaErrBox' style='display:none'>"
                      "<div class='alert-title'>" + String(FPSTR(SVG_ALERT)) + " Update Error</div>"
                      "<div class='alert-desc' id='otaErrMsg'></div>"
                      "</div>"
                      "<form id='otaForm' method='POST' action='/update' enctype='multipart/form-data'>"
                      "<div class='upload-box' onclick='document.getElementById(\"binFile\").click()'>"
                      "<input type='file' id='binFile' name='update' accept='.bin' style='display:none' onchange='onFileSelected(this)' required>"
                      + String(FPSTR(SVG_UPLOAD)) +
                      "<div style='font-weight:700;font-size:14px;color:#1a5632' id='fileLabel'>Choose Compiled Binary (.bin)</div>"
                      "<div style='font-size:11px;color:#6b7280;margin-top:4px' id='fileSub'>Target Partition: 1.9MB Minimal SPIFFS</div>"
                      "</div>"
                      "<button type='submit' id='flashBtn' class='btn-primary'>START WIRELESS FLASH</button>"
                      "<div class='progress-wrap' id='progWrap'><div class='progress-fill' id='progFill'></div></div>"
                      "<div class='progress-label' id='progText'>Flashing Firmware: 0%</div>"
                      "</form>"
                      "<div class='footer' style='margin-top:22px'>"
                      "<div>Enter the admin key supplied with this controller.</div>"
                      "<a href='/' class='link-action'>&larr; Return to Wi-Fi Setup Portal</a>"
                      "</div>"
                      "</div>"
                      "<script>"
                      "function showError(msg) {"
                      "  document.getElementById('otaErrBox').style.display = 'block';"
                      "  document.getElementById('otaErrMsg').innerText = msg;"
                      "}"
                      "function clearError() {"
                      "  document.getElementById('otaErrBox').style.display = 'none';"
                      "}"
                      "function onFileSelected(input) {"
                      "  clearError();"
                      "  if(input.files && input.files[0]) {"
                      "    var f = input.files[0];"
                      "    if(!f.name.toLowerCase().endsWith('.bin')) {"
                      "      showError('Invalid file type! Please select an ESP32 compiled binary (.bin).');"
                      "      input.value = '';"
                      "      document.getElementById('fileLabel').innerText = 'Choose Compiled Binary (.bin)';"
                      "      document.getElementById('fileSub').innerText = 'Target Partition: 1.9MB Minimal SPIFFS';"
                      "      return;"
                      "    }"
                      "    if(f.size > 1966080) {"
                      "      showError('File exceeds partition limit! (' + (f.size/1024/1024).toFixed(2) + ' MB > 1.90 MB). Use min_spiffs partition.');"
                      "    }"
                      "    document.getElementById('fileLabel').innerText = f.name;"
                      "    document.getElementById('fileSub').innerText = 'Size: ' + (f.size/1024).toFixed(1) + ' KB';"
                      "  }"
                      "}"
                      "document.getElementById('otaForm').onsubmit = function(e) {"
                      "  var file = document.getElementById('binFile').files[0];"
                      "  if(!file) { showError('Please select a .bin file before starting flash.'); e.preventDefault(); return false; }"
                      "  clearError();"
                      "  document.getElementById('flashBtn').style.display = 'none';"
                      "  document.getElementById('progWrap').style.display = 'block';"
                      "  document.getElementById('progText').style.display = 'block';"
                      "  var xhr = new XMLHttpRequest();"
                      "  xhr.open('POST', '/update', true);"
                      "  xhr.upload.onprogress = function(evt) {"
                      "    if(evt.lengthComputable) {"
                      "      var pct = Math.round((evt.loaded / evt.total) * 100);"
                      "      document.getElementById('progFill').style.width = pct + '%';"
                      "      document.getElementById('progText').innerText = 'Flashing Controller: ' + pct + '%';"
                      "    }"
                      "  };"
                      "  xhr.onload = function() {"
                      "    if(xhr.status == 200) {"
                      "      document.body.innerHTML = `<div class=\"card\" style=\"text-align:center\">" + String(FPSTR(SVG_CHECK)) + "<h2 style=\"color:#1a5632;margin-bottom:6px\">Update Complete!</h2><p style=\"font-size:13px;color:#4b5563;margin-bottom:14px\">Controller successfully flashed and verified. Rebooting into new firmware...</p><div style=\"font-size:28px;font-weight:800;color:#1a5632\" id=\"cd\">15</div><p style=\"font-size:11px;color:#9ca3af;margin-top:4px\">seconds until device reboots</p></div>`;"
                      "      var c = 15; var t = setInterval(function() { c--; if(c>=0) document.getElementById('cd').innerText = c; if(c<=0) { clearInterval(t); window.location.href = '/'; } }, 1000);"
                      "    } else {"
                      "      showError(xhr.responseText || 'OTA flash write failed. Please check network connection and retry.');"
                      "      document.getElementById('flashBtn').style.display = 'block';"
                      "      document.getElementById('flashBtn').innerText = 'RETRY WIRELESS FLASH';"
                      "      document.getElementById('progWrap').style.display = 'none';"
                      "      document.getElementById('progText').style.display = 'none';"
                      "    }"
                      "  };"
                      "  xhr.onerror = function() {"
                      "    showError('Network connection lost during upload. Reconnect to SoftAP and try again.');"
                      "    document.getElementById('flashBtn').style.display = 'block';"
                      "    document.getElementById('flashBtn').innerText = 'RETRY WIRELESS FLASH';"
                      "    document.getElementById('progWrap').style.display = 'none';"
                      "    document.getElementById('progText').style.display = 'none';"
                      "  };"
                      "  var formData = new FormData(document.getElementById('otaForm'));"
                      "  xhr.send(formData);"
                      "  e.preventDefault();"
                      "};"
                      "</script>"
                      "</body></html>";
        server.send(200, "text/html", html);
    });

    server.on("/update", HTTP_POST, []() {
        if (!server.authenticate("admin", getPerBoxSecret().c_str())) {
            return server.requestAuthentication();
        }
        server.sendHeader("Connection", "close");
        if (g_otaValidationFailed) {
            server.send(400, "text/plain", "FIRMWARE REJECTED: " + g_otaCustomError);
            return;
        }
        if (Update.hasError()) {
            String errMsg = "OTA FLASH FAILED: ";
            uint8_t err = Update.getError();
            if (err == UPDATE_ERROR_SPACE) errMsg += "File size exceeds partition limit (max 1.9MB).";
            else if (err == UPDATE_ERROR_MAGIC_BYTE) errMsg += "Invalid file. Not an ESP32 firmware binary (.bin).";
            else if (err == UPDATE_ERROR_WRITE) errMsg += "Flash write error. Hardware write timed out.";
            else if (err == UPDATE_ERROR_ERASE) errMsg += "Flash partition erase error.";
            else if (err == UPDATE_ERROR_SIZE) errMsg += "Firmware file size truncated.";
            else errMsg += "Flash verification error (Code " + String(err) + ").";
            server.send(500, "text/plain", errMsg);
        } else {
            server.send(200, "text/plain", "OK");
            delay(1000);
            ESP.restart();
        }
    }, []() {
        HTTPUpload& upload = server.upload();
        if (upload.status == UPLOAD_FILE_START) {
            g_otaValidationFailed = false;
            g_otaCustomError = "";
            Serial.printf("[OTA] Starting firmware flash: %s\n", upload.filename.c_str());
            if (!Update.begin(UPDATE_SIZE_UNKNOWN)) {
                Update.printError(Serial);
            }
        } else if (upload.status == UPLOAD_FILE_WRITE) {
            feedWatchdog();
            if (g_otaValidationFailed) return;

            // Pre-flight binary header and architecture inspection
            if (upload.totalSize == 0 && upload.currentSize >= 16) {
                uint8_t magic = upload.buf[0];
                if (magic != 0xE9) {
                    g_otaValidationFailed = true;
                    g_otaCustomError = "Invalid file header! File is not an ESP firmware binary (Magic byte: 0x" + String(magic, HEX) + " != 0xE9).";
                    Update.abort();
                    return;
                }

                uint16_t chipId = upload.buf[12] | (upload.buf[13] << 8);
#if defined(CONFIG_IDF_TARGET_ESP32C6) || defined(ARDUINO_DFROBOT_FIREBEETLE_2_ESP32C6)
                if (chipId != 13) { // 13 = ESP32-C6
                    g_otaValidationFailed = true;
                    String chipName = (chipId == 0) ? "ESP32 (Classic/CAM)" : (chipId == 9 ? "ESP32-S3" : (chipId == 5 ? "ESP32-C3" : "Unknown Chip"));
                    g_otaCustomError = "Board Architecture Mismatch! This hardware is an ESP32-C6 (RISC-V), but the binary was compiled for " + chipName + " (Chip ID " + String(chipId) + "). Please re-compile with 'DFRobot FireBeetle 2 ESP32-C6' selected.";
                    Update.abort();
                    return;
                }
#elif !defined(CONFIG_IDF_TARGET_ESP32C6)
                if (chipId != 0) { // 0 = ESP32 Classic / ESP32-CAM
                    g_otaValidationFailed = true;
                    String chipName = (chipId == 13) ? "ESP32-C6" : (chipId == 9 ? "ESP32-S3" : (chipId == 5 ? "ESP32-C3" : "Unknown Chip"));
                    g_otaCustomError = "Board Architecture Mismatch! This hardware is an AI-Thinker ESP32-CAM (Xtensa LX6), but the binary was compiled for " + chipName + " (Chip ID " + String(chipId) + "). Please re-compile with 'AI Thinker ESP32-CAM' selected.";
                    Update.abort();
                    return;
                }
#endif
            }

            if (Update.write(upload.buf, upload.currentSize) != upload.currentSize) {
                Update.printError(Serial);
            }
        } else if (upload.status == UPLOAD_FILE_END) {
            feedWatchdog();
            if (g_otaValidationFailed) return;
            if (Update.end(true)) {
                Serial.printf("[OTA] Flash success: %u bytes\n", upload.totalSize);
            } else {
                Update.printError(Serial);
            }
        }
    });
}

// -------------------------------------------------------------
// 3. Enterprise Captive Provisioning Portal ("WiFiManager")
// -------------------------------------------------------------
static void registerWiFiSaveRoute() {
    server.on("/wifi", HTTP_GET, []() {
        server.send(200, "text/html", "<!doctype html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'>"
            "<title>Wi-Fi settings</title></head><body><h2>Wi-Fi settings</h2>"
            "<form method='post' action='/save'><label>Network name <input name='ssid' required></label><br>"
            "<label>Enterprise email (leave blank for home Wi-Fi) <input name='eap_email'></label><br>"
            "<label>Password <input name='eap_pass' type='password'></label><br>"
            "<button>Save and connect</button></form></body></html>");
    });
    server.on("/save", HTTP_POST, []() {
        String ssid = server.arg("ssid"); ssid.trim();
        String email = server.arg("eap_email"); email.trim();
        if (ssid.isEmpty() || ssid.length() > 32) {
            server.send(400, "text/plain", "Enter a network name of 1 to 32 characters.");
            return;
        }
        if (!email.isEmpty() && email.indexOf('@') < 0) email += "@myemeris.edu.za";
        prefs.begin("smarthydro", false);
        prefs.putString("ssid", ssid);
        prefs.putString("eap_id", email);
        prefs.putString("eap_user", email);
        prefs.putString("eap_pass", server.arg("eap_pass"));
        prefs.putInt("wifi_err", 0);
        prefs.end();
        server.send(200, "text/html", "<!doctype html><html><body><h2>Credentials saved</h2>"
            "<p>The node is connecting to your router. The hotspot will close. Relay timers keep running.</p>"
            "<p>If the cloud is still unreachable after three minutes, the node returns to LoRa. Hold BOOT to reopen setup.</p></body></html>");
        requestConnectivityMode(1);
    });
}

static void startEnterpriseSetupPortal(const char* nodeName) {
    Serial.printf("[Portal] Starting Enterprise Setup Portal AP: %s\n", nodeName);
    
    // Explicitly shut down station driver to stop NO_AP_FOUND / STA_LEAVING retries safely
    safeEnterpriseDisable();
    safeWiFiDisconnect();
    delay(50);

    IPAddress apIP(192, 168, 4, 1);
    IPAddress netMsk(255, 255, 255, 0);
    WiFi.mode(WIFI_AP_STA);
    delay(50);
    WiFi.softAPConfig(apIP, apIP, netMsk);
    WiFi.softAP(nodeName);

    dnsServer.setErrorReplyCode(DNSReplyCode::NoError);
    dnsServer.start(DNS_PORT, "*", apIP);
    isCaptivePortalActive = true;

    auto handleCaptiveRedirect = []() {
        server.sendHeader("Location", "http://192.168.4.1/", true);
        server.send(302, "text/html", "<!DOCTYPE html><html><head><meta http-equiv='refresh' content='0;url=http://192.168.4.1/'><script>window.location.replace('http://192.168.4.1/');</script></head><body>Redirecting to SmartHydro Setup Portal...</body></html>");
    };

    server.on("/generate_204", HTTP_GET, handleCaptiveRedirect);
    server.on("/gen_204", HTTP_GET, handleCaptiveRedirect);
    server.on("/hotspot-detect.html", HTTP_GET, handleCaptiveRedirect);
    server.on("/canonical.html", HTTP_GET, handleCaptiveRedirect);
    server.on("/ncsi.txt", HTTP_GET, handleCaptiveRedirect);
    server.on("/connecttest.txt", HTTP_GET, handleCaptiveRedirect);
    server.on("/success.txt", HTTP_GET, handleCaptiveRedirect);
    server.on("/success.html", HTTP_GET, handleCaptiveRedirect);
    server.on("/library/test/success.html", HTTP_GET, handleCaptiveRedirect);
    server.on("/mobile/status.php", HTTP_GET, handleCaptiveRedirect);
    server.on("/check_network_status", HTTP_GET, handleCaptiveRedirect);
    server.on("/kindle-wifi/wifiredirect.html", HTTP_GET, handleCaptiveRedirect);
    server.onNotFound(handleCaptiveRedirect);

    // Serve Provisioning Web UI
    server.on("/", HTTP_GET, []() {
        prefs.begin("smarthydro", false);
        int wifiErr = prefs.isKey("wifi_err") ? prefs.getInt("wifi_err", 0) : 0;
        String errSsid = prefs.isKey("err_ssid") ? prefs.getString("err_ssid", "") : "";
        String savedSsid = prefs.isKey("ssid") ? prefs.getString("ssid", "Edu-WiFi") : "Edu-WiFi";
        String savedEmail = prefs.isKey("eap_user") ? prefs.getString("eap_user", "") : "";
        prefs.end();

        String manualBanner = "";
        if (g_isManualSoftApTriggered) {
            manualBanner = "<div class='alert-box' style='background:#eff6ff;border:1px solid #bfdbfe;color:#1e40af;'>"
                           "<div class='alert-title'><svg viewBox='0 0 24 24' width='16' height='16' stroke='currentColor' stroke-width='2' fill='none' style='margin-right:6px;vertical-align:-2px;'><circle cx='12' cy='12' r='10'></circle><line x1='12' y1='8' x2='12' y2='12'></line><line x1='12' y1='16' x2='12.01' y2='16'></line></svg> Local SoftAP Maintenance Mode Active</div>"
                           "<div class='alert-desc'>BOOT button was held for 3 seconds. The node is running in local maintenance mode. You can re-configure network credentials below or proceed directly to <a href='/update' style='color:#1d4ed8;font-weight:700;'>Web OTA Wireless Firmware Update &rarr;</a></div>"
                           "</div>";
        }

        String errorBanner = "";
        if (wifiErr != 0) {
            String title = "Connection Failed";
            String desc = "Unable to connect to the specified Wi-Fi network.";

            if (wifiErr == 201) {
                title = "Network Not Found";
                desc = "Could not locate SSID <strong>'" + (errSsid.isEmpty() ? savedSsid : errSsid) + "'</strong>. Check spelling (SSIDs are case-sensitive) or ensure the node is in range.";
            } else if (wifiErr == 202 || wifiErr == 204) {
                title = "Authentication Failed";
                desc = "Incorrect Wi-Fi password or student ID for <strong>'" + (errSsid.isEmpty() ? savedSsid : errSsid) + "'</strong>. Please re-enter your credentials.";
            } else if (wifiErr == 999) {
                title = "Connection Timed Out";
                desc = "Network did not assign an IP address within 12 seconds. The gateway may be congested.";
            }

            errorBanner = "<div class='alert-box alert-error'>"
                          "<div class='alert-title'>" + String(FPSTR(SVG_ALERT)) + " " + title + "</div>"
                          "<div class='alert-desc'>" + desc + "</div>"
                          "</div>";
        }

        String html = "<!DOCTYPE html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'>"
                      "<title>Smart Hydro - WiFi Manager</title>" + getSharedStyles() + "</head><body>"
                      "<div class='card'>"
                      "<div class='header'>"
                      "<img class='brand-logo' src='" + String(FPSTR(SMARTHYDRO_LOGO_B64)) + "' alt='SmartHydro Logo'>"
                      "<div class='brand-title'>Smart Hydro</div>"
                      "<div class='brand-slogan'>Farming Made Smart</div>"
                      "<span class='node-badge'>" + String(NODE_BADGE_NAME) + "</span>"
                      "</div>" + manualBanner + errorBanner +
                      "<form method='POST' action='/save'>"
                      "<label>Network SSID:</label><input type='text' name='ssid' value='" + savedSsid + "' placeholder='e.g. Edu-WiFi or Home WiFi' autocomplete='off' required>"
                      "<label>Student Email or University ID (Leave blank if on Home Wi-Fi):</label><input type='text' id='eap_email' name='eap_email' value='" + savedEmail + "' placeholder='e.g. st10435031@myemeris.edu.za' autocomplete='off'>"
                      "<label>Wi-Fi Password:</label>"
                      "<div style='position:relative;display:flex;align-items:center;'>"
                      "<input type='password' id='eap_pass' name='eap_pass' placeholder='Enter Wi-Fi password' autocomplete='new-password' required autofocus style='padding-right:42px;'>"
                      "<button type='button' onclick='togglePassword()' style='position:absolute;right:8px;background:none;border:none;cursor:pointer;color:#6b7280;padding:6px;display:flex;align-items:center;'>"
                      "<svg id='eyeIcon' viewBox='0 0 24 24' width='20' height='20' stroke='currentColor' stroke-width='2' fill='none' stroke-linecap='round' stroke-linejoin='round'>"
                      "<path d='M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z'></path><circle cx='12' cy='12' r='3'></circle>"
                      "</svg>"
                      "</button>"
                      "</div>"
                      "<button type='submit' class='btn-primary'>SAVE & CONNECT TO CLOUD</button>"
                      "</form>"
                      "<hr class='divider'>"
                      "<div class='offline-sec'>"
                      "<p>Deploying in field without campus Wi-Fi?</p>"
                      "<form method='POST' action='/mode/offline'>"
                      "<button type='submit' class='btn-offline'>OPERATE IN OFFLINE FARM MODE (OPEN WI-FI)</button>"
                      "</form>"
                      "</div>"
                      "<div class='footer'>"
                      "<div>Enter the admin key supplied with this controller.</div>"
                      "<a href='/update' class='link-action'>Web OTA Wireless Firmware Update &rarr;</a>"
                      "</div>"
                      "</div>"
                      "<script>"
                      "function togglePassword() {"
                      "  var p = document.getElementById('eap_pass');"
                      "  var icon = document.getElementById('eyeIcon');"
                      "  if (p.type === 'password') {"
                      "    p.type = 'text';"
                      "    icon.innerHTML = \"<path d='M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24'></path><line x1='1' y1='1' x2='23' y2='23'></line>\";"
                      "  } else {"
                      "    p.type = 'password';"
                      "    icon.innerHTML = \"<path d='M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z'></path><circle cx='12' cy='12' r='3'></circle>\";"
                      "  }"
                      "}"
                      "</script>"
                      "</body></html>";
        server.send(200, "text/html", html);
    });

    // Save form action
    registerWiFiSaveRoute();

    // Switch to Offline mode action
    server.on("/mode/offline", HTTP_POST, []() {
        prefs.begin("smarthydro", false);
        prefs.putBool("is_offline", true); prefs.putInt("op_mode", 2); prefs.putInt("pref_mode", 2); prefs.putInt("mode_reason", 0);
        prefs.putInt("wifi_err", 0);
        prefs.end();
        server.send(200, "text/html", "<!DOCTYPE html><html><head>" + getSharedStyles() + "</head><body><div class='card' style='text-align:center'>" + String(FPSTR(SVG_PLANT)) + "<h3 style='color:#1a5632;margin-bottom:8px'>Configured for Offline Mode</h3><p style='font-size:13px;color:#6b7280'>Switching to the local farm hotspot...</p></div></body></html>");
        requestConnectivityMode(2);
    });

    setupWebOtaHandlers();
    server.begin();
    Serial.printf("[Portal] WebServer started at http://%s\n", WiFi.softAPIP().toString().c_str());
    Serial.printf("[Portal] Device Admin Key: %s\n", getPerBoxSecret().c_str());
}

// -------------------------------------------------------------
// 4. Offline SoftAP & Data-Mule RoomDB Server
// -------------------------------------------------------------
static void startOfflineSoftAP(const char* apName) {
    Serial.printf("[Offline] Starting Hydroponic Offline SoftAP: %s\n", apName);
    
    safeEnterpriseDisable();
    safeWiFiDisconnect();
    delay(50);

    IPAddress apIP(192, 168, 4, 1);
    IPAddress netMsk(255, 255, 255, 0);
    WiFi.mode(WIFI_AP_STA);
    delay(50);
    WiFi.softAPConfig(apIP, apIP, netMsk);
    WiFi.softAP(apName);

    dnsServer.setErrorReplyCode(DNSReplyCode::NoError);
    dnsServer.start(DNS_PORT, "*", apIP);
    isCaptivePortalActive = true;

    static bool routesReady = false;
    if (routesReady) { server.begin(); return; }
    routesReady = true;
    registerWiFiSaveRoute();

    auto handleCaptiveRedirect = []() {
        server.sendHeader("Location", "http://192.168.4.1/", true);
        server.send(302, "text/html", "<!DOCTYPE html><html><head><meta http-equiv='refresh' content='0;url=http://192.168.4.1/'><script>window.location.replace('http://192.168.4.1/');</script></head><body>Redirecting to SmartHydro Offline Dashboard...</body></html>");
    };

    server.on("/generate_204", HTTP_GET, handleCaptiveRedirect);
    server.on("/gen_204", HTTP_GET, handleCaptiveRedirect);
    server.on("/hotspot-detect.html", HTTP_GET, handleCaptiveRedirect);
    server.on("/canonical.html", HTTP_GET, handleCaptiveRedirect);
    server.on("/ncsi.txt", HTTP_GET, handleCaptiveRedirect);
    server.on("/connecttest.txt", HTTP_GET, handleCaptiveRedirect);
    server.on("/success.txt", HTTP_GET, handleCaptiveRedirect);
    server.on("/success.html", HTTP_GET, handleCaptiveRedirect);
    server.on("/library/test/success.html", HTTP_GET, handleCaptiveRedirect);
    server.on("/mobile/status.php", HTTP_GET, handleCaptiveRedirect);
    server.on("/check_network_status", HTTP_GET, handleCaptiveRedirect);
    server.on("/kindle-wifi/wifiredirect.html", HTTP_GET, handleCaptiveRedirect);
    server.onNotFound(handleCaptiveRedirect);

    // Offline Mode Main Dashboard
    server.on("/", HTTP_GET, []() {
        String html = "<!DOCTYPE html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'>"
                      "<title>Smart Hydro - Offline Dashboard</title>" + getSharedStyles() + "</head><body>"
                      "<div class='card'>"
                      "<div class='header'>"
                      "<img class='brand-logo' src='" + String(FPSTR(SMARTHYDRO_LOGO_B64)) + "' alt='SmartHydro Logo'>"
                      "<div class='brand-title'>Smart Hydro</div>"
                      "<div class='brand-slogan'>Farming Made Smart</div>"
                      "<span class='node-badge' style='background:#fef3c7;color:#92400e;border-color:#fde68a;'>OFFLINE FARM MODE (OPEN WI-FI)</span>"
                      "</div>"
                      
                      "<div class='alert-box' style='background:#f0fdf4;border:1px solid #bbf7d0;color:#166534;'>"
                      "<div class='alert-title'>" + String(FPSTR(SVG_PLANT)) + " Standalone Edge Operation</div>"
                      "<div class='alert-desc'>This tower is operating autonomously. Sensor telemetry is logged locally to memory ready for Android Data-Mule harvesting.</div>"
                      "</div>"
                      
                      "<div style='margin:16px 0;'>"
                      "<h4 style='font-size:12px;color:#374151;margin-bottom:8px;font-weight:700;text-transform:uppercase;letter-spacing:0.5px;'>Live Sensor Telemetry</h4>"
                      "<div style='display:grid;grid-template-columns:1fr 1fr;gap:8px;font-size:12px;text-align:center;'>"
                      "<div style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:10px;'><div style='color:#6b7280;font-size:11px;'>pH Level</div><div style='font-size:18px;font-weight:800;color:#1a5632;margin-top:2px;'>" + String(currentTelemetry.ph, 2) + "</div></div>"
                      "<div style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:10px;'><div style='color:#6b7280;font-size:11px;'>EC (mS/cm)</div><div style='font-size:18px;font-weight:800;color:#1a5632;margin-top:2px;'>" + String(currentTelemetry.ec, 2) + "</div></div>"
                      "<div style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:10px;'><div style='color:#6b7280;font-size:11px;'>Temperature</div><div style='font-size:18px;font-weight:800;color:#1a5632;margin-top:2px;'>" + String(currentTelemetry.temp, 1) + " &deg;C</div></div>"
                      "<div style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:10px;'><div style='color:#6b7280;font-size:11px;'>Humidity</div><div style='font-size:18px;font-weight:800;color:#1a5632;margin-top:2px;'>" + String(currentTelemetry.hum, 1) + " %</div></div>"
                      "</div>"
                      "</div>"
                      
                      "<div class='offline-sec' style='margin-top:16px;'>"
                      "<h4 style='color:#1a5632;font-size:14px;margin-bottom:4px;font-weight:700;'>SmartHydro Android Companion</h4>"
                      "<p style='font-size:12px;color:#6b7280;margin-bottom:10px;'>Sync historical logs into your local RoomDB database with our mobile app.</p>"
                      "<a href='/download/android-app' class='btn-primary' style='display:block;text-align:center;text-decoration:none;padding:11px 0;'>DOWNLOAD COMPANION APP (.APK)</a>"
                      "<div style='font-size:11px;color:#9ca3af;margin-top:6px;'>Build v1.0.0-release &bull; Offline Data-Mule</div>"
                      "</div>"

                      "<hr class='divider'>"
                      "<form method='POST' action='/api/mode/online'>"
                      "<label style='margin-top:0;'>Device Admin Key:</label>"
                      "<input type='password' name='admin_key' placeholder='Enter admin key (e.g. SH-XXXXXX)' required>"
                      "<button type='submit' class='btn-offline' style='margin-top:10px;'>SWITCH BACK TO ONLINE CLOUD MODE</button>"
                      "</form>"

                      "<div class='footer' style='margin-top:18px;'>"
                      "<div>Enter the admin key supplied with this controller.</div>"
                      "<a href='/update' class='link-action'>Web OTA Wireless Firmware Update &rarr;</a>"
                      "</div>"
                      "</div></body></html>";
        html.replace("</body>", "<p><a href='/wifi'>Wi-Fi settings</a></p></body>");
        server.send(200, "text/html", html);
    });

    server.on("/download/android-app", HTTP_GET, []() {
        String html = "<!DOCTYPE html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'>"
                      "<title>SmartHydro Companion APK</title>" + getSharedStyles() + "</head><body>"
                      "<div class='card' style='text-align:center;'>"
                      "<div class='header'>"
                      "<img class='brand-logo' src='" + String(FPSTR(SMARTHYDRO_LOGO_B64)) + "' alt='SmartHydro Logo'>"
                      "<h2 style='color:#1a5632;margin-bottom:4px;'>Android Companion App</h2>"
                      "<p style='font-size:13px;color:#6b7280;'>SmartHydro Mobile APK Asset Slot</p>"
                      "</div>"
                      "<div class='alert-box' style='background:#f0fdf4;border:1px solid #bbf7d0;color:#166534;text-align:left;'>"
                      "<div class='alert-title'>Ready for Android APK Deployment</div>"
                      "<div class='alert-desc'>When deploying your production release APK, link this endpoint to your release asset URL or host it in the controller's flash storage.</div>"
                      "</div>"
                      "<p style='font-size:12px;color:#4b5563;margin-bottom:16px;'>Version: <strong>v1.0.0 (Data-Mule Edition)</strong><br>Target: Android 8.0+ (API 26-34)</p>"
                      "<a href='/' class='btn-primary' style='display:block;text-decoration:none;padding:11px 0;'>&larr; Return to Offline Dashboard</a>"
                      "</div></body></html>";
        server.send(200, "text/html", html);
    });

    server.on("/api/telemetry/live", HTTP_GET, []() {
        String json = "{";
        json += "\"ph\":" + String(currentTelemetry.ph, 2) + ",";
        json += "\"ec\":" + String(currentTelemetry.ec, 2) + ",";
        json += "\"temperature\":" + String(currentTelemetry.temp, 2) + ",";
        json += "\"humidity\":" + String(currentTelemetry.hum, 2) + ",";
        json += "\"light\":" + String(currentTelemetry.lux, 1) + ",";
        json += "\"waterLevelRaw\":" + String(currentTelemetry.waterRaw) + ",";
        json += "\"waterLevelDetected\":" + String(currentTelemetry.waterOk ? "true" : "false") + ",";
        json += "\"mlPh\":" + String(currentTelemetry.mlPh) + ",";
        json += "\"mlEc\":" + String(currentTelemetry.mlEc) + ",";
        json += "\"mlTemp\":" + String(currentTelemetry.mlTemp) + ",";
        json += "\"mlHumidity\":" + String(currentTelemetry.mlHum) + ",";
        json += "\"relays\":" + localRelaysJson();
        json += "}";
        server.send(200, "application/json", json);
    });

    server.on("/api/sync/bundle", HTTP_GET, []() {
        String json = "{\"records\":[";
        for (int i = 0; i < cacheCount; i++) {
            if (i > 0) json += ",";
            json += "{";
            json += "\"timestamp\":" + String(offlineCache[i].timestamp) + ",";
            json += "\"ph\":" + String(offlineCache[i].ph, 2) + ",";
            json += "\"ec\":" + String(offlineCache[i].ec, 2) + ",";
            json += "\"temp\":" + String(offlineCache[i].temp, 2) + ",";
            json += "\"hum\":" + String(offlineCache[i].hum, 2) + ",";
            json += "\"lux\":" + String(offlineCache[i].lux, 1) + ",";
            json += "\"waterRaw\":" + String(offlineCache[i].waterRaw) + ",";
            json += "\"waterOk\":" + String(offlineCache[i].waterOk ? "true" : "false");
            json += "}";
        }
        json += "]}";
        server.send(200, "application/json", json);
    });

    server.on("/api/mode/online", HTTP_POST, []() {
        bool authorized = false;
        if (server.hasArg("admin_key") && server.arg("admin_key") == getPerBoxSecret()) {
            authorized = true;
        } else if (server.authenticate("admin", getPerBoxSecret().c_str())) {
            authorized = true;
        }

        if (!authorized) {
            server.send(401, "text/html", "<!DOCTYPE html><html><head>" + getSharedStyles() + "</head><body><div class='card' style='text-align:center'><h3 style='color:#991b1b;margin-bottom:8px'>Invalid Admin Key!</h3><p style='font-size:13px;color:#6b7280'>The admin key provided does not match this hardware node.</p><a href='/' class='btn-primary' style='display:inline-block;margin-top:14px;text-decoration:none;padding:10px 20px;'>Try Again</a></div></body></html>");
            return;
        }

        prefs.begin("smarthydro", false);
        prefs.putBool("is_offline", false); prefs.putInt("op_mode", 1); prefs.putInt("pref_mode", 1); prefs.putInt("mode_reason", 0);
        prefs.putInt("op_mode", 1); // MODE_WIFI_CLOUD (Canonical 1)
        prefs.putInt("wifi_err", 0);
        prefs.end();
        server.send(200, "text/html", "<!DOCTYPE html><html><head>" + getSharedStyles() + "</head><body><div class='card' style='text-align:center'>" + String(FPSTR(SVG_SYNC)) + "<h3 style='color:#1a5632;margin-bottom:8px'>Switching to Online Mode...</h3><p style='font-size:13px;color:#6b7280'>Connecting to cloud station mode...</p></div></body></html>");
        requestConnectivityMode(1);
    });

    server.on("/api/mode/lora", HTTP_POST, []() {
        prefs.begin("smarthydro", false);
        prefs.putBool("is_offline", false); prefs.putInt("op_mode", 1); prefs.putInt("pref_mode", 1); prefs.putInt("mode_reason", 0);
        prefs.putInt("op_mode", 0); prefs.putInt("pref_mode", 0); prefs.putInt("mode_reason", 0); // MODE_LORA_BLE
        prefs.end();
        server.send(200, "application/json", "{\"ok\":true,\"mode\":\"lora\"}");
        requestConnectivityMode(0);
    });

    registerLocalControlRoutes();

    setupWebOtaHandlers();
    server.begin();
    Serial.printf("[Offline] Telemetry Server active at http://%s\n", WiFi.softAPIP().toString().c_str());
    Serial.printf("[Offline] Admin Key: %s\n", getPerBoxSecret().c_str());
}

// -------------------------------------------------------------
// 5. Unified Server Loop Handler (DNS Interception + HTTP)
// -------------------------------------------------------------
#ifndef MANUAL_TRIGGER_PIN
  #define MANUAL_TRIGGER_PIN 0
#endif

#ifndef SETUP_PORTAL_NAME
  #define SETUP_PORTAL_NAME "SmartHydro-Fodder-Setup"
#endif

extern bool isOfflineMode;
extern bool portalActive;

static void handleProvisioningLoop() {
    if (isCaptivePortalActive) {
        dnsServer.processNextRequest();
    }
    server.handleClient();
}
