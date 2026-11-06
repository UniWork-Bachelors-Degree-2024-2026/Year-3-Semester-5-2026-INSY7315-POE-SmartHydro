#include "../../SmartHydro-Vertical-Tower-EMERISNMB/TowerPhConversion.h"

// Compile with the tower's C++ toolchain. These checks need no connected board.
static_assert(shTowerPhSignal(1490, 1510) == SHTowerPhSignal::Valid);
static_assert(shTowerPhSignal(1500, 1600) == SHTowerPhSignal::Valid);
static_assert(shTowerPhSignal(1500, 1601) == SHTowerPhSignal::Unstable);
static_assert(shTowerPhSignal(15, 115) == SHTowerPhSignal::Valid);
static_assert(shTowerPhSignal(14, 100) == SHTowerPhSignal::NearZero);
static_assert(shTowerPhSignal(0, 0) == SHTowerPhSignal::NearZero);
static_assert(shTowerPhSignal(3000, 3080) == SHTowerPhSignal::Valid);
static_assert(shTowerPhSignal(3000, 3081) == SHTowerPhSignal::Overrange);
static_assert(shTowerPhSignal(1600, 1500) == SHTowerPhSignal::Unstable);

// Previously reported batches must not recover dosing by averaging away noise.
static_assert(shTowerPhSignal(671, 2391) == SHTowerPhSignal::Unstable);
static_assert(shTowerPhSignal(2563, 2867) == SHTowerPhSignal::Unstable);
static_assert(shTowerPhSignal(2750, 3356) == SHTowerPhSignal::Overrange);
static_assert(shTowerPhSignal(0, 65) == SHTowerPhSignal::NearZero);
static_assert(shTowerPhSignal(2838, 2871) == SHTowerPhSignal::Valid);

static constexpr bool trimmedBatchChecks() {
    uint32_t quiet[16] = {1490, 1510, 1500, 1500, 1495, 1505, 1500, 1500,
                         1490, 1510, 1500, 1500, 1495, 1505, 1500, 1500};
    if (shTowerTrimmedMilliVolts(quiet) != 1500.0f ||
        shTowerPhSignal(quiet[0], quiet[15]) != SHTowerPhSignal::Valid) return false;
    // Even when trimming leaves a plausible midpoint, retain a dropout/noise
    // fault. Neither the display cache nor dosing may accept that batch.
    uint32_t dropout[16] = {0, 1500, 1500, 1500, 1500, 1500, 1500, 1500,
                           1500, 1500, 1500, 1500, 1500, 1500, 1500, 1500};
    if (shTowerTrimmedMilliVolts(dropout) != 1500.0f ||
        shTowerPhSignal(dropout[0], dropout[15]) != SHTowerPhSignal::NearZero) return false;
    uint32_t noisy[16] = {600, 2500, 1500, 1500, 1500, 1500, 1500, 1500,
                         1500, 1500, 1500, 1500, 1500, 1500, 1500, 1500};
    if (shTowerTrimmedMilliVolts(noisy) != 1500.0f ||
        shTowerPhSignal(noisy[0], noisy[15]) != SHTowerPhSignal::Unstable) return false;
    uint32_t clipped[16] = {3300, 1500, 1500, 1500, 1500, 1500, 1500, 1500,
                           1500, 1500, 1500, 1500, 1500, 1500, 1500, 1500};
    return shTowerTrimmedMilliVolts(clipped) == 1500.0f &&
        shTowerPhSignal(clipped[0], clipped[15]) == SHTowerPhSignal::Overrange;
}
static_assert(trimmedBatchChecks());
// Passing the signal check does not establish a valid pH conversion or calibration.
