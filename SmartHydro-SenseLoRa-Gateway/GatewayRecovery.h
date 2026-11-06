#pragma once
#include <stdint.h>
#include <stdio.h>
#include <string.h>

static inline bool gatewaySimReady(const char* response) {
    const char* line = strstr(response, "+CPIN:");
    char state[16] = {};
    return line && sscanf(line, "+CPIN: %15s", state) == 1 && strcmp(state, "READY") == 0;
}

static inline bool gatewayRegistered(const char* response) {
    const char* line = strstr(response, "+CEREG:");
    while (line) {
        int reporting = -1, state = -1;
        if (sscanf(line, "+CEREG: %d,%d", &reporting, &state) == 2)
            return state == 1 || state == 5;
        line = strstr(line + 7, "+CEREG:");
    }
    return false;
}

struct GatewayRecovery {
    uint8_t failures = 0;
    uint8_t attachFailures = 0;
    uint32_t lastAttemptMs = 0;
    uint32_t retryDelayMs = 0;
    uint32_t lastResetMs = 0;
    bool hasReset = false;

    bool ready(uint32_t now) const {
        return retryDelayMs == 0 || uint32_t(now - lastAttemptMs) >= retryDelayMs;
    }
    void failed(uint32_t now) {
        if (failures < 10) ++failures;
        lastAttemptMs = now;
        retryDelayMs = 1000UL << (failures > 5 ? 5 : failures - 1);
    }
    void connected() {
        failures = 0;
        attachFailures = 0;
        retryDelayMs = 0;
    }
    void serverUnavailable(uint32_t now) {
        // A valid HTTP refusal proves that the modem transport worked.
        failures = 0;
        attachFailures = 0;
        lastAttemptMs = now;
        retryDelayMs = 5000UL;
    }
    bool shouldReset(uint32_t now) const {
        return (failures >= 4 || attachFailures >= 4) &&
               (!hasReset || uint32_t(now - lastResetMs) >= 300000UL);
    }
    void resetStarted(uint32_t now) {
        hasReset = true;
        lastResetMs = now;
        attachFailures = 0;
    }
};
