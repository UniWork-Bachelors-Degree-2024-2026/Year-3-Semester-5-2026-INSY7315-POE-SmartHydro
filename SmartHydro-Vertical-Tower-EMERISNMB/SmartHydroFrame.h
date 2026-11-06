#pragma once
#include <Arduino.h>
#include <stdint.h>
#include <math.h>

// ==============================================================================
// SMARTHYDRO UNIFIED RF FRAME, MODE ENUM & ICASA 1% AIRTIME LEDGER (v1)
// ==============================================================================
// Shared across:
//   1. SmartHydro-Fodder-System-EMERISNMB (Heltec WiFi LoRa 32 V3 - ESP32-S3)
//   2. SmartHydro-Vertical-Tower-EMERISNMB (DFRobot FireBeetle 2 ESP32-C6)
//   3. SmartHydro-Tower-LoRa-Bridge-EMERISNMB (Seeed XIAO ESP32S3 + Wio-SX1262)
//   4. SmartHydro-SenseLoRa-Gateway (SenseLoRa 4G LTE + SX1276 Gateway)
//
// South African ICASA Compliance (868.0 - 868.6 MHz SRD Band):
//   - Maximum Effective Radiated Power (ERP): +14 dBm (25 mW)
//   - Maximum Duty Cycle: 1% rolling 1-hour window (36,000 ms per 3,600,000 ms)
//   - Modulation: SF7, BW 125.0 kHz, CR 4/5, Preamble 8, Sync Word 0x12
// ==============================================================================

#define SH_FRAME_VERSION                1

// Canonical Operating Mode Enumeration (Identical across Firmware, API, DB, MVC)
#ifndef MODE_LORA_BLE
#define MODE_LORA_BLE                   0   // Primary Default: 868.1 MHz LoRa + NimBLE 5.0
#endif
#ifndef MODE_WIFI_CLOUD
#define MODE_WIFI_CLOUD                 1   // Online Cloud Mode: Wi-Fi + SignalR WebSockets + LoRa/BLE active
#endif
#ifndef MODE_EMERGENCY_SOFTAP
#define MODE_EMERGENCY_SOFTAP           2   // Emergency SoftAP Mode: Local Hotspot + LoRa/BLE active
#endif

// Mode Entry Reason Enumeration (Prevents Auto-Revert Overwriting User Choice)
#define MODE_REASON_USER                0   // Explicitly selected by operator via MVC, BLE, SoftAP, or BOOT button
#define MODE_REASON_AUTO_FALLBACK       1   // Entered automatically after 5-min LoRa cloud-ACK timeout

// Canonical Actuator & Dosing Source Attribution Codes
#define SH_SRC_USER_REMOTE              0   // MVC / Cloud operator command
#define SH_SRC_EDGE_ML                  1   // On-device Random Forest ML classifier decision
#define SH_SRC_THRESHOLD                2   // On-device user recipe threshold boundary rule
#define SH_SRC_SCHEDULE                 3   // Photoperiod (16h/8h) or irrigation/misting timer schedule
#define SH_SRC_SAFETY                   4   // Hardware safety / shock-prevention interlock
#define SH_SRC_USER_LOCAL               5   // Local BLE or SoftAP farmer action

// Binary Frame Type Identifiers (Reserved for dual binary/JSON parser support)
#define SH_FRAME_TYPE_TELEMETRY         0x11
#define SH_FRAME_TYPE_CMD_ACK           0x12
#define SH_FRAME_TYPE_DOSE_EVENT        0x13
#define SH_FRAME_TYPE_GATEWAY_ACK       0x21
#define SH_FRAME_TYPE_DOWNLINK_CMD      0x22

// ICASA 1% Rolling 1-Hour Airtime Budget Constants
#define ICASA_WINDOW_MS                 (3600UL * 1000UL)
#define ICASA_MAX_AIRTIME_MS_PER_HOUR   36000UL
#define ICASA_TELEMETRY_SOFT_CAP_MS     32000UL
#define SH_AIRTIME_SLOTS                128

// Transmission Priority Classes for Airtime Ledger Admission Control
#define SH_PRIO_CRITICAL_ACK            0   // Command ACKs & Gateway ACKs (Always admitted up to 36,000 ms)
#define SH_PRIO_COMMAND_EVENT           1   // Actuator state transitions & dose events
#define SH_PRIO_TELEMETRY               2   // Periodic 15s / 17.5s live telemetry
#define SH_PRIO_BACKLOG                 3   // Store-and-forward historical replay frames

