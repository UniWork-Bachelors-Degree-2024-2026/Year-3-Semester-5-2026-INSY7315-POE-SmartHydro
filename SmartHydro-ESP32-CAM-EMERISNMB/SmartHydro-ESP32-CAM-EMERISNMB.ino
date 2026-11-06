/*
  ==================================================================================================
  SMART HYDROPONIC CONTROLLER - ESP32-CAM ON-DEMAND HIGH-RES SNAPSHOT NODE
  ==================================================================================================
  Target MCU:  AI-Thinker ESP32-CAM (ESP32-D0WDQ6 + OV2640 + 4MB PSRAM)
  Deployment:  Canopy Plant Health Imaging Node (EMERISNMB)
  Architecture:
    - Pure On-Demand Snapshot Architecture (Zero Stream / Zero FB-OVF)
    - Maximum Clarity XGA (1600x1200) Crop Health Inspection Photos
    - Quiescent Sensor Power Gating: OV2640 completely powered DOWN via GPIO 32 (PWDN HIGH = 0 mA)
    - Sub-Second SignalR Trigger via WebSocket (ActuatorHub) + 3s REST Polling Fallback
    - User-requested and daily snapshots, with sensor standby between captures
    - Over-The-Air (OTA) Web Firmware Update Engine (/update) on Port 80
  ==================================================================================================
*/

#include <Arduino.h>
#include <WiFi.h>
#include <esp_mac.h>
#include <esp_wifi.h>
#include <HTTPClient.h>
#include <WiFiClientSecure.h>
#include <WebSocketsClient_Generic.h>
#include <ArduinoJson.h>
#include "esp_camera.h"

#include "CameraConfig.h"
#include "Provisioning.h"

// DigiCert Global Root G2 (Root CA for Azure App Services *.azurewebsites.net)
static const char* AZURE_ROOT_CA =
"-----BEGIN CERTIFICATE-----\n"
"MIIDjjCCAnagAwIBAgIQAzrx5qcRqaC7KGSxHQn65TANBgkqhkiG9w0BAQsFADBh\n"
"MQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMRkwFwYDVQQLExB3\n"
"d3cuZGlnaWNlcnQuY29tMSAwHgYDVQQDExdEaWdpQ2VydCBHbG9iYWwgUm9vdCBH\n"
"MgwHhcNMTMwODAxMTIwMDAwWhcNMzgwMTE1MTIwMDAwWjBhMQswCQYDVQQGEwJV\n"
"UzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMRkwFwYDVQQLExB3d3cuZGlnaWNlcnQu\n"
"Y29tMSAwHgYDVQQDExdEaWdpQ2VydCBHbG9iYWwgUm9vdCBHMg==\n"
"-----END CERTIFICATE-----\n";

static String deviceMac = "";
static bool isFlashOn = false;
static unsigned long lastPollTime = 0;
static const unsigned long POLL_INTERVAL_MS = 4000; // Poll CameraCommands every 4s as reliable companion to SignalR
static const unsigned long DAILY_CAPTURE_INTERVAL_MS = 24UL * 60UL * 60UL * 1000UL;
static const unsigned long DAILY_CAPTURE_RETRY_MS = 5UL * 60UL * 1000UL;
static unsigned long lastDailyCapture = 0;
static unsigned long lastDailyAttempt = 0;

// Preferred high-clarity resolution (SXGA 1280x1024 when PSRAM is present, SVGA 800x600 fallback)
static framesize_t getPreferredFrameSize() {
    return psramFound() ? FRAMESIZE_SXGA : FRAMESIZE_SVGA;
}

static const char* getFrameSizeLabel(framesize_t fs) {
    switch (fs) {
        case FRAMESIZE_UXGA: return "UXGA 1600x1200";
        case FRAMESIZE_SXGA: return "SXGA 1280x1024";
        case FRAMESIZE_XGA:  return "XGA 1024x768";
        case FRAMESIZE_SVGA: return "SVGA 800x600";
        default:             return "HIGH-RES";
    }
}

// -------------------------------------------------------------
// FORWARD DECLARATIONS
// -------------------------------------------------------------
static bool initCamera(framesize_t frameSize = FRAMESIZE_SXGA);
static void stopCamera();
static bool captureAndUploadSnapshot(framesize_t targetResolution = FRAMESIZE_SXGA, bool useFlash = false, int structureId = 0);
static void initCameraWebSocket();
static void syncWithCloud();

