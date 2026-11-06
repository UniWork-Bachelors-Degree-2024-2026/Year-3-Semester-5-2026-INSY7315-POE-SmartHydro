#pragma once

static bool saveTowerReceipt(size_t index, const SHTowerCommandOutbox<String>::Entry& entry) {
    char key[5]; snprintf(key, sizeof(key), "c%02u", (unsigned)index);
    if (!outboxFilesReady) {
        Serial.println("[Command ACK] Flash is unavailable; this receipt remains in RAM only.");
        return false;
    }
    if (!entry.cid) {
        if (removeOutboxFile(key)) return true;
        Serial.println("[Command ACK] Could not retire a confirmed flash receipt; it may replay after restart.");
        return false;
    }
    SHTowerReceiptRecord record{};
    if (entry.payload.length() >= sizeof(record.payload)) {
        Serial.println("[Command ACK] Receipt exceeds flash record size; keeping it in RAM."); return false;
    }
    record.cid = entry.cid; record.phase = entry.phase;
    memcpy(record.payload, entry.payload.c_str(), entry.payload.length());
    record.checksum = shTowerReceiptChecksum(record);
    if (writeOutboxFile(key, &record, sizeof(record))) return true;
    Serial.println("[Command ACK] Flash write failed; pending receipt remains in RAM and will retry.");
    return false;
}

static void beginTowerReceiptStorage() {
    commandAckOutbox.attachStorage(saveTowerReceipt);
    unsigned restored = 0;
    for (size_t i = 0; i < 16; ++i) {
        char key[5]; snprintf(key, sizeof(key), "c%02u", (unsigned)i);
        SHTowerReceiptRecord record{};
        if (!readOutboxFile(key, &record, sizeof(record))) {
            char path[40]; outboxPath(key, path);
            if (outboxFilesReady && LittleFS.exists(path)) {
                commandAckOutbox.reserveSlot(i);
                Serial.printf("[Command ACK] Unreadable receipt %s retained for investigation.\n", key);
            }
            continue;
        }
        if (!shValidTowerReceipt(record)) {
            commandAckOutbox.reserveSlot(i);
            Serial.printf("[Command ACK] Damaged receipt %s retained for investigation.\n", key); continue;
        }
        StaticJsonDocument<512> ack;
        bool parsed = deserializeJson(ack, record.payload) == DeserializationError::Ok;
        const char* phase = ack["phase"] | "";
        bool validPhase = !strcmp(phase, "accepted") || !strcmp(phase, "completed") || !strcmp(phase, "cancelled");
        if (!parsed || !validPhase ||
            !isMatchingNodeMac(ack["m"] | "") || (ack["cid"] | 0) != record.cid ||
            commandPhaseCode(ack["phase"] | "") != record.phase || (ack["cack"] | 0) != 1 ||
            !ack["ok"].is<int>() || (ack["ok"].as<int>() != 0 && ack["ok"].as<int>() != 1)) {
            commandAckOutbox.reserveSlot(i);
            Serial.printf("[Command ACK] Invalid receipt %s retained for investigation.\n", key); continue;
        }
        if (commandAckOutbox.restore(i, record.cid, record.phase, String(record.payload), millis())) ++restored;
    }
    Serial.printf("[Command ACK] Restored %u pending receipts; unfinished doses remain unconfirmed.\n", restored);
}
