#pragma once
#include <stdint.h>
#include <math.h>

struct SHDosePlan {
    uint8_t pump = 0, source = 3; // 1 pH up, 2 pH down, 3 EC up, 4 clean-water dilution
    float value = 0, low = 0, high = 0, target = 0, ml = 0;
    uint32_t duration = 0;
    int prediction = 2;
};
static inline float shClamp(float value, float low, float high) { return value < low ? low : value > high ? high : value; }
static inline SHDosePlan shPlanDose(float value, float low, float high, int prediction, bool ph) {
    SHDosePlan plan;
    if (!isfinite(value) || !isfinite(low) || !isfinite(high) || low >= high || low < 0 ||
        value < 0 || value > (ph ? 14.0f : 2.0f) || (!ph && value >= 2.0f)) return plan;
    float target = (low + high) / 2.0f, error = value - target;
    if (fabsf(error) <= (ph ? 0.10f : 0.05f)) return plan;
    bool below = value < low, above = value > high;
    bool mlUp = prediction == 1 && value < low + 0.10f && error < 0;
    bool mlDown = prediction == 0 && value > high - 0.10f && error > 0;
    if (!below && !above && !mlUp && !mlDown) return plan;
    plan.pump = ph ? (error < 0 ? 1 : 2) : (error < 0 ? 3 : 4);
    plan.source = below || above ? 3 : 4;
    plan.value = value; plan.low = low; plan.high = high; plan.target = target; plan.prediction = prediction;
    // These are bounded pulse estimates, not a chemical-response calibration.
    float scaled = ph ? shClamp((fabsf(error) - 0.10f) / 0.90f, 0, 1)
                      : shClamp(fabsf(error) / fmaxf(high - low, 0.10f), 0, 1);
    plan.ml = roundf((ph ? 0.5f + 1.5f * scaled : 1.0f + 4.0f * scaled) * 100.0f) / 100.0f;
    plan.duration = (uint32_t)lroundf(plan.ml * 800.0f);
    return plan;
}
class SHDoseBudget {
    struct Charge { uint32_t at = 0, runtime = 0; } phCharges[20];
    uint32_t lastAt = 0, lastRuntime = 0, lastMix = 0, restoredAt = 0, restoredRuntime = 0;
    bool dosed = false;
    unsigned next = 0;
public:
    uint32_t phUsed(uint32_t now) const {
        uint32_t total = uint32_t(now - restoredAt) < 3600000UL ? restoredRuntime : 0;
        for (const auto& charge : phCharges) if (charge.runtime && uint32_t(now - charge.at) < 3600000UL) total += charge.runtime;
        return total;
    }
    uint32_t remaining(uint32_t now) const {
        uint32_t elapsed = uint32_t(now - lastAt), window = lastRuntime + lastMix;
        return dosed && elapsed < window ? window - elapsed : 0;
    }
    const char* refusal(uint32_t now, bool ph, uint32_t duration, bool fault, bool busy) const {
        if (fault) return "sensor_fault";
        if (busy) return "pump_busy";
        if (duration < 400 || duration > (ph ? 1600UL : 20000UL)) return "pulse_limit";
        if (remaining(now)) return "mixing_lockout";
        if (ph && phUsed(now) + duration > 8000UL) return "ph_hourly_cap";
        return nullptr;
    }
    void charge(uint32_t now, bool ph, uint32_t duration) {
        dosed = true; lastAt = now; lastRuntime = duration; lastMix = ph ? 900000UL : 600000UL;
        if (ph) { phCharges[next] = {now, duration}; next = (next + 1) % 20; }
    }
    void outputStarted(uint32_t now, bool ph) {
        lastAt = now;
        if (ph) phCharges[(next + 19) % 20].at = now;
    }
    void restore(uint32_t now, uint32_t phRuntime, uint32_t mixMs) {
        restoredAt = now; restoredRuntime = phRuntime;
        dosed = mixMs > 0; lastAt = now; lastRuntime = 0; lastMix = mixMs;
    }
};