// Global asynchronous capture trigger flags (decouples capture from WebSocket event callback)
static volatile bool g_pendingCaptureRequested = false;
static volatile bool g_pendingCaptureFlash = false;
static volatile int g_pendingCaptureStructureId = 0;

// -------------------------------------------------------------
// CAMERA HARDWARE INITIALIZATION & SENSOR STANDBY MANAGEMENT
// -------------------------------------------------------------
static bool isCameraInitialized = false;

// CameraWebServer LED Flash PWM Implementation (prevents 300mA brownout voltage drop)
static void setupLedFlash() {
    ledcAttach(FLASH_LED_PIN, 5000, 8);
    ledcWrite(FLASH_LED_PIN, 0);
}

static void setFlash(bool on) {
    if (on) {
        // Controlled duty cycle (45 / 255) delivers crisp illumination without dropping 3.3V rail
        ledcWrite(FLASH_LED_PIN, 45);
    } else {
        ledcWrite(FLASH_LED_PIN, 0);
    }
}

static void applyOv2640ImageQualityTuning(sensor_t* s, framesize_t targetSize) {
    if (s == NULL) return;

    s->set_framesize(s, targetSize);
    s->set_quality(s, psramFound() ? 8 : 10); // Lower number = higher JPEG quality (8 = ultra-crisp)

    // OV2640 ISP Image Enhancement Pipeline for Hydroponic Canopy Inspection
    s->set_brightness(s, 0);                  // Balanced brightness (-2 to 2)
    s->set_contrast(s, 1);                    // Slightly boosted contrast for leaf vein definition
    s->set_saturation(s, 1);                  // True-to-life chlorophyll green saturation
    s->set_sharpness(s, 2);                   // Crisp edge sharpness
    s->set_denoise(s, 1);                     // Hardware sensor denoise enabled
    s->set_special_effect(s, 0);              // No color tint effects
    s->set_whitebal(s, 1);                    // Auto White Balance (AWB) ON
    s->set_awb_gain(s, 1);                    // AWB Gain DSP ON
    s->set_wb_mode(s, 0);                     // Auto WB mode (adapts to grow lights or daylight)
    s->set_exposure_ctrl(s, 1);               // Auto Exposure Control (AEC) ON
    s->set_aec2(s, 1);                        // Night/Indoor AEC DSP ON
    s->set_ae_level(s, 0);                    // Neutral AE target level
    s->set_gain_ctrl(s, 1);                   // Auto Gain Control (AGC) ON
    s->set_gainceiling(s, (gainceiling_t)2);  // Cap ISO gain at 8x to prevent grainy noise
    s->set_bpc(s, 1);                         // Black Pixel Correction ON
    s->set_wpc(s, 1);                         // White Pixel Correction ON
    s->set_raw_gma(s, 1);                     // Raw Gamma Curve Correction ON
    s->set_lenc(s, 1);                        // Lens Vignette Correction ON
    s->set_dcw(s, 1);                         // Downsize Clock/Scaling precision ON
    s->set_colorbar(s, 0);                    // Test pattern OFF

    if (s->id.PID == OV2640_PID) {
        s->set_vflip(s, 1);
        s->set_hmirror(s, 0);
    }
}

