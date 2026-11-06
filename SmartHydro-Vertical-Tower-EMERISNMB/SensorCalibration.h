#pragma once
#include <Arduino.h>
#include <Preferences.h>
#include <math.h>
#include "TowerPhConversion.h"
#include "TowerSensorDoseGuard.h"
#include "TowerLastPh.h"
#include <esp_timer.h>

// DFRobot SEN0161-V2 pH and SEN0244 TDS, directly connected to ADC1.
// References: github.com/DFRobot/DFRobot_PH and github.com/DFRobot/GravityTDS.
// EC is expressed as mS/cm; multiply by 1000 for uS/cm. No ppm is reported.
class SensorCalibration {
public:
    float phV7 = 1500.0f;
    float phV4 = 2032.44f;
    float ecK = 1.0f;
    float phMv = 0.0f;
    uint32_t phRaw = 0, phMvMin = 0, phMvMax = 0;
    uint32_t phRawMin = 0, phRawMax = 0;
    const char* phReason = "not_sampled";
    float ecMv = 0.0f;
    bool faultPH = true, faultEC = true, faultDHT = true, faultLight = false;
    SHSensorDoseGuard doseGuard;
    SHSensorDoseGuard ecDoseGuard;
    SHTowerLastPh lastPh;
    bool hasPhCalibration() const { return calibratedPh; }

    void begin() {
        doseGuard.reset();
        ecDoseGuard.reset(); lastPh.reset();
        Preferences storage;
        storage.begin("smarthydro", true);
        savedPh7 = storage.isKey("ph_v7"); savedPh4 = storage.isKey("ph_v4");
        phV7 = savedPh7 ? storage.getFloat("ph_v7", 1500.0f) : 1500.0f;
        phV4 = savedPh4 ? storage.getFloat("ph_v4", 2032.44f) : 2032.44f;
        savedPh7 = savedPh7 && validPhPoint(phV7);
        savedPh4 = savedPh4 && validPhPoint(phV4);
        if (!savedPh7) phV7 = 1500.0f;
        if (!savedPh4) phV4 = 2032.44f;
        calibratedPh = savedPh7 && savedPh4 && validPhPair(phV7, phV4);
        ecK = storage.isKey("ec_k") ? storage.getFloat("ec_k", 1.0f) : 1.0f;
        storage.end();
        if (!isfinite(ecK) || ecK < 0.25f || ecK > 4.0f) ecK = 1.0f;
    }

    static float readMilliVolts(uint8_t pin, uint32_t* minimum = nullptr, uint32_t* maximum = nullptr) {
        uint32_t samples[16];
        for (uint8_t i = 0; i < 16; ++i) {
            samples[i] = analogReadMilliVolts(pin);
            delayMicroseconds(250);
        }
        float average = shTowerTrimmedMilliVolts(samples);
        if (minimum) *minimum = samples[0];
        if (maximum) *maximum = samples[15];
        return average;
    }

    float readPh(uint8_t pin) {
        uint32_t sumRaw = 0;
        // Use the same middle-eight-of-sixteen filter as EC, retaining every
        // sample's endpoints for the existing signal-quality checks below.
        phMv = readMilliVolts(pin, &phMvMin, &phMvMax);
        // Match HydroBeetle: finish the calibrated-mV batch before taking
        // raw diagnostic readings, rather than alternating the two ADC APIs.
        phRawMin = UINT32_MAX; phRawMax = 0;
        for (uint8_t i = 0; i < 10; ++i) {
            uint32_t raw = analogRead(pin);
            sumRaw += raw;
            if (raw < phRawMin) phRawMin = raw;
            if (raw > phRawMax) phRawMax = raw;
            delayMicroseconds(250);
        }
        phRaw = sumRaw / 10;
        // Raw diagnostics are sampled later and cannot replace a failed mV batch.
        // Do not average a rail dropout or overrange sample into a valid pH.
        SHTowerPhSignal signal = shTowerPhSignal(phMvMin, phMvMax);
        bool invalid = signal != SHTowerPhSignal::Valid;
        phReason = shTowerPhSignalReason(signal);
        float ph = calibratedPh ? 7.0f - 3.0f * (phMv - phV7) / (phV4 - phV7) + PH_CALIBRATION_OFFSET
                                : shTowerDefaultPh(phMv, PH_CALIBRATION_OFFSET);
        if (!invalid && (!isfinite(ph) || ph < 0.0f || ph > 14.0f)) {
            invalid = true;
            phReason = "conversion_out_of_range";
        }
        faultPH = consecutive(invalid, phFailures);
        lastPh.observe(ph, !invalid && ph >= PH_PLAUSIBLE_MIN && ph <= PH_PLAUSIBLE_MAX,
            (uint64_t)esp_timer_get_time() / 1000ULL);
        // A rejected sample is unavailable immediately; three failures confirm a fault.
        return invalid ? -1.0f : ph;
    }

