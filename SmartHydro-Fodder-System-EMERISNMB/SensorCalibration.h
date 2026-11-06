#pragma once
#include <Arduino.h>
#include <Preferences.h>
#include <math.h>

// DFRobot SEN0161-V2 pH and SEN0244 TDS, directly connected to ADC1.
// References: github.com/DFRobot/DFRobot_PH and github.com/DFRobot/GravityTDS.
// EC is expressed as mS/cm; multiply by 1000 for uS/cm. No ppm is reported.
class SensorCalibration {
public:
    float phV7 = 1500.0f;
    float phV4 = 2032.44f;
    float ecK = 1.0f;
    float phMv = 0.0f;
    float ecMv = 0.0f;
    bool faultPH = true, faultEC = true, faultDHT = true, faultLight = false;

    void begin() {
        Preferences storage;
        storage.begin("smarthydro", true);
        phV7 = storage.isKey("ph_v7") ? storage.getFloat("ph_v7", 1500.0f) : 1500.0f;
        phV4 = storage.isKey("ph_v4") ? storage.getFloat("ph_v4", 2032.44f) : 2032.44f;
        ecK = storage.isKey("ec_k") ? storage.getFloat("ec_k", 1.0f) : 1.0f;
        storage.end();
        if (!validPhPair(phV7, phV4)) { phV7 = 1500.0f; phV4 = 2032.44f; }
        if (!isfinite(ecK) || ecK < 0.25f || ecK > 4.0f) ecK = 1.0f;
    }

    static float readMilliVolts(uint8_t pin) {
        uint32_t samples[16];
        for (uint8_t i = 0; i < 16; ++i) {
            samples[i] = analogReadMilliVolts(pin);
            delayMicroseconds(250);
        }
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

    float readPh(uint8_t pin) {
        phMv = readMilliVolts(pin);
        bool invalid = phMv < 15.0f || phMv > 3080.0f;
        faultPH = consecutive(invalid, phFailures);
        float ph = 7.0f - 3.0f * (phMv - phV7) / (phV4 - phV7);
        // A rejected sample is unavailable immediately; three failures confirm a fault.
        return invalid || !isfinite(ph) || ph < 0.0f || ph > 14.0f ? -1.0f : ph;
    }

    float readEc(uint8_t pin, float ambientTemp) {
        ecMv = readMilliVolts(pin);
        // Zero conductivity is within the SEN0244 measurement range.
        // Neither a low water status nor a zero signal proves a probe fault.
        bool invalid = ecMv > 2300.0f;
        faultEC = consecutive(invalid, ecFailures);
        // DHT22 measures air, not reservoir water. This is an approximation,
        // as requested; 25 C is used when air is outside the compensation bounds.
        compensationTemp = isfinite(ambientTemp) && ambientTemp >= -10.0f && ambientTemp <= 60.0f
            ? ambientTemp : 25.0f;
        float ec = rawEcMs(ecMv, compensationTemp) * ecK;
        return invalid || !isfinite(ec) || ec < 0.0f || ec > 15.0f ? -1.0f : ec;
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
            float mv = readMilliVolts(phPin);
            if (command == "calph7") nextV7 = mv; else nextV4 = mv;
            if (!validPhPair(nextV7, nextV4)) {
                Serial.println(F("[Calibration] Rejected pH calibration: check buffer and signal."));
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
            Serial.println(F("[Calibration] Saved. The next sample uses the new calibration."));
        } else Serial.println(F("[Calibration] NVS write failed; calibration unchanged."));
        return true;
    }

private:
    uint8_t phFailures = 0, ecFailures = 0, dhtFailures = 0;
    float compensationTemp = 25.0f;
    static bool consecutive(bool failed, uint8_t& count) {
        count = failed ? (count < 3 ? count + 1 : 3) : 0;
        return count >= 3;
    }
    static bool validPhPair(float neutral, float acid) {
        return isfinite(neutral) && isfinite(acid) && neutral >= 15.0f && acid <= 3000.0f
            && acid - neutral >= 100.0f && acid - neutral <= 1500.0f;
    }
    static float rawEcMs(float mv, float temperature) {
        float volts = mv / 1000.0f;
        float polynomial = 133.42f * volts * volts * volts - 255.86f * volts * volts + 857.39f * volts;
        return polynomial / (1.0f + 0.02f * (temperature - 25.0f)) / 1000.0f;
    }
};
