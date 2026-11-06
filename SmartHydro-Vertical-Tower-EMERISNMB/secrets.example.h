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

// Required for cloud telemetry and commands. Match Security__DeviceKey on the API.
// Use 32 random bytes written as 64 hexadecimal characters. Keep secrets.h private.
#define SMARTHYDRO_DEVICE_KEY "REPLACE_WITH_YOUR_64_CHARACTER_HEXADECIMAL_KEY"
