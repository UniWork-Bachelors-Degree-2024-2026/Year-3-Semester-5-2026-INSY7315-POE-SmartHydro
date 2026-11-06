package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 2. STRUCTURES MODELS (/api/Structures)

// Details of a cultivation structure and its aggregated online status
data class StructureDto(
    @SerializedName("id") val id: Int,
    @SerializedName("siteId") val siteId: String? = null,
    @SerializedName("name") val name: String,
    @SerializedName("structureType") val structureType: String,
    @SerializedName("labelColour") val labelColour: String,
    @SerializedName("locationText") val locationText: String?,
    @SerializedName("latitude") val latitude: Double?,
    @SerializedName("longitude") val longitude: Double?,
    @SerializedName("createdAt") val createdAt: String,
    @SerializedName("isOnline") val isOnline: Boolean,
    @SerializedName("lastSeenAt") val lastSeenAt: String?,
    @SerializedName("cameraMac") val cameraMac: String? = null,
    @SerializedName("controllerMac") val controllerMac: String? = null
)

// Request payload for creating a new cultivation structure
data class CreateStructureDto(
    @SerializedName("name") val name: String,
    @SerializedName("structureType") val structureType: String,
    @SerializedName("labelColour") val labelColour: String,
    @SerializedName("locationText") val locationText: String? = null,
    @SerializedName("latitude") val latitude: Double? = null,
    @SerializedName("longitude") val longitude: Double? = null,
    @SerializedName("siteId") val siteId: String? = null,
    @SerializedName("cameraMac") val cameraMac: String? = null,
    @SerializedName("controllerMac") val controllerMac: String? = null
)

// Request payload for updating structure metadata
data class UpdateStructureDto(
    @SerializedName("name") val name: String,
    @SerializedName("labelColour") val labelColour: String,
    @SerializedName("locationText") val locationText: String? = null,
    @SerializedName("latitude") val latitude: Double? = null,
    @SerializedName("longitude") val longitude: Double? = null,
    @SerializedName("siteId") val siteId: String? = null,
    @SerializedName("cameraMac") val cameraMac: String? = null,
    @SerializedName("controllerMac") val controllerMac: String? = null
)

// Aggregated summary contract for one-shot structure dashboard screen loading
data class StructureDashboardSummaryDto(
    @SerializedName("structureId") val structureId: Int,
    @SerializedName("siteId") val siteId: String? = null,
    @SerializedName("structureName") val structureName: String,
    @SerializedName("structureType") val structureType: String,
    @SerializedName("labelColour") val labelColour: String,
    @SerializedName("locationText") val locationText: String?,
    @SerializedName("isOnline") val isOnline: Boolean,
    @SerializedName("latestTelemetry") val latestTelemetry: TelemetrySnapshotDto?,
    @SerializedName("actuatorState") val actuatorState: ActuatorStateResponseDto?,
    @SerializedName("unresolvedAlertsCount") val unresolvedAlertsCount: Int,
    @SerializedName("recentAlerts") val recentAlerts: List<AlertResponseDto> = emptyList(),
    @SerializedName("pendingDosingCommandsCount") val pendingDosingCommandsCount: Int,
    @SerializedName("devices") val devices: List<DeviceDto> = emptyList(),
    @SerializedName("cameraMac") val cameraMac: String? = null,
    @SerializedName("cameraDevice") val cameraDevice: DeviceDto? = null
)

// Real-time telemetry snapshot values contained within dashboard summary
data class TelemetrySnapshotDto(
    @SerializedName("phLevel") val phLevel: Double?,
    @SerializedName("ecMs") val ecMs: Double?,
    @SerializedName("lightLux") val lightLux: Double?,
    @SerializedName("waterLevelRaw") val waterLevelRaw: Int?,
    @SerializedName("waterLevelDetected") val waterLevelDetected: Boolean?,
    @SerializedName("temperatureC") val temperatureC: Double?,
    @SerializedName("humidityPercent") val humidityPercent: Double?,
    @SerializedName("recordedAt") val recordedAt: String
)