    float readEc(uint8_t pin, float ambientTemp) {
        ecMv = readMilliVolts(pin);
        // Zero conductivity is within the SEN0244 measurement range.
        // Neither a low water status nor a zero signal proves a probe fault.
        bool invalid = ecMv > 2300.0f;
        // DHT22 measures air, not reservoir water. This is an approximation,
        // as requested; 25 C is used when air is outside the compensation bounds.
        compensationTemp = isfinite(ambientTemp) && ambientTemp >= -10.0f && ambientTemp <= 60.0f
            ? ambientTemp : 25.0f;
        float ec = rawEcMs(ecMv, compensationTemp) * ecK;
        invalid = invalid || !isfinite(ec) || ec < 0.0f || ec > 15.0f;
        faultEC = consecutive(invalid, ecFailures);
        return invalid ? -1.0f : ec;
    }

    bool dhtValid(float temperature, float humidity) {
        bool valid = isfinite(temperature) && temperature >= -40.0f && temperature <= 80.0f
            && isfinite(humidity) && humidity >= 0.0f && humidity <= 100.0f;
        faultDHT = consecutive(!valid, dhtFailures);
        return valid;
    }

    float readLight(uint8_t pin) {
        int raw = analogRead(pin);
        // DFR0026 V2.1: darkness and ADC saturation are valid endpoints.
        // An analogue reading alone cannot prove that the module is disconnected.
        faultLight = raw < 0 || raw > 4095;
        return faultLight ? -1.0f : raw * (100.0f / 4095.0f);
    }

    bool command(String command, uint8_t phPin, uint8_t ecPin, float ambientTemp) {
        command.trim();
        command.toLowerCase();
        if (command != "calph7" && command != "calph4" && !command.startsWith("calec:")) return false;
        float nextV7 = phV7, nextV4 = phV4, nextK = ecK;
        if (command == "calph7" || command == "calph4") {
            readPh(phPin);
            float mv = phMv;
            if (command == "calph7") nextV7 = mv; else nextV4 = mv;
            bool hasOtherPoint = command == "calph7" ? savedPh4 : savedPh7;
            if (shTowerPhSignal(phMvMin, phMvMax) != SHTowerPhSignal::Valid || !validPhPoint(mv) ||
                (hasOtherPoint && !validPhPair(nextV7, nextV4))) {
                Serial.println(F("[Calibration] Rejected pH calibration: use a known buffer and a stable signal."));
                return true;
            }
        } else {
            char* end = nullptr;
            String argument = command.substring(6);
            float target = strtof(argument.c_str(), &end);
            float mv = readMilliVolts(ecPin);
            float temp = isfinite(ambientTemp) && ambientTemp >= -10.0f && ambientTemp <= 60.0f
                ? ambientTemp : 25.0f;
            float raw = rawEcMs(mv, temp);
            nextK = target / raw;
            if (end == argument.c_str() || *end != '\0' || !isfinite(target) || target <= 0.0f || target > 2.0f
                || mv < 5.0f || mv > 2300.0f || !isfinite(nextK) || nextK < 0.25f || nextK > 4.0f) {
                Serial.println(F("[Calibration] Rejected EC calibration: use a known standard within 0-2 mS/cm."));
                return true;
            }
        }
        Preferences storage;
        if (!storage.begin("smarthydro", false)) {
            Serial.println(F("[Calibration] Cannot open NVS."));
            return true;
        }
        bool saved;
        if (command == "calph7") saved = storage.putFloat("ph_v7", nextV7) == sizeof(float);
        else if (command == "calph4") saved = storage.putFloat("ph_v4", nextV4) == sizeof(float);
        else saved = storage.putFloat("ec_k", nextK) == sizeof(float);
        storage.end();
        if (saved) {
            phV7 = nextV7; phV4 = nextV4; ecK = nextK;
            if (command == "calph7") savedPh7 = true;
            if (command == "calph4") savedPh4 = true;
            calibratedPh = savedPh7 && savedPh4 && validPhPair(phV7, phV4);
            doseGuard.reset();
            ecDoseGuard.reset(); lastPh.reset();
            Serial.println(F("[Calibration] Saved. Fresh stable readings are required before dosing."));
            if ((command == "calph7" || command == "calph4") && !calibratedPh)
                Serial.println(F("[Calibration] Capture the other pH buffer to complete two-point calibration."));
        } else Serial.println(F("[Calibration] NVS write failed; calibration unchanged."));
        return true;
    }

private:
    bool calibratedPh = false;
    bool savedPh7 = false, savedPh4 = false;
    uint8_t phFailures = 0, ecFailures = 0, dhtFailures = 0;
    float compensationTemp = 25.0f;
    static bool consecutive(bool failed, uint8_t& count) {
        count = failed ? (count < 3 ? count + 1 : 3) : 0;
        return count >= 3;
    }
    static bool validPhPoint(float mv) {
        return isfinite(mv) && mv >= 15.0f && mv <= 3000.0f;
    }
    static bool validPhPair(float neutral, float acid) {
        return validPhPoint(neutral) && validPhPoint(acid)
            && acid - neutral >= 100.0f && acid - neutral <= 1500.0f;
    }
    static float rawEcMs(float mv, float temperature) {
        float volts = mv / 1000.0f;
        float polynomial = 133.42f * volts * volts * volts - 255.86f * volts * volts + 857.39f * volts;
        return polynomial / (1.0f + 0.02f * (temperature - 25.0f)) / 1000.0f;
    }
};