static bool initCamera(framesize_t frameSize) {
    if (isCameraInitialized) return true;

    // Hard power-cycle OV2640 sensor so internal registers reset cleanly
    pinMode(PWDN_GPIO_NUM, OUTPUT);
    digitalWrite(PWDN_GPIO_NUM, HIGH);
    delay(100);
    digitalWrite(PWDN_GPIO_NUM, LOW);
    delay(100);

    framesize_t actualSize = psramFound() ? frameSize : FRAMESIZE_SVGA;

    camera_config_t config = {};
    config.ledc_channel = LEDC_CHANNEL_0;
    config.ledc_timer   = LEDC_TIMER_0;
    config.pin_d0       = Y2_GPIO_NUM;
    config.pin_d1       = Y3_GPIO_NUM;
    config.pin_d2       = Y4_GPIO_NUM;
    config.pin_d3       = Y5_GPIO_NUM;
    config.pin_d4       = Y6_GPIO_NUM;
    config.pin_d5       = Y7_GPIO_NUM;
    config.pin_d6       = Y8_GPIO_NUM;
    config.pin_d7       = Y9_GPIO_NUM;
    config.pin_xclk     = XCLK_GPIO_NUM;
    config.pin_pclk     = PCLK_GPIO_NUM;
    config.pin_vsync    = VSYNC_GPIO_NUM;
    config.pin_href     = HREF_GPIO_NUM;
    config.pin_sccb_sda = SIOD_GPIO_NUM;
    config.pin_sccb_scl = SIOC_GPIO_NUM;
    config.pin_pwdn     = PWDN_GPIO_NUM;
    config.pin_reset    = RESET_GPIO_NUM;
    config.xclk_freq_hz = 20000000;
    config.frame_size   = psramFound() ? FRAMESIZE_UXGA : FRAMESIZE_SVGA;
    config.pixel_format = PIXFORMAT_JPEG;
    config.grab_mode    = CAMERA_GRAB_WHEN_EMPTY;
    config.fb_location  = CAMERA_FB_IN_PSRAM;
    config.jpeg_quality = 10;
    config.fb_count     = 1;

    // Stock CameraWebServer pattern: detect PSRAM for high-res double buffering
    if (psramFound()) {
        config.jpeg_quality = 8;
        config.fb_count     = 2;
        config.grab_mode    = CAMERA_GRAB_LATEST;
    } else {
        config.frame_size   = FRAMESIZE_SVGA;
        config.fb_location  = CAMERA_FB_IN_DRAM;
        config.jpeg_quality = 10;
    }

    esp_err_t err = esp_camera_init(&config);
    if (err != ESP_OK) {
        Serial.printf("[Camera] esp_camera_init failed: 0x%x\n", err);
        return false;
    }

    sensor_t* s = esp_camera_sensor_get();
    applyOv2640ImageQualityTuning(s, actualSize);

    // Warmup flush: discard initial frames so AWB/AEC settle after power-on
    for (int i = 0; i < 2; i++) {
        camera_fb_t* warmFb = esp_camera_fb_get();
        if (warmFb) {
            esp_camera_fb_return(warmFb);
        }
        delay(40);
    }

    isCameraInitialized = true;
    Serial.printf("[Camera] Online! PSRAM: %s | FrameSize: %s | JPEG Quality: %d\n",
                  psramFound() ? "ENABLED (Double Buffer)" : "DISABLED (Internal DRAM)",
                  getFrameSizeLabel(actualSize),
                  psramFound() ? 8 : 10);
    return true;
}

static void stopCamera() {
    // All acquired frame buffers must be returned before releasing the driver.
    if (isCameraInitialized) {
        esp_camera_deinit();
        isCameraInitialized = false;
    }
    pinMode(PWDN_GPIO_NUM, OUTPUT);
    digitalWrite(PWDN_GPIO_NUM, HIGH);
    Serial.println("[Camera] Sensor in standby. Wi-Fi remains available for capture requests.");
}

static bool isCapturing = false;

