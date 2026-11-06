#pragma once
// =================================================================================================
// SmartHydro ESP32 - Local Test Secrets Template
// =================================================================================================
// Copy this file to "secrets.h" in the same directory for optional local bench testing.
// "secrets.h" is ignored by Git and will NEVER be committed to the repository.
// In standard deployment, leave empty and configure credentials via the WiFiManager Captive Portal.
// =================================================================================================

#define SECRET_EAP_ID   ""
#define SECRET_EAP_USER ""
#define SECRET_EAP_PASS ""

// Home pilot: the gateway handles Wi-Fi. Manual Online/SoftAP still work.
// Set true to restore automatic Wi-Fi fallback from Primary on this node.
#define SH_PRIMARY_WIFI_FALLBACK false

// Temporary home test: skip BLE startup on the affected Heltec.
#define SH_FODDER_BLE_ENABLED false

// Temporary isolation test. Restore LoRa after checking bare-board stability.
// Enable LoRa with TX disabled for a separate receive-only test.
#define SH_FODDER_LORA_ENABLED false
#define SH_FODDER_LORA_TX_ENABLED false

// Required for cloud telemetry and commands. Match Security__DeviceKey on the API.
// Use 32 random bytes written as 64 hexadecimal characters. Keep secrets.h private.
#define SMARTHYDRO_DEVICE_KEY "REPLACE_WITH_YOUR_64_CHARACTER_HEXADECIMAL_KEY"
