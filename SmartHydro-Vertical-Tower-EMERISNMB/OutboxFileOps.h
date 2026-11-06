#pragma once
#include <stdint.h>
#include <stddef.h>
#include <string.h>

// Keep the previous record until a complete replacement has been checked.
template<class Storage>
static bool shCommitOutbox(Storage& storage, const char* path, const char* temporary, const void* bytes, size_t length) {
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