// -------------------------------------------------------------
// HIGH-RES SNAPSHOT MULTIPART UPLOAD TO .NET 10 API
// -------------------------------------------------------------
static bool captureAndUploadSnapshot(framesize_t targetResolution, bool useFlash, int structureId) {
    if (isCapturing) {
        Serial.println("[Snapshot] Capture already in progress. Ignoring duplicate trigger.");
        return false;
    }
    isCapturing = true;
    feedWatchdog();

    framesize_t effectiveRes = psramFound() ? targetResolution : FRAMESIZE_SVGA;

    Serial.printf("\n[Snapshot] >>> Shutter triggered! Capturing frame (%s, Flash: %s, Struct: %d)...\n",
                  getFrameSizeLabel(effectiveRes),
                  (useFlash || isFlashOn) ? "ON" : "OFF", structureId);

    if (!isCameraInitialized) {
        if (!initCamera(effectiveRes)) {
            Serial.println("[Snapshot] Camera initialization failed.");
            stopCamera();
            isCapturing = false;
            return false;
        }
    } else {
        sensor_t* s = esp_camera_sensor_get();
        if (s != NULL) {
            s->set_framesize(s, effectiveRes);
            s->set_quality(s, psramFound() ? 8 : 10);
        }
    }

    // Engage PWM LED flash and allow auto-exposure to adapt across 2 warmup frames
    bool flashEngaged = useFlash || isFlashOn;
    if (flashEngaged) {
        setFlash(true);
        delay(180);
    }

    // Flush 2 stale DMA buffers so AWB/AEC lock onto current lighting before final shutter capture
    for (int i = 0; i < 2; i++) {
        camera_fb_t* warm = esp_camera_fb_get();
        if (warm) {
            esp_camera_fb_return(warm);
        }
        delay(35);
    }

    // Capture crisp final frame
    camera_fb_t* fb = esp_camera_fb_get();

    // Turn OFF flash immediately after shutter capture (unless flashlight is kept ON)
    if (flashEngaged && !isFlashOn) {
        setFlash(false);
    }

    feedWatchdog();

    if (!fb) {
        Serial.println("[Snapshot] Frame capture failed. Recovering camera sensor...");
        if (flashEngaged) setFlash(false);
        stopCamera();
        isCapturing = false;
        return false;
    }

    Serial.printf("[Snapshot] Frame captured successfully! Size: %u bytes (%u KB)\n", fb->len, fb->len / 1024);

    // Outbound Upload to API (supports both HTTP and HTTPS based on getApiBaseUrl())
    String apiBase = getApiBaseUrl();
    if (apiBase.endsWith("/")) apiBase = apiBase.substring(0, apiBase.length() - 1);
    String uploadUrl = apiBase + "/api/Camera/upload";
    Serial.printf("[Upload] Sending photo to: %s\n", uploadUrl.c_str());

    bool isHttps = apiBase.startsWith("https://");
    WiFiClientSecure secureClient;
    WiFiClient plainClient;
    HTTPClient http;
    http.setTimeout(25000);

    feedWatchdog();

    bool httpOk = false;
    if (isHttps) {
        secureClient.setInsecure();
        httpOk = http.begin(secureClient, uploadUrl);
    } else {
        httpOk = http.begin(plainClient, uploadUrl);
    }

    if (!httpOk) {
        Serial.println("[Upload] Failed to connect to API endpoint.");
        esp_camera_fb_return(fb);
        stopCamera();
        isCapturing = false;
        return false;
    }

    String boundary = "----SmartHydroBoundary" + String(millis());
    http.addHeader("Content-Type", "multipart/form-data; boundary=" + boundary);

    // Build multipart form body
    String head = "--" + boundary + "\r\n";
    head += "Content-Disposition: form-data; name=\"deviceMac\"\r\n\r\n" + deviceMac + "\r\n";
    if (structureId > 0) {
        head += "--" + boundary + "\r\n";
        head += "Content-Disposition: form-data; name=\"structureId\"\r\n\r\n" + String(structureId) + "\r\n";
    }
    head += "--" + boundary + "\r\n";
    head += "Content-Disposition: form-data; name=\"plant\"\r\n\r\nHydroponic Canopy\r\n";
    head += "--" + boundary + "\r\n";
    head += "Content-Disposition: form-data; name=\"cameraLabel\"\r\n\r\nOverhead ESP32-CAM\r\n";
    head += "--" + boundary + "\r\n";
    head += "Content-Disposition: form-data; name=\"file\"; filename=\"canopy.jpg\"\r\n";
    head += "Content-Type: image/jpeg\r\n\r\n";

    String tail = "\r\n--" + boundary + "--\r\n";

    uint32_t totalLen = head.length() + fb->len + tail.length();

    // Allocate in PSRAM (ps_malloc) to prevent depleting internal SRAM!
    uint8_t* payloadBuffer = nullptr;
    if (psramFound()) {
        payloadBuffer = (uint8_t*)ps_malloc(totalLen);
    } else {
        payloadBuffer = (uint8_t*)malloc(totalLen);
    }

    if (!payloadBuffer) {
        Serial.println("[Upload] Memory allocation failed for payload buffer.");
        esp_camera_fb_return(fb);
        stopCamera();
        http.end();
        isCapturing = false;
        return false;
    }

    memcpy(payloadBuffer, head.c_str(), head.length());
    memcpy(payloadBuffer + head.length(), fb->buf, fb->len);
    memcpy(payloadBuffer + head.length() + fb->len, tail.c_str(), tail.length());

    // The upload owns its copy now; stop the sensor before the blocking POST.
    esp_camera_fb_return(fb);
    stopCamera();

    feedWatchdog();
    int code = http.POST(payloadBuffer, totalLen);
    free(payloadBuffer);
    feedWatchdog();

    Serial.printf("[Upload] Server response: %d\n", code);
    if (code == 200 || code == 201) {
        Serial.println("[Upload] >>> Photo uploaded and saved to database successfully!\n");
    } else {
        Serial.printf("[Upload] Upload returned error code: %d\n\n", code);
    }

    http.end();
    isCapturing = false;
    return (code == 200 || code == 201);
}

