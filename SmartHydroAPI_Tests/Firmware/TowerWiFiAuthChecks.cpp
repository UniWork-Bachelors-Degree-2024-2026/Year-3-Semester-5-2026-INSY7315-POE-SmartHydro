#include "../../SmartHydro-Vertical-Tower-EMERISNMB/WiFiAuthPolicy.h"

static_assert(shTowerLegacyWiFiAuth(true, true, false) == SHTowerWiFiAuth::SharedPassword);
static_assert(shTowerLegacyWiFiAuth(true, false, false) == SHTowerWiFiAuth::SharedPassword);
static_assert(shTowerLegacyWiFiAuth(true, true, true) == SHTowerWiFiAuth::Enterprise);
static_assert(shTowerLegacyWiFiAuth(false, true, false) == SHTowerWiFiAuth::Enterprise);
static_assert(shTowerLegacyWiFiAuth(false, false, false) == SHTowerWiFiAuth::SharedPassword);
static_assert(shTowerLegacyWiFiAuth(false, true, true) == SHTowerWiFiAuth::Enterprise);
