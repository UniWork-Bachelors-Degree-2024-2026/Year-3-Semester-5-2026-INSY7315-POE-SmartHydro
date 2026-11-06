#pragma once
#include <stdint.h>

struct SHConnectivityPolicy {
    int preferred = 0, active = 0, reason = 0;
    uint32_t enteredMs = 0, lastCloudMs = 0, lastGoodLoraMs = 0;
    uint32_t lastProbeMs = 0;
    uint32_t lastRecoveryAckMs = 0;
    uint8_t goodAcks = 0;
    bool onlineSwitchPending = false;
    bool hasGoodLoraAck = false;

    void enter(int mode, int why, uint32_t now) {
        active = mode;
        reason = why;
        enteredMs = now;
        lastCloudMs = now;
        lastProbeMs = now;
        goodAcks = 0;
        onlineSwitchPending = mode == 1 && why == 0;
    }
    void userChoice(int mode, uint32_t now) {
        preferred = mode;
        enter(mode, 0, now);
    }
    void cloudSuccess(uint32_t now) {
        lastCloudMs = now;
        onlineSwitchPending = false;
    }
    void loraAck(bool cloudUp, uint32_t now) {
        if (!cloudUp) { goodAcks = 0; return; }
        if (uint32_t(now - lastGoodLoraMs) > 30000UL) goodAcks = 0;
        lastGoodLoraMs = now;
        hasGoodLoraAck = true;
        if (goodAcks > 0 && uint32_t(now - lastRecoveryAckMs) < 10000UL) return;
        lastRecoveryAckMs = now;
        if (goodAcks < 3) ++goodAcks;
    }
    int next(uint32_t now, bool savedWifi = true) {
        if (hasGoodLoraAck && uint32_t(now - lastGoodLoraMs) >= 300000UL) hasGoodLoraAck = false;
        bool loraRecent = goodAcks > 0 && uint32_t(now - lastGoodLoraMs) < 60000UL;
        if (!loraRecent) goodAcks = 0;
        if (active == 0 && uint32_t(now - enteredMs) >= 300000UL &&
            (!loraRecent && (!hasGoodLoraAck || uint32_t(now - lastGoodLoraMs) >= 300000UL))) return savedWifi ? 1 : 2;
        if (active == 1) {
            if (reason == 1 && preferred == 0 && goodAcks >= 3) return 0;
            if (uint32_t(now - lastCloudMs) >= 600000UL) return 2;
        }
        if (active == 2 && preferred == 0 && goodAcks >= 3) return 0;
        return active;
    }
};
