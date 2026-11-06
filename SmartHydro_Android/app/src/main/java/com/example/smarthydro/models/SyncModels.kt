package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 10. DATA-MULE OFFLINE SYNC MODELS (/api/Sync)

// Watermark timestamp response used to prevent duplicate offline data sync
data class HighWaterMarkResponseDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("latestTimestamp") val latestTimestamp: String?,
    @SerializedName("totalStoredReadings") val totalStoredReadings: Int
)

// Request batch containing cached offline telemetry readings to sync
data class TelemetryBatchSyncRequestDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("readings") val readings: List<TelemetryBatchItemDto>
)

// Individual sensor reading captured offline
data class TelemetryBatchItemDto(
    @SerializedName("ph") val ph: Double,
    @SerializedName("ec") val ec: Double? = null,
    @SerializedName("light") val light: Double,
    @SerializedName("waterLevelRaw") val waterLevelRaw: Int,
    @SerializedName("waterLevelDetected") val waterLevelDetected: Boolean,
    @SerializedName("temperature") val temperature: Double,
    @SerializedName("humidity") val humidity: Double,
    @SerializedName("ml_ph") val mlPh: Int = 0,
    @SerializedName("ml_ec") val mlEc: Int = 0,
    @SerializedName("ml_temp") val mlTemp: Int = 0,
    @SerializedName("ml_humidity") val mlHumidity: Int = 0,
    @SerializedName("recordedAt") val recordedAt: String
)

// Batch sync outcome report for telemetry readings
data class TelemetryBatchSyncResponseDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("totalReceived") val totalReceived: Int,
    @SerializedName("inserted") val inserted: Int,
    @SerializedName("skipped") val skipped: Int,
    @SerializedName("latestStoredTimestamp") val latestStoredTimestamp: String?
)

// Request batch containing alerts recorded offline
data class AlertsBatchSyncRequestDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("structureId") val structureId: Int? = null,
    @SerializedName("alerts") val alerts: List<AlertBatchItemDto>
)

// Individual alert recorded offline
data class AlertBatchItemDto(
    @SerializedName("component") val component: String,
    @SerializedName("severity") val severity: String = "WARNING",
    @SerializedName("value") val value: String? = null,
    @SerializedName("detail") val detail: String? = null,
    @SerializedName("occurredAt") val occurredAt: String
)

// Batch sync outcome report for offline alerts
data class AlertsBatchSyncResponseDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("totalReceived") val totalReceived: Int,
    @SerializedName("inserted") val inserted: Int,
    @SerializedName("skipped") val skipped: Int
)

// Request batch containing manual actuator overrides executed on-site while offline
data class OverridesBatchSyncRequestDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("overrides") val overrides: List<OverrideBatchItemDto>
)

// Individual manual actuator toggle executed offline
data class OverrideBatchItemDto(
    @SerializedName("actuatorName") val actuatorName: String,
    @SerializedName("newState") val newState: Boolean,
    @SerializedName("changedBy") val changedBy: String = "Android_Mule_Worker",
    @SerializedName("executedAt") val executedAt: String
)

// Batch sync outcome report for offline overrides
data class OverridesBatchSyncResponseDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("totalReceived") val totalReceived: Int,
    @SerializedName("inserted") val inserted: Int,
    @SerializedName("skipped") val skipped: Int
)
