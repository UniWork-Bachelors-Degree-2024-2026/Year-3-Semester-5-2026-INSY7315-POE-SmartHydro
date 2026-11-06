#pragma once
#include <stdint.h>
#include <stddef.h>

template<class Payload> class SHCommandAckOutbox {
public:
    struct Entry { Payload payload; uint16_t cid = 0; uint8_t phase = 0; uint32_t created = 0, sentAt = 0; bool sent = false; };
private:
    Entry entries[16];
    size_t cursor = 0;
public:
    bool put(uint16_t cid, uint8_t phase, const Payload& payload, uint32_t now) {
        Entry* slot = nullptr;
        for (auto& entry : entries) {
            if (entry.cid == cid) {
                if (entry.phase != 0 && phase == 0) return true;
                if (entry.phase == phase) return true;
                slot = &entry; break;
            }
            if (!entry.cid && !slot) slot = &entry;
        }
        if (!cid || !slot) return false;
        *slot = Entry{payload, cid, phase, now, 0, false}; return true;
    }
    Entry* next(uint32_t now) {
        for (size_t n = 0; n < 16; ++n) {
            size_t i = (cursor + n) % 16;
            auto& entry = entries[i];
            if (entry.cid && uint32_t(now - entry.created) >= 120000UL) entry = Entry{};
            if (entry.cid && (!entry.sent || uint32_t(now - entry.sentAt) >= 12000UL)) {
                cursor = (i + 1) % 16; return &entry;
            }
        }
        return nullptr;
    }
    void sent(Entry& entry, uint32_t now) { entry.sent = true; entry.sentAt = now; }
    void confirm(uint16_t cid, uint8_t phase) {
        for (auto& entry : entries) if (entry.cid == cid && entry.phase == phase) entry = Entry{};
    }
};
