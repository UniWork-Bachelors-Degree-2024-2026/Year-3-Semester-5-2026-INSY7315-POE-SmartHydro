#pragma once
#include <stdint.h>
#include <stddef.h>
#include <string.h>

struct RingBufferSlot {
    uint64_t seq = 0;
    int64_t capturedUnix = 0;
    uint32_t capturedMs = 0, bootId = 0, lastSentMs = 0;
    float ph = -1, ec = -1, temp = -99, hum = -1, light = -1;
    uint16_t waterRaw = 0, version = 1;
    uint8_t relays = 0, mode = 0, preferred = 0, reason = 0, flags = 0;
    int8_t mlPh = 2, mlEc = 2, mlTemp = 2, mlHum = 2;
    uint32_t checksum = 0;
};

static inline uint32_t telemetryChecksum(const RingBufferSlot& sample) {
    const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&sample);
    uint32_t value = 2166136261UL;
    for (size_t i = 0; i < offsetof(RingBufferSlot, checksum); ++i) value = (value ^ bytes[i]) * 16777619UL;
    return value;
}

class TelemetryQueue {
public:
    static constexpr size_t Capacity = 64;
    RingBufferSlot slots[Capacity] = {};
    size_t head = 0;

    size_t count() const {
        size_t result = 0;
        for (const auto& slot : slots) if (slot.seq) ++result;
        return result;
    }
    int find(uint64_t seq) const {
        if (!seq) return -1;
        for (size_t i = 0; i < Capacity; ++i) if (slots[i].seq == seq) return (int)i;
        return -1;
    }
    int oldest(uint32_t now, bool readyOnly = false) const {
        int found = -1;
        for (size_t i = 0; i < Capacity; ++i) {
            if (!slots[i].seq) continue;
            if (readyOnly && slots[i].lastSentMs && uint32_t(now - slots[i].lastSentMs) < 15000UL) continue;
            if (found < 0 || slots[i].seq < slots[found].seq) found = (int)i;
        }
        return found;
    }
    int insert(const RingBufferSlot& sample) {
        for (size_t n = 0; n < Capacity; ++n) {
            size_t i = (head + n) % Capacity;
            if (!slots[i].seq) { slots[i] = sample; head = (i + 1) % Capacity; return (int)i; }
        }
        int i = oldest(0);
        slots[i] = sample; head = (i + 1) % Capacity;
        return i;
    }
    bool acknowledge(uint64_t seq) {
        int i = find(seq);
        if (i < 0) return false;
        slots[i] = RingBufferSlot{};
        return true;
    }
    static bool uncertain(const RingBufferSlot& sample, uint32_t boot) {
        return sample.capturedUnix == 0 && sample.bootId != boot;
    }
    static uint32_t age(const RingBufferSlot& sample, uint32_t now, uint32_t boot, int64_t utc) {
        if (sample.capturedUnix > 0 && utc >= sample.capturedUnix)
            return (uint32_t)(utc - sample.capturedUnix);
        return sample.bootId == boot ? uint32_t(now - sample.capturedMs) / 1000UL : 0;
    }
};
