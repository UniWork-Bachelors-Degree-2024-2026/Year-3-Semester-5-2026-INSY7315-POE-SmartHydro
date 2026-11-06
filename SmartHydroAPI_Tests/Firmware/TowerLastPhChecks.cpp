#include "../../SmartHydro-Vertical-Tower-EMERISNMB/TowerLastPh.h"

constexpr bool checkLastPh() {
    SHTowerLastPh cache;
    if (cache.remaining(1000) || cache.hasReading(1000)) return false;
    cache.observe(6.2f, true, 1000);
    if (cache.remaining(1000) != 10800000UL || !cache.hasReading(1000) || cache.ageMs(1000)) return false;
    cache.observe(12.0f, false, 2000);
    if (cache.value() != 6.2f || cache.remaining(2000) != 10799000UL) return false;
    if (!cache.remaining(10800999ULL) || cache.remaining(10801000ULL)) return false;
    if (!cache.hasReading(10801000ULL) || cache.ageMs(10801000ULL) != 10800000ULL || cache.value() != 6.2f) return false;
    cache.observe(7.0f, true, 5000000000ULL);
    if (!cache.remaining(5000000001ULL) || cache.remaining(1000) || cache.hasReading(1000)) return false;
    cache.reset();
    return cache.remaining(5000000001ULL) == 0 && !cache.hasReading(5000000001ULL);
}
static_assert(checkLastPh(), "Last pH tracks accepted captures and their age, survives millisecond wrap and resets on restart.");
