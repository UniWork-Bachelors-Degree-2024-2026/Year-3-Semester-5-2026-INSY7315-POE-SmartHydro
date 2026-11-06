package com.example.smarthydro.data.local

import androidx.room.Entity

// One sensor reading, cached locally so it survives the app being closed. Readings from a
// box (over its own hotspot, no internet) and readings from the cloud both end up here,
// keyed the same way the API's own dedupe does: one device can only have one reading at
// a given moment. isSynced tracks whether the cloud has this reading yet, so a farmer's
// phone can carry it until it does, rather than losing it if it is closed too soon.
// A sensor that gave no reading is null rather than a made-up zero.
@Entity(tableName = "cached_telemetry_readings", primaryKeys = ["deviceMac", "recordedAtEpochMs"])
data class CachedTelemetryReading(
    val deviceMac: String,
    val recordedAtEpochMs: Long,
    val ph: Double?,
    val ec: Double?,
    val temperature: Double?,
    val humidity: Double?,
    val light: Double?,
    val waterLevelRaw: Int,
    val waterLevelDetected: Boolean,
    val mlPh: Int,
    val mlEc: Int,
    val mlTemp: Int,
    val mlHumidity: Int,
    val isSynced: Boolean
)
