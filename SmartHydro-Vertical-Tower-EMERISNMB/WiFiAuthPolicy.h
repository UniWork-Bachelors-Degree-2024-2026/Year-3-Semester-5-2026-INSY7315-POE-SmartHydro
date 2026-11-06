#pragma once
#include <stdint.h>

enum class SHTowerWiFiAuth : uint8_t { SharedPassword = 0, Enterprise = 1 };

// The confirmed default campus network uses a shared password. Preserve the
// authentication of other legacy saved networks rather than changing them.
static constexpr SHTowerWiFiAuth shTowerLegacyWiFiAuth(bool defaultNetwork,
    bool savedUsername, bool defaultEnterprise) {
    return (defaultNetwork ? defaultEnterprise : savedUsername)
        ? SHTowerWiFiAuth::Enterprise : SHTowerWiFiAuth::SharedPassword;
}
