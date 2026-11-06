#pragma once
#include <LittleFS.h>
#include <esp_partition.h>
#include "OutboxFileOps.h"

static bool outboxFilesReady = false;
static void beginOutboxFiles() {
    outboxFilesReady = LittleFS.begin(false);
    if (!outboxFilesReady) {
        const esp_partition_t* partition = esp_partition_find_first(ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_DATA_SPIFFS, "spiffs");
        bool blank = partition != nullptr; uint8_t bytes[1024];
        for (size_t offset = 0; blank && offset < partition->size; offset += sizeof(bytes)) {
            size_t count = partition->size - offset > sizeof(bytes) ? sizeof(bytes) : partition->size - offset;
            if (esp_partition_read(partition, offset, bytes, count) != ESP_OK) { blank = false; break; }
            for (size_t i = 0; i < count; ++i) if (bytes[i] != 0xFF) { blank = false; break; }
            yield();
        }
        // Never format an existing or damaged filesystem automatically.
        if (blank && LittleFS.format()) outboxFilesReady = LittleFS.begin(false);
    }
    if (outboxFilesReady && !LittleFS.exists("/sh-outbox")) outboxFilesReady = LittleFS.mkdir("/sh-outbox");
    Serial.println(outboxFilesReady ? "[Storage] Outbox filesystem ready; NVS is reserved for settings, bonds and safety." :
        "[Storage] Outbox filesystem unavailable. Existing data was not erased; using legacy NVS storage.");
}
static void outboxPath(const char* key, char (&path)[40]) { snprintf(path, sizeof(path), "/sh-outbox/%s.bin", key); }
static bool readOutboxFile(const char* key, void* value, size_t length) {
    if (!outboxFilesReady) return false;
    char path[40]; outboxPath(key, path); if (!LittleFS.exists(path)) return false;
    auto file = LittleFS.open(path, "r");
    if (!file) return false;
    bool valid = file.size() == length && file.read(static_cast<uint8_t*>(value), length) == length;
    file.close(); return valid;
}
static bool writeOutboxFile(const char* key, const void* value, size_t length) {
    if (!outboxFilesReady) return false;
    char path[40], temporary[48]; outboxPath(key, path); snprintf(temporary, sizeof(temporary), "%s.tmp", path);
    return shCommitOutbox(LittleFS, path, temporary, value, length);
}
static bool removeOutboxFile(const char* key) {
    if (!outboxFilesReady) return true;
    char path[40]; outboxPath(key, path); return !LittleFS.exists(path) || LittleFS.remove(path);
}
