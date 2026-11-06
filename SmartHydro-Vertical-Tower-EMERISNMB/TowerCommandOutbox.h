#pragma once
#include <stdint.h>
#include <stddef.h>
#include <string.h>

struct SHTowerReceiptRecord {
    uint32_t magic = 0x53484143;
    uint16_t version = 1, cid = 0;
    uint8_t phase = 0;
    char payload[384] = {};
    uint32_t checksum = 0;
};
static uint32_t shTowerReceiptChecksum(const SHTowerReceiptRecord& record) {
    const auto* bytes = reinterpret_cast<const uint8_t*>(&record);
    uint32_t hash = 2166136261UL;
    for (size_t i = 0; i < offsetof(SHTowerReceiptRecord, checksum); ++i) hash = (hash ^ bytes[i]) * 16777619UL;
    return hash;
}
static bool shValidTowerReceipt(const SHTowerReceiptRecord& record) {
    return record.magic == 0x53484143 && record.version == 1 && record.cid && record.phase <= 2
        && record.payload[0] && memchr(record.payload, '\0', sizeof(record.payload))
        && record.checksum == shTowerReceiptChecksum(record);
}

template<class Payload> class SHTowerCommandOutbox {
public:
    struct Entry { Payload payload; uint16_t cid = 0; uint8_t phase = 0; uint32_t created = 0, sentAt = 0; bool sent = false, persisted = false; };
    using Save = bool (*)(size_t, const Entry&);
private:
    Entry entries[16];
    bool reserved[16] = {};
    size_t cursor = 0;
    Save save = nullptr;
public:
    void attachStorage(Save writer) { save = writer; }
    void reserveSlot(size_t index) { if (index < 16) reserved[index] = true; }
    bool canPut(uint16_t cid) const {
        if (!cid) return false;
        for (size_t i = 0; i < 16; ++i) if (!reserved[i] && (!entries[i].cid || entries[i].cid == cid)) return true;
        return false;
    }
    bool restore(size_t index, uint16_t cid, uint8_t phase, const Payload& payload, uint32_t now) {
        if (index >= 16 || reserved[index] || !cid || phase > 2) return false;
        entries[index] = Entry{payload, cid, phase, now, 0, false, true}; return true;
    }
    bool put(uint16_t cid, uint8_t phase, const Payload& payload, uint32_t now) {
        Entry* slot = nullptr;
        for (size_t i = 0; i < 16; ++i) {
            if (reserved[i]) continue;
            auto& entry = entries[i];
            if (entry.cid == cid) {
                if (entry.phase != 0 && phase == 0) return true;
                if (entry.phase == phase) return true;
                slot = &entry; break;
            }
            if (!entry.cid && !slot) slot = &entry;
        }
        if (!cid || !slot) return false;
        *slot = Entry{payload, cid, phase, now, 0, false};
        if (save) slot->persisted = save(size_t(slot - entries), *slot);
        return true;
    }
    Entry* next(uint32_t now) {
        for (size_t n = 0; n < 16; ++n) {
            size_t i = (cursor + n) % 16;
            auto& entry = entries[i];
            if (entry.cid && (!entry.sent || uint32_t(now - entry.sentAt) >= 12000UL)) {
                // An unknown outcome stays pending until the API confirms its receipt.
                if (save && !entry.persisted) entry.persisted = save(i, entry);
                cursor = (i + 1) % 16; return &entry;
            }
        }
        return nullptr;
    }
    void sent(Entry& entry, uint32_t now) { entry.sent = true; entry.sentAt = now; }
    void confirm(uint16_t cid, uint8_t phase) {
        for (size_t i = 0; i < 16; ++i) if (entries[i].cid == cid && entries[i].phase == phase) {
            if (save) save(i, Entry{});
            entries[i] = Entry{};
        }
    }
};
