#pragma once
#include <stdint.h>
#include <stddef.h>
#include <string.h>

// This detects damaged records; transport authentication remains separate.
static uint32_t shSpoolChecksum(const void* bytes, size_t length) {
    uint32_t hash = 2166136261UL;
    const auto* data = static_cast<const uint8_t*>(bytes);
    for (size_t i = 0; i < length; ++i) hash = (hash ^ data[i]) * 16777619UL;
    return hash;
}

// Never replace the last readable copy with a partial write.
template<class Storage>
static bool shCommitGatewayRecord(Storage& storage, const char* path, const char* temporary,
                                  const void* bytes, size_t length) {
    auto file = storage.open(temporary, "w");
    if (!file) return false;
    bool written = file.write(static_cast<const uint8_t*>(bytes), length) == length;
    file.flush(); file.close();
    if (!written) { storage.remove(temporary); return false; }
    auto check = storage.open(temporary, "r");
    if (!check || check.size() != length) { check.close(); storage.remove(temporary); return false; }
    uint8_t buffer[64]; size_t offset = 0;
    while (offset < length) {
        size_t count = length - offset > sizeof(buffer) ? sizeof(buffer) : length - offset;
        if (check.read(buffer, count) != count || memcmp(buffer, static_cast<const uint8_t*>(bytes) + offset, count)) {
            check.close(); storage.remove(temporary); return false;
        }
        offset += count;
    }
    check.close();
    if (!storage.rename(temporary, path)) { storage.remove(temporary); return false; }
    return true;
}

// Used only to migrate the old backlog. The source stays intact until every
// remaining byte has been copied and compared, then LittleFS renames the copy.
template<class Storage>
static bool shRetireGatewayPrefix(Storage& storage, const char* path, const char* temporary, size_t offset, void (*progress)() = nullptr) {
    auto source = storage.open(path, "r");
    if (!source || offset > source.size() || !source.seek(offset)) { source.close(); return false; }
    const size_t remaining = source.size() - offset;
    auto target = storage.open(temporary, "w");
    if (!target) { source.close(); return false; }
    uint8_t buffer[256]; size_t copied = 0;
    while (copied < remaining) {
        size_t count = remaining - copied > sizeof(buffer) ? sizeof(buffer) : remaining - copied;
        if (source.read(buffer, count) != count || target.write(buffer, count) != count) {
            source.close(); target.close(); storage.remove(temporary); return false;
        }
        copied += count;
        if (progress) progress();
    }
    target.flush(); target.close(); source.close();
    auto original = storage.open(path, "r");
    auto check = storage.open(temporary, "r");
    if (!original || !check || check.size() != remaining || !original.seek(offset)) {
        original.close(); check.close(); storage.remove(temporary); return false;
    }
    uint8_t compare[256]; copied = 0;
    while (copied < remaining) {
        size_t count = remaining - copied > sizeof(buffer) ? sizeof(buffer) : remaining - copied;
        if (original.read(buffer, count) != count || check.read(compare, count) != count || memcmp(buffer, compare, count)) {
            original.close(); check.close(); storage.remove(temporary); return false;
        }
        copied += count;
        if (progress) progress();
    }
    original.close(); check.close();
    if (!storage.rename(temporary, path)) { storage.remove(temporary); return false; }
    return true;
}
