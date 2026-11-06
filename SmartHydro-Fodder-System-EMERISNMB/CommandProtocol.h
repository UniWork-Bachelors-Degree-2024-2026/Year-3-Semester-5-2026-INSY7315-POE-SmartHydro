#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>
#include <math.h>
#include <esp_timer.h>

struct SHCommandResult { uint16_t cid = 0; bool ok = false; const char* reason = "unsupported"; const char* phase = "accepted"; };
class SHManualOverrides {
  uint32_t since[4] = {}; bool held[4] = {};
public:
  void hold(uint8_t channel) { since[channel] = millis(); held[channel] = true; }
  bool active(uint8_t channel) const {
    return held[channel] && (uint32_t)(millis() - since[channel]) < 3600000UL;
  }
  void apply(JsonObject cmd) {
    if (cmd.containsKey("r")) for (int i = 0; i < 4; ++i) hold(i);
    if (cmd.containsKey("w") || cmd.containsKey("s") || cmd.containsKey("mainWaterPump") || cmd.containsKey("spraySystem") || cmd.containsKey("waterPump")) hold(0);
    if (cmd.containsKey("gl") || cmd.containsKey("l") || cmd.containsKey("growLight") || cmd.containsKey("GrowLight")) hold(1);
    if (cmd.containsKey("fc") || cmd.containsKey("circulationFan") || cmd.containsKey("CirculationFan")) hold(2);
    if (cmd.containsKey("fe") || cmd.containsKey("extractorFan") || cmd.containsKey("ExtractorFan")) hold(3);
  }
};
class SHCommandHistory {
  SHCommandResult entries[16];
  uint8_t next = 0;
public:
  SHCommandResult* find(uint16_t cid) {
    if (!cid) return nullptr;
    for (auto& item : entries) if (item.cid == cid) return &item;
    return nullptr;
  }
  void save(SHCommandResult result) {
    if (!result.cid) return;
    auto* item = find(result.cid);
    if (item) *item = result;
    else { entries[next] = result; next = (next + 1) % 16; }
  }
};

static bool shNormaliseCommand(JsonObject cmd, uint8_t mask) {
  if (cmd.containsKey("actuator") && cmd.containsKey("state")) {
    String actuator = cmd["actuator"] | ""; actuator.toLowerCase();
    const char* key = nullptr;
    if (actuator == "growlight") key = "gl";
    else if (actuator == "mainwaterpump" || actuator == "waterpump" || actuator == "spraysystem") key = "w";
    else if (actuator == "circulationfan") key = "fc";
    else if (actuator == "extractorfan") key = "fe";
    else if (actuator == "phup" || actuator == "phuppump") key = "pu";
    else if (actuator == "phdown" || actuator == "phdownpump") key = "pd";
    else if (actuator == "ecup" || actuator == "nutrientuppump") key = "nu";
    else if (actuator == "ecdown" || actuator == "dilutiondownpump") key = "nd";
    if (!key) return false;
    cmd[key] = cmd["state"].as<bool>() ? 1 : 0;
  }
  String text = cmd["cmd"] | (cmd["command"] | "");
  text.trim(); text.toLowerCase();
  if (!text.length()) return true;
  int colon = text.indexOf(':');
  String name = colon >= 0 ? text.substring(0, colon) : text;
  const char* doseKey = nullptr;
  if (name == "pu" || name == "ph_up") doseKey = "pu";
  else if (name == "pd" || name == "ph_down") doseKey = "pd";
  else if (name == "nu" || name == "ec_up" || name == "nutrient") doseKey = "nu";
  else if (name == "nd" || name == "ec_down") doseKey = "nd";
  if (doseKey) {
    float ml = 1.0f;
    if (colon >= 0) {
      String amount = text.substring(colon + 1); amount.trim();
      char* end; ml = strtof(amount.c_str(), &end);
      if (!amount.length() || *end || !isfinite(ml) || ml <= 0) return false;
    }
    cmd[doseKey] = constrain(ml, 0.5f, 25.0f);
    return true;
  }
  text.replace('_', ' ');
  const char* key = nullptr; uint8_t bit = 0;
  const char* names[] = {"light", "pump", "spray", "fan", "circulation", "extractor"};
  const char* keys[] = {"gl", "w", "s", "fc", "fc", "fe"};
  const uint8_t bits[] = {2, 1, 1, 4, 4, 8};
  for (int i = 0; i < 6; ++i) {
    String base(names[i]);
    if (text == base || text == base + " on" || text == base + " off") { key = keys[i]; bit = bits[i]; break; }
  }
  if (!key) return false;
  cmd[key] = text.endsWith(" on") ? 1 : text.endsWith(" off") ? 0 : ((mask & bit) ? 0 : 1);
  return true;
}