// -------------------------------------------------------------
// REST COMMAND POLLING (CameraCommands Table Synchronization)
// -------------------------------------------------------------
static void pollApiCommands() {
    if (WiFi.status() != WL_CONNECTED || isCapturing || g_pendingCaptureRequested) return;

    String apiBase = getApiBaseUrl();
    if (apiBase.endsWith("/")) apiBase = apiBase.substring(0, apiBase.length() - 1);
    String pollUrl = apiBase + "/api/camera/poll-command/" + deviceMac;

    bool isHttps = apiBase.startsWith("https://");
    WiFiClientSecure secureClient;
    WiFiClient plainClient;
    HTTPClient http;
    http.setTimeout(3500);

    bool beginOk = false;
    if (isHttps) {
        secureClient.setInsecure();
        beginOk = http.begin(secureClient, pollUrl);
    } else {
        beginOk = http.begin(plainClient, pollUrl);
    }
    if (!beginOk) return;

    int code = http.GET();
    if (code == 200) {
        String payload = http.getString();
        StaticJsonDocument<384> doc;
        DeserializationError err = deserializeJson(doc, payload);
        if (!err) {
            bool capturePending = doc["capturePending"] | false;
            bool withFlash = doc["flash"] | false;
            int structId = doc["structureId"] | 0;
            if (capturePending) {
                Serial.printf("[CameraCommands] >>> Pending capture command detected (Flash: %s, Struct: %d)! Snapping high-res photo...\n",
                              withFlash ? "ON" : "OFF", structId);
                http.end();
                captureAndUploadSnapshot(getPreferredFrameSize(), withFlash, structId);
                return;
            }
        }
    }
    http.end();
}

// -------------------------------------------------------------
// REAL-TIME SIGNALR WEBSOCKET CLIENT (ACTUATORHUB)
// -------------------------------------------------------------
static WebSocketsClient wsCamera;
static bool isWsConnected = false;

static String getApiHostName() {
    String host = getApiBaseUrl();
    if (host.startsWith("https://")) host = host.substring(8);
    else if (host.startsWith("http://")) host = host.substring(7);
    int slashIdx = host.indexOf('/');
    if (slashIdx != -1) host = host.substring(0, slashIdx);
    int portIdx = host.indexOf(':');
    if (portIdx != -1) host = host.substring(0, portIdx);
    return host;
}

static int getApiPort() {
    String api = getApiBaseUrl();
    int port = api.startsWith("https://") ? 443 : 80;
    int colonIdx = api.indexOf(':', 8);
    if (colonIdx != -1) {
        int slashIdx = api.indexOf('/', colonIdx);
        String portStr = (slashIdx != -1) ? api.substring(colonIdx + 1, slashIdx) : api.substring(colonIdx + 1);
        int parsed = portStr.toInt();
        if (parsed > 0) port = parsed;
    }
    return port;
}

static int wsFailCount = 0;