#pragma pack(push, 1)
struct ShTelemetryFrameV1 {
    uint8_t  versionAndType;   // (SH_FRAME_VERSION << 4) | 0x01 (0x11)
    uint8_t  mac[6];           // 6-byte hardware MAC address
    uint8_t  bootId;           // Incremented in NVS on every boot
    uint16_t seq;              // Monotonic sequence number
    uint16_t ageSec;           // Seconds since sample capture (0 = live, >0 = buffered)
    uint8_t  ph_x100_hi;       // uint16 pH * 100
    uint8_t  ph_x100_lo;
    uint16_t ec_uS;            // Electrical conductivity in uS/cm (0 - 15000)
    int16_t  temp_x10;         // Temperature in 0.1 C
    uint16_t hum_x10;          // Relative humidity in 0.1 %
    uint16_t lux_div10;        // Ambient light in lux / 10
    uint8_t  relayMask;        // Canonical 8-bit relay state bitmask
    uint8_t  modeAndFlags;     // bits 0..1=activeMode, bits 2..3=prefMode, bit4=reason, bit5=waterOk, bit6=fault, bit7=abort
    uint8_t  crc8;             // CRC-8/MAXIM over bytes 0..23
};

struct ShCommandAckFrameV1 {
    uint8_t  versionAndType;   // 0x12
    uint8_t  mac[6];
    uint16_t cid;              // 16-bit Command ID being acknowledged
    uint8_t  relayMask;        // Confirmed physical GPIO read-back bitmask
    uint8_t  activeMode;       // Confirmed active operating mode (0, 1, or 2)
    uint8_t  crc8;
};

struct ShGatewayAckFrameV1 {
    uint8_t  versionAndType;   // 0x21
    uint8_t  mac[6];
    uint8_t  upFlag;           // 1 = 4G cloud POST succeeded within 60s, 0 = cloud unreachable
    int8_t   rssiDbm;          // Gateway reception RSSI in dBm
    uint8_t  crc8;
};
#pragma pack(pop)

// CRC-8 calculation helper for compact binary frames
static inline uint8_t shComputeCrc8(const uint8_t* data, size_t len) {
    uint8_t crc = 0x00;
    for (size_t i = 0; i < len; i++) {
        crc ^= data[i];
        for (uint8_t b = 0; b < 8; b++) {
            if (crc & 0x80) crc = (uint8_t)((crc << 1) ^ 0x07);
            else crc <<= 1;
        }
    }
    return crc;
}

// Canonical mode string <-> index conversion helpers
static inline const char* shModeToString(int mode) {
    switch (mode) {
        case MODE_WIFI_CLOUD:       return "online";
        case MODE_EMERGENCY_SOFTAP: return "softap";
        case MODE_LORA_BLE:
        default:                    return "lora";
    }
}

static inline const char* shModeReasonToString(int reason) {
    return (reason == MODE_REASON_AUTO_FALLBACK) ? "auto_fallback" : "user";
}

static inline const char* shSourceCodeToString(uint8_t src) {
    switch (src) {
        case SH_SRC_EDGE_ML:     return "EdgeML";
        case SH_SRC_THRESHOLD:   return "Threshold";
        case SH_SRC_SCHEDULE:    return "Schedule";
        case SH_SRC_SAFETY:      return "SafetyInterlock";
        case SH_SRC_USER_LOCAL:  return "LocalOperator";
        case SH_SRC_USER_REMOTE:
        default:                 return "RemoteOperator";
    }
}

static inline int shParseModeVariant(const String& modeStr) {
    String s = modeStr;
    s.trim();
    s.toLowerCase();
    if (s == "0" || s == "lora" || s == "lora_ble" || s == "auto") {
        return MODE_LORA_BLE;
    }
    if (s == "1" || s == "online" || s == "wifi" || s == "cloud" || s == "wifi_cloud") {
        return MODE_WIFI_CLOUD;
    }
    if (s == "2" || s == "softap" || s == "offline" || s == "ap" || s == "hotspot") {
        return MODE_EMERGENCY_SOFTAP;
    }
    return -1;
}

