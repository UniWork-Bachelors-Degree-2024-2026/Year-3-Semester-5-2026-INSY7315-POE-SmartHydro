#pragma once
#include <stdint.h>
#include <stddef.h>

class SHStoredCaptureCache {
    struct Entry { uint64_t mac = 0, sequence = 0; uint32_t at = 0; } entries[64];
    size_t next = 0;
public:
    bool contains(uint64_t mac, uint64_t sequence, uint32_t now) const {
        for (const auto& entry : entries) if (sequence && entry.mac == mac && entry.sequence == sequence && uint32_t(now - entry.at) < 600000UL) return true;
        return false;
    }
    void remember(uint64_t mac, uint64_t sequence, uint32_t now) {
        if (!mac || !sequence) return;
        for (auto& entry : entries) if (entry.mac == mac && entry.sequence == sequence) { entry.at = now; return; }
        entries[next] = {mac, sequence, now}; next = (next + 1) % 64;
    }
};

static inline int shUplinkPriority(bool acknowledgement, bool activity, bool uncertain, int64_t ageSeconds) {
    return acknowledgement ? 0 : !activity && !uncertain && ageSeconds >= 0 && ageSeconds < 30 ? 1 : 2;
}

static inline bool shUploadBeforePoll(bool pollDue, bool acknowledgementWaiting, bool ackTurnUsed) {
    return !pollDue || (acknowledgementWaiting && !ackTurnUsed);
}
template<class Classify>
static size_t shSelectUplink(size_t count, unsigned slot, Classify classify) {
    size_t selected = 0; int best = 3;
    // Receipt traffic comes first, including on a history-draining turn.
    for (size_t i = 0; i < count; ++i) if (classify(i) == 0) return i;
    if (slot % 4 == 0) return selected;
    for (size_t i = 0; i < count; ++i) {
        int priority = classify(i);
        if (priority < best) { best = priority; selected = i; }
    }
    return selected;
}