static void onCameraWsEvent(WStype_t type, uint8_t * payload, size_t length) {
    switch (type) {
        case WStype_DISCONNECTED:
            isWsConnected = false;
            Serial.println("[SignalR WS] Disconnected from ActuatorHub.");
            wsFailCount++;
            if (wsFailCount >= 4 && !isUsingFallbackApi()) {
                Serial.println("[SignalR WS] Primary Azure WebSocket unreachable. Switching to Localhost fallback...");
                setUseFallbackApi(true);
                wsFailCount = 0;
                initCameraWebSocket();
            }
            break;

        case WStype_CONNECTED:
            isWsConnected = true;
            wsFailCount = 0;
            Serial.println("[SignalR WS] Connected to ActuatorHub! Sending handshake...");
            wsCamera.sendTXT("{\"protocol\":\"json\",\"version\":1}\x1e");
            break;

        case WStype_TEXT: {
            String msg = String((char*)payload);
            int start = 0;
            while (start < msg.length()) {
                int end = msg.indexOf('\x1e', start);
                if (end == -1) end = msg.length();
                String record = msg.substring(start, end);
                start = end + 1;

                if (record == "{}") {
                    Serial.printf("[SignalR WS] Handshake confirmed! Registering camera group '%s'...\n", deviceMac.c_str());
                    String regPayload = "{\"type\":1,\"target\":\"RegisterDevice\",\"arguments\":[\"" + deviceMac + "\"]}\x1e";
                    wsCamera.sendTXT(regPayload);
                    continue;
                }

                if (record.length() > 0) {
                    StaticJsonDocument<512> doc;
                    DeserializationError err = deserializeJson(doc, record);
                    if (!err && doc.containsKey("target")) {
                        const char* target = doc["target"];

                        if (strcmp(target, "TriggerCapture") == 0 || strcmp(target, "CaptureSnapshot") == 0) {
                            bool withFlash = false;
                            int targetStructId = 0;
                            const char* targetMac = nullptr;
                            if (doc.containsKey("arguments") && doc["arguments"].size() > 0) {
                                JsonVariant arg = doc["arguments"][0];
                                if (arg.is<JsonObject>()) {
                                    targetMac = arg["deviceMac"] | nullptr;
                                    withFlash = arg["flash"] | false;
                                    targetStructId = arg["structureId"] | 0;
                                }
                            }
                            // Only trigger if addressed to this camera or broadcast without a differing MAC
                            bool macMatch = true;
                            if (targetMac != nullptr && strlen(targetMac) > 0) {
                                String cleanTarget = String(targetMac);
                                cleanTarget.replace(":", "");
                                cleanTarget.replace("-", "");
                                cleanTarget.toUpperCase();
                                String cleanSelf = deviceMac;
                                cleanSelf.replace(":", "");
                                cleanSelf.replace("-", "");
                                cleanSelf.toUpperCase();
                                macMatch = (cleanTarget == cleanSelf);
                            }
                            if (macMatch) {
                                Serial.printf("[SignalR] <<< On-demand snapshot queued (Flash: %s, Struct: %d)!\n",
                                              withFlash ? "ON" : "OFF", targetStructId);
                                g_pendingCaptureRequested = true;
                                g_pendingCaptureFlash = withFlash;
                                g_pendingCaptureStructureId = targetStructId;
                            }
                        } else if (strcmp(target, "SetFlashlight") == 0 || strcmp(target, "ToggleFlash") == 0) {
                            bool state = false;
                            if (doc.containsKey("arguments") && doc["arguments"].size() > 0) {
                                JsonVariant arg = doc["arguments"][0];
                                if (arg.is<JsonObject>()) {
                                    state = arg["state"] | false;
                                } else {
                                    state = arg.as<bool>();
                                }
                            }
                            isFlashOn = state;
                            setFlash(isFlashOn);
                            Serial.printf("[SignalR] <<< Persistent Flashlight set to: %s\n", isFlashOn ? "ON" : "OFF");
                        }
                    }
                }
            }
            break;
        }

        case WStype_ERROR:
            Serial.println("[SignalR WS] WebSocket connection error.");
            break;

        default:
            break;
    }
}

static void initCameraWebSocket() {
    String host = getApiHostName();
    int port = getApiPort();
    bool isHttps = getApiBaseUrl().startsWith("https://");

    Serial.printf("[SignalR WS] Initializing WebSocket to %s://%s:%d/hubs/actuators\n",
                  isHttps ? "wss" : "ws", host.c_str(), port);

    if (isHttps) {
        wsCamera.beginSSL(host.c_str(), port, "/hubs/actuators");
    } else {
        wsCamera.begin(host.c_str(), port, "/hubs/actuators");
    }

    wsCamera.onEvent(onCameraWsEvent);
    wsCamera.setReconnectInterval(5000);
}

// -------------------------------------------------------------
// CLOUD HEARTBEAT & CAMERA REGISTRATION (Strictly Separate from LoRa)
// -------------------------------------------------------------
static unsigned long lastCloudSyncMs = 0;
static bool isDeviceRegistered = false;

static void syncWithCloud() {
    if (WiFi.status() != WL_CONNECTED) return;

    pollApiCommands();
    if (!isDeviceRegistered) {
        Serial.printf("[Cloud] Online Camera Node active! MAC: %s | IP: %s\n",
                      deviceMac.c_str(), WiFi.localIP().toString().c_str());
        isDeviceRegistered = true;
    }
}

