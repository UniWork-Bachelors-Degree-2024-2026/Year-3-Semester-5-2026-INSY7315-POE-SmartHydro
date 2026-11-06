package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 6. ALERTS MODELS (/api/Alerts)

// Request payload for raising a new sensor anomaly or threshold violation alert
data class CreateAlertDto(
    @SerializedName("structureId") val structureId: Int,
    @SerializedName("deviceMac") val deviceMac: String? = null,
    @SerializedName("component") val component: String,
    @SerializedName("severity") val severity: String = "WARNING",
    @SerializedName("value") val value: String? = null,
    @SerializedName("detail") val detail: String? = null
)

// Request payload for resolving an alert
data class ResolveAlertDto(
    @SerializedName("clearedBy") val clearedBy: String = "AndroidApp"
)

// Alert record details returned by the API
data class AlertResponseDto(
    @SerializedName("id") val id: Long,
    @SerializedName("structureId") val structureId: Int,
    @SerializedName("structureName") val structureName: String?,
    @SerializedName("deviceMac") val deviceMac: String?,
    @SerializedName("deviceName") val deviceName: String?,
    @SerializedName("component") val component: String,
    @SerializedName("severity") val severity: String,
    @SerializedName("value") val value: String?,
    @SerializedName("detail") val detail: String?,
    @SerializedName("occurredAt") val occurredAt: String,
    @SerializedName("isResolved") val isResolved: Boolean,
    @SerializedName("clearedBy") val clearedBy: String?,
    @SerializedName("resolvedAt") val resolvedAt: String?
)

// Notification badge count response for unresolved alerts
data class UnresolvedCountResponseDto(
    @SerializedName("structureId") val structureId: Int?,
    @SerializedName("unresolvedCount") val unresolvedCount: Int
)
