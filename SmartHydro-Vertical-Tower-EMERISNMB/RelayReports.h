#pragma once
#include <ArduinoJson.h>
#include <stdint.h>
#include <stddef.h>
#include <string.h>

static void shLocalRelayFields(JsonObject object, uint8_t mask) {
    const char* names[] = {"mainWaterPump", "growLight", "circulationFan", "extractorFan",
        "phUpPump", "phDownPump", "nutrientUpPump", "dilutionDownPump"};
    for (int bit = 0; bit < 8; ++bit) object[names[bit]] = (mask & (1 << bit)) != 0;
}

static void shBleRelayFields(JsonObject object, uint8_t mask) {
    const char* names[] = {"s", "gl", "fc", "fe", "pu", "pd", "nu", "nd"};
    for (int bit = 0; bit < 8; ++bit) object[names[bit]] = (mask & (1 << bit)) ? 1 : 0;
    object["r"] = mask;
}

static bool shBleNotificationFits(uint16_t mtu, size_t length) {
    return mtu >= 3 && length <= size_t(mtu - 3);
}

static const char* shLocalRefusalMessage(const char* reason) {
    if (!strcmp(reason, "sensor_fault") || !strcmp(reason, "sensor_stale") || !strcmp(reason, "sensor_recovering"))
        return "Dose not started: waiting for fresh, stable sensor readings.";
    if (!strcmp(reason, "mixing_lockout")) return "Dose not started: the reservoir is still mixing.";
    if (!strcmp(reason, "pump_busy")) return "Dose not started: another dosing pump is running.";
    if (!strcmp(reason, "ph_hourly_cap") || !strcmp(reason, "pulse_limit")) return "Dose not started: the safe dosing limit was reached.";
    return "The controller did not accept this request. Check the reported reason.";
}
