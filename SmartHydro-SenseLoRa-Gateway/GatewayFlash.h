#pragma once
#include <LittleFS.h>
#include <esp_partition.h>

static bool mountGatewayFilesystem() {
    if (LittleFS.begin(false)) return true;
    const esp_partition_t* partition = esp_partition_find_first(ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_DATA_SPIFFS, "spiffs");
    if (!partition) return false;
    uint8_t bytes[1024];
    for (size_t offset = 0; offset < partition->size; offset += sizeof(bytes)) {
        size_t count = partition->size - offset > sizeof(bytes) ? sizeof(bytes) : partition->size - offset;
        if (esp_partition_read(partition, offset, bytes, count) != ESP_OK) return false;
        for (size_t i = 0; i < count; ++i) if (bytes[i] != 0xFF) return false;
        feedGatewayWatchdog();
    }
    // Initialise erased flash only. A damaged backlog needs recovery, not automatic erasure.
    return LittleFS.format() && LittleFS.begin(false);
}