// -------------------------------------------------------------
// ARDUINO SETUP & RUNTIME
// -------------------------------------------------------------
void setup() {
    Serial.begin(115200);
    delay(200);
    Serial.println("\n[Init] SmartHydro ESP32-CAM (Online High-Res Node) Booting...");

    // Determine Hardware MAC Address
    uint8_t mac[6];
    esp_read_mac(mac, ESP_MAC_WIFI_STA);
    char macStr[18];
    snprintf(macStr, sizeof(macStr), "%02X:%02X:%02X:%02X:%02X:%02X",
             mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    deviceMac = String(macStr);
    Serial.printf("[Init] MAC Address: %s\n", deviceMac.c_str());

    setupLedFlash();
    stopCamera();

    // 1. Connect to Wi-Fi first to ensure stable power rail before turning on camera
    bool connected = connectEnterpriseWiFi();
    if (!connected) {
        String setupAp = "SmartHydro-CAM-Setup";
        startEnterpriseSetupPortal(setupAp.c_str());
        unsigned long lastRetryMs = millis();
        while (true) {
            handleProvisioningLoop();
            feedWatchdog();
            // Retry WiFi connection every 30 seconds in case network becomes available
            if (millis() - lastRetryMs >= 30000) {
                lastRetryMs = millis();
                Serial.println("[Portal] Retrying WiFi connection...");
                if (connectEnterpriseWiFi()) {
                    Serial.println("[Portal] WiFi reconnected! Rebooting into station mode...");
                    delay(500);
                    ESP.restart();
                } else {
                    // WiFi retry failed — restore the setup AP so portal stays reachable
                    Serial.println("[Portal] Retry failed. Restoring setup AP...");
                    WiFi.mode(WIFI_AP);
                    delay(50);
                    WiFi.softAP(setupAp.c_str());
                }
            }
            delay(10);
        }
    }

    WiFi.softAPdisconnect(true);
    WiFi.mode(WIFI_STA);

    // 2. Keep Wi-Fi modem sleep OFF so DMA clock never glitches
    WiFi.setSleep(false);
    esp_wifi_set_ps(WIFI_PS_NONE);
    Serial.println("[Power] Wi-Fi connected and power stabilized.");

    // Start SignalR WebSocket connection
    initCameraWebSocket();

    // Initial Cloud Heartbeat
    syncWithCloud();

    lastPollTime = millis();
    // Take a baseline on startup, then schedule daily from the last successful upload.
    lastDailyCapture = millis() - DAILY_CAPTURE_INTERVAL_MS;
    lastDailyAttempt = millis() - DAILY_CAPTURE_RETRY_MS;
    initHardwareWatchdog(30);
}

void loop() {
    feedWatchdog();

    // Execute any on-demand capture triggered via SignalR
    if (g_pendingCaptureRequested) {
        g_pendingCaptureRequested = false;
        captureAndUploadSnapshot(getPreferredFrameSize(), g_pendingCaptureFlash, g_pendingCaptureStructureId);
    }

    // Handle OTA Web Server
    handleProvisioningLoop();

    // SignalR WebSocket Event Loop (sub-second command delivery!)
    wsCamera.loop();

    unsigned long now = millis();

    // Periodic Heartbeat every 60 seconds
    if (WiFi.status() == WL_CONNECTED && (now - lastCloudSyncMs >= 60000)) {
        lastCloudSyncMs = now;
        syncWithCloud();
    }

    // 1. Poll API CameraCommands table every 4s so commands queued by MVC always execute reliably
    if (now - lastPollTime >= POLL_INTERVAL_MS) {
        lastPollTime = now;
        pollApiCommands();
    }

    // Service user requests first. Failed daily uploads retry at most every five minutes.
    now = millis();
    if (WiFi.status() == WL_CONNECTED && !isCapturing && !g_pendingCaptureRequested &&
        now - lastDailyCapture >= DAILY_CAPTURE_INTERVAL_MS &&
        now - lastDailyAttempt >= DAILY_CAPTURE_RETRY_MS) {
        lastDailyAttempt = now;
        Serial.println("[Scheduled Daily] Taking the automatic daily photograph.");
        if (captureAndUploadSnapshot(getPreferredFrameSize(), false, 0)) {
            lastDailyCapture = millis();
        }
    }

    delay(10);
}
