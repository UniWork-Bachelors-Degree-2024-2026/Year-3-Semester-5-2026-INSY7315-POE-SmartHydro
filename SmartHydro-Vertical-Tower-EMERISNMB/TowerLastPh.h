#pragma once
#include <stdint.h>
#include <math.h>

// RAM-only display history. A restart never invents a previous pH reading.
// This cache does not authorise confirmed manual overrides.
class SHTowerLastPh {
    bool available = false;
    uint64_t capturedMs = 0;
    float average = 0;
public:
    static constexpr uint32_t MaximumAgeMs = 10800000UL;
    constexpr void reset() { available = false; }
    constexpr void observe(float ph, bool accepted, uint64_t now) {
        if (!accepted || !(ph >= 0 && ph <= 14)) return;
        average = ph; capturedMs = now; available = true;
    }
    constexpr uint32_t remaining(uint64_t now) const {
        if (!available || now < capturedMs || now - capturedMs >= MaximumAgeMs) return 0;
        return MaximumAgeMs - (uint32_t)(now - capturedMs);
    }
    // Display history may remain visible after dosing eligibility expires.
    constexpr bool hasReading(uint64_t now) const { return available && now >= capturedMs; }
    constexpr uint64_t ageMs(uint64_t now) const { return hasReading(now) ? now - capturedMs : 0; }
    constexpr float value() const { return average; }
};
