#pragma once
#include <stdint.h>

// Keep a brief run of fresh readings before permitting another chemical dose.
class SHSensorDoseGuard {
    bool sampled = false, valid = false;
    uint8_t goodSamples = 0;
    uint32_t lastAt = 0, firstGoodAt = 0;
public:
    void reset() { sampled = valid = false; goodSamples = 0; }

    void observe(bool good, uint32_t now) {
        bool gap = sampled && uint32_t(now - lastAt) > 10000UL;
        bool distinct = !sampled || now != lastAt;
        if (!good || gap) goodSamples = 0;
        if (good && distinct) {
            if (!goodSamples) firstGoodAt = now;
            if (goodSamples < 3) ++goodSamples;
        }
        sampled = true; valid = good; lastAt = now;
    }

    const char* refusal(uint32_t now) const {
        if (!sampled || !valid) return "sensor_fault";
        if (uint32_t(now - lastAt) > 10000UL) return "sensor_stale";
        if (goodSamples < 3 || uint32_t(lastAt - firstGoodAt) < 6000UL)
            return "sensor_recovering";
        return nullptr;
    }

    uint32_t freshFor(uint32_t now) const {
        return refusal(now) ? 0 : 10000UL - uint32_t(now - lastAt);
    }
};