// Analytical LoRa Time-on-Air (ToA) calculator in milliseconds (Semtech AN1200.13 formula)
// Used when RadioLib's getTimeOnAir() is not directly accessible (e.g. across UART bridge)
static inline uint32_t shCalculateLoRaToaMs(
    size_t payloadBytes,
    uint8_t sf = 7,
    float bwKhz = 125.0f,
    uint8_t cr = 5,
    uint16_t preambleLen = 8)
{
    float tSymMs = powf(2.0f, (float)sf) / bwKhz; // For SF7/125kHz: 128 / 125 = 1.024 ms
    float tPreambleMs = ((float)preambleLen + 4.25f) * tSymMs;
    int de = (sf >= 11 && bwKhz <= 125.0f) ? 1 : 0;
    int ih = 0; // Explicit header enabled
    int crc = 1; // CRC enabled
    float num = 8.0f * (float)payloadBytes - 4.0f * (float)sf + 28.0f + 16.0f * (float)crc - 20.0f * (float)ih;
    float den = 4.0f * ((float)sf - 2.0f * (float)de);
    int payloadCock = 0;
    if (num > 0.0f && den > 0.0f) {
        payloadCock = (int)ceilf(num / den) * (cr);
    }
    float nPayload = 8.0f + (float)(payloadCock > 0 ? payloadCock : 0);
    float tPayloadMs = nPayload * tSymMs;
    return (uint32_t)ceilf(tPreambleMs + tPayloadMs);
}

// Rolling 1-Hour Airtime Ledger enforcing the South African ICASA 1% Duty-Cycle Limit
class SmartHydroAirtimeLedger {
public:
    struct Entry {
        uint32_t timestampMs;
        uint16_t durationMs;
    };

    SmartHydroAirtimeLedger() : _head(0), _count(0), _totalMs(0) {
        for (size_t i = 0; i < SH_AIRTIME_SLOTS; i++) {
            _entries[i].timestampMs = 0;
            _entries[i].durationMs = 0;
        }
    }

    void pruneExpired(uint32_t nowMs) {
        while (_count > 0) {
            size_t oldestIdx = (_head + SH_AIRTIME_SLOTS - _count) % SH_AIRTIME_SLOTS;
            if ((uint32_t)(nowMs - _entries[oldestIdx].timestampMs) >= ICASA_WINDOW_MS) {
                if (_totalMs >= _entries[oldestIdx].durationMs) {
                    _totalMs -= _entries[oldestIdx].durationMs;
                } else {
                    _totalMs = 0;
                }
                _count--;
            } else {
                break;
            }
        }
    }

    bool canTransmit(uint16_t packetToaMs, uint8_t priorityClass = SH_PRIO_TELEMETRY) {
        uint32_t nowMs = millis();
        pruneExpired(nowMs);
        uint32_t projected = _totalMs + packetToaMs;
        if (priorityClass == SH_PRIO_CRITICAL_ACK || priorityClass == SH_PRIO_COMMAND_EVENT) {
            return projected <= ICASA_MAX_AIRTIME_MS_PER_HOUR;
        }
        if (priorityClass == SH_PRIO_BACKLOG) {
            return projected <= (ICASA_TELEMETRY_SOFT_CAP_MS - 4000UL);
        }
        return projected <= ICASA_TELEMETRY_SOFT_CAP_MS;
    }

    void recordTransmission(uint16_t packetToaMs) {
        uint32_t nowMs = millis();
        pruneExpired(nowMs);
        if (_count == SH_AIRTIME_SLOTS) {
            size_t oldestIdx = (_head + SH_AIRTIME_SLOTS - _count) % SH_AIRTIME_SLOTS;
            if (_totalMs >= _entries[oldestIdx].durationMs) {
                _totalMs -= _entries[oldestIdx].durationMs;
            }
            _count--;
        }
        _entries[_head].timestampMs = nowMs;
        _entries[_head].durationMs = packetToaMs;
        _head = (_head + 1) % SH_AIRTIME_SLOTS;
        _count++;
        _totalMs += packetToaMs;
    }

    uint32_t getUsedAirtimeMs() {
        pruneExpired(millis());
        return _totalMs;
    }

    float getDutyCyclePercent() {
        pruneExpired(millis());
        return ((float)_totalMs / (float)ICASA_WINDOW_MS) * 100.0f;
    }

    float getBudgetUsedPercent() {
        pruneExpired(millis());
        return ((float)_totalMs / (float)ICASA_MAX_AIRTIME_MS_PER_HOUR) * 100.0f;
    }

private:
    Entry    _entries[SH_AIRTIME_SLOTS];
    size_t   _head;
    size_t   _count;
    uint32_t _totalMs;
};
