#pragma once
#include <stdint.h>
#include <stdio.h>
#include <string>

class SHGatewayNodeRoutes {
    struct Entry { uint64_t mac = 0; uint32_t seenAt = 0; } entries[8];
    unsigned next = 0;
public:
    void heard(uint64_t mac, uint32_t now) {
        if (!mac) return;
        for (auto& entry : entries) if (entry.mac == mac) { entry.seenAt = now; return; }
        entries[next] = {mac, now}; next = (next + 1) % 8;
    }
    std::string nodes(uint32_t now) const {
        std::string result;
        for (const auto& entry : entries) if (entry.mac && uint32_t(now - entry.seenAt) < 600000UL) {
            char mac[13]; snprintf(mac, sizeof(mac), "%012llX", (unsigned long long)entry.mac);
            if (!result.empty()) result += ',';
            result += mac;
        }
        return result;
    }
};
