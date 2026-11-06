#pragma once
#include <stdint.h>

// Sort the complete batch and average its middle eight samples. Callers must
// validate the untrimmed endpoints before accepting a pH reading.
static constexpr float shTowerTrimmedMilliVolts(uint32_t (&samples)[16]) {
    for (uint8_t i = 1; i < 16; ++i) {
        uint32_t value = samples[i];
        int j = i - 1;
        while (j >= 0 && samples[j] > value) { samples[j + 1] = samples[j]; --j; }
        samples[j + 1] = value;
    }
    uint32_t total = 0;
    for (uint8_t i = 4; i < 12; ++i) total += samples[i];
    return total / 8.0f;
}

enum class SHTowerPhSignal { Valid, NearZero, Overrange, Unstable };

// Provisional noise limit approved for the tower, not a DFRobot specification.
static constexpr uint32_t SH_TOWER_PH_MAX_SPAN_MV = 100;
static constexpr SHTowerPhSignal shTowerPhSignal(uint32_t minimumMv, uint32_t maximumMv) {
    return minimumMv < 15 ? SHTowerPhSignal::NearZero :
        maximumMv > 3080 ? SHTowerPhSignal::Overrange :
        maximumMv < minimumMv || maximumMv - minimumMv > SH_TOWER_PH_MAX_SPAN_MV
            ? SHTowerPhSignal::Unstable : SHTowerPhSignal::Valid;
}

static inline const char* shTowerPhSignalReason(SHTowerPhSignal signal) {
    switch (signal) {
        case SHTowerPhSignal::NearZero: return "signal_near_zero";
        case SHTowerPhSignal::Overrange: return "signal_overrange";
        case SHTowerPhSignal::Unstable: return "signal_unstable";
        default: return "valid";
    }
}

// Gravity SEN0161-V2 factory references for direct ADC wiring.
// 
// An estimate until both pH buffers have been captured; not probe calibration.
static inline float shTowerDefaultPh(float milliVolts, float offset) {
    return 7.0f - 3.0f * (milliVolts - 1500.0f) / (2032.44f - 1500.0f) + offset;
}