static bool shRequestedDoseMs(JsonObject cmd, uint32_t& duration) {
  float value = 800;
  if (cmd.containsKey("dm") || cmd.containsKey("durationMs")) value = cmd["dm"] | (cmd["durationMs"] | 0.0f);
  else if (cmd.containsKey("volumeMl") || cmd.containsKey("v") || cmd.containsKey("vol")) {
    float ml = cmd["volumeMl"] | (cmd["v"] | (cmd["vol"] | 0.0f));
    if (!isfinite(ml) || ml <= 0) return false;
    value = constrain(ml, 0.5f, 25.0f) * 800.0f;
  } else if (cmd.containsKey("durationSeconds")) value = cmd["durationSeconds"].as<float>() * 1000.0f;
  if (!isfinite(value) || value < 400 || value > 20000) return false;
  duration = lroundf(value); return true;
}

static bool shHasCommandAction(JsonObject cmd) {
  const char* keys[] = {"r", "w", "s", "fc", "fe", "gl", "l", "dt", "pu", "pd", "nu", "nd",
    "growLight", "GrowLight", "circulationFan", "CirculationFan", "extractorFan", "ExtractorFan",
    "mainWaterPump", "waterPump", "spraySystem", "phUp", "phDown", "ecUp", "ecDown",
    "phUpPump", "PhUpPump", "phDownPump", "PhDownPump", "nutrientUpPump", "NutrientUpPump",
    "dilutionDownPump", "DilutionDownPump", "nutrientA", "nutrientB", "mode", "operatingMode",
    "p0", "minPh", "p1", "maxPh", "p2", "minEc", "p3", "maxEc", "p5", "maxTemp", "p7", "maxHum"};
  for (const char* key : keys) if (cmd.containsKey(key)) return true;
  return false;
}


static bool shThresholdsValid(JsonObject cmd, float phLow, float phHigh, float ecLow, float ecHigh) {
  const char* aliases[6][3] = {{"p0", "minPh", nullptr}, {"p1", "maxPh", nullptr},
    {"p2", "minEc", "e0"}, {"p3", "maxEc", "e1"}, {"p5", "maxTemp", "t1"}, {"p7", "maxHum", "h1"}};
  float proposed[] = {phLow, phHigh, ecLow, ecHigh, 25, 80}; bool any = false;
  for (int i = 0; i < 6; ++i) for (int j = 0; j < 3 && aliases[i][j]; ++j) if (cmd.containsKey(aliases[i][j])) {
    JsonVariant value = cmd[aliases[i][j]];
    if (!value.is<float>() && !value.is<int>()) return false;
    proposed[i] = value.as<float>(); if (!isfinite(proposed[i])) return false;
    any = true; break;
  }
  return !any || (proposed[0] >= 0 && proposed[1] <= 14 && proposed[0] < proposed[1] &&
    proposed[2] >= 0 && proposed[2] < proposed[3] && proposed[3] <= 99999 &&
    proposed[4] >= -40 && proposed[4] <= 80 && proposed[5] >= 0 && proposed[5] <= 100);
}

// Returns -1 for a refused dose, 0 for no dose, and 1 for an accepted dose/stop.
static int shApplyDose(JsonObject cmd, bool (*start)(const char*, uint32_t, const char*), void (*stop)(const char*)) {
  const char* doseId = cmd["doseId"] | (cmd["commandId"] | "");
  if (cmd.containsKey("dt")) {
    uint32_t duration;
    if (!shRequestedDoseMs(cmd, duration)) return -1;
    return start(cmd["dt"] | "", duration, doseId) ? 1 : -1;
  }
  const char* aliases[4][6] = {
    {"pu", "phUp", "phUpPump", "PhUpPump", nullptr, nullptr},
    {"pd", "phDown", "phDownPump", "PhDownPump", nullptr, nullptr},
    {"nu", "ecUp", "nutrientUpPump", "NutrientUpPump", "nutrientA", nullptr},
    {"nd", "ecDown", "dilutionDownPump", "DilutionDownPump", "nutrientB", nullptr}
  };
  const char* pumps[] = {"PH_UP", "PH_DOWN", "NUTRIENT_UP", "DILUTION_DOWN"};
  JsonVariant values[4]; int starts = 0; bool found = false;
  for (int i = 0; i < 4; ++i) for (int j = 0; j < 6 && aliases[i][j]; ++j) {
    if (cmd.containsKey(aliases[i][j])) {
      values[i] = cmd[aliases[i][j]]; found = true;
      if (!values[i].is<bool>() && !values[i].is<float>() && !values[i].is<int>()) return -1;
      float ml = values[i].as<float>();
      if (!isfinite(ml) || ml < 0) return -1;
      if (ml > 0) starts++;
      break;
    }
  }
  if (starts > 1) return -1;
  for (int i = 0; i < 4; ++i) if (!values[i].isNull() && values[i].as<float>() == 0) stop(pumps[i]);
  for (int i = 0; i < 4; ++i) if (!values[i].isNull() && values[i].as<float>() > 0) {
    uint32_t duration = lroundf(constrain(values[i].as<float>(), 0.5f, 25.0f) * 800.0f);
    return start(pumps[i], duration, doseId) ? 1 : -1;
  }
  return found ? 1 : 0;
}

#include "DosingPolicy.h"
#include <Preferences.h>
#include <time.h>
class SHDoseSafety {
  SHDoseBudget budget;
  Preferences storage;
  bool ready = false;
  esp_timer_handle_t timer = nullptr;
  uint8_t cutoffPin = 0;
  uint32_t duration = 0, deadline = 0;
  const char* lastReason = "not_ready";
  static void cutoff(void* arg) { digitalWrite(static_cast<SHDoseSafety*>(arg)->cutoffPin, HIGH); }
public:
  void begin() {
    ready = storage.begin("dose-safety", false);
    if (!ready) return;
    int64_t utc = time(nullptr), saved = storage.getLong64("at", 0);
    uint32_t used = storage.getUInt("ph", 0), mix = storage.getUInt("mix", 0);
    // Without a trustworthy clock, keep the saved limits for a full new window.
    if (utc >= 1704067200LL && saved >= 1704067200LL && utc >= saved) {
      uint32_t elapsed = (uint32_t)(utc - saved);
      if (elapsed >= 3600) used = 0;
      mix = elapsed * 1000ULL >= mix ? 0 : mix - elapsed * 1000UL;
    }
    budget.restore(millis(), used, mix);
    lastReason = "ready";
  }
  uint32_t remaining() const { return budget.remaining(millis()); }
  uint32_t endsAt() const { return deadline; }
  const char* reason() const { return lastReason; }
  bool arm(uint8_t pin, uint32_t requested, bool fault, bool busy) {
    uint32_t now = millis();
    bool ph = pin == PIN_DOSE_PH_UP || pin == PIN_DOSE_PH_DOWN;
    lastReason = budget.refusal(now, ph, requested, fault, busy);
    if (lastReason) return false;
    if (!ready) { lastReason = "safety_storage"; return false; }
    if (!timer) {
      esp_timer_create_args_t args = {}; args.callback = cutoff; args.arg = this; args.name = "dose-off";
      if (esp_timer_create(&args, &timer) != ESP_OK) { lastReason = "cutoff_timer"; return false; }
    }
    uint32_t used = budget.phUsed(now) + (ph ? requested : 0);
    uint32_t mix = requested + (ph ? 900000UL : 600000UL) + 2000UL;
    // Save the charge before energising the pump, including if power fails mid-pulse.
    if (storage.putLong64("at", time(nullptr)) != 8 || storage.putUInt("mix", mix) != 4 ||
        storage.putUInt("ph", used) != 4) { lastReason = "safety_storage"; return false; }
    esp_timer_stop(timer); cutoffPin = pin; duration = requested;
    if (esp_timer_start_once(timer, (uint64_t)duration * 1000ULL) != ESP_OK) { lastReason = "cutoff_timer"; return false; }
    budget.charge(now, ph, duration); deadline = now + duration; lastReason = "accepted";
    return true;
  }
  bool outputStarted(uint8_t pin) {
    if (pin != cutoffPin || !timer) return false;
    esp_timer_stop(timer);
    if (esp_timer_start_once(timer, (uint64_t)duration * 1000ULL) != ESP_OK) { lastReason = "cutoff_timer"; return false; }
    budget.outputStarted(millis(), pin == PIN_DOSE_PH_UP || pin == PIN_DOSE_PH_DOWN);
    deadline = millis() + duration;
    return true;
  }
};
