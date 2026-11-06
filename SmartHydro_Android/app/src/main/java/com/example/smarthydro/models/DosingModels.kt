package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 7. DOSING MODELS (/api/Dosing)

// Request payload for enqueueing a one-shot volumetric peristaltic dosing command
data class DosingCommandRequestDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("dosingType") val dosingType: String,
    @SerializedName("targetVolumeMl") val targetVolumeMl: Double,
    @SerializedName("issuedBy") val issuedBy: String? = "AndroidApp"
)

// Hardware callback payload reporting execution outcome of a dosing pulse
data class DosingCallbackRequestDto(
    @SerializedName("commandId") val commandId: String,
    @SerializedName("status") val status: String = "COMPLETED",
    @SerializedName("executedAt") val executedAt: String? = null
)

// Details of a queued or completed volumetric dosing command
data class DosingCommandResponseDto(
    @SerializedName("id") val id: String,
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("dosingType") val dosingType: String,
    @SerializedName("targetVolumeMl") val targetVolumeMl: Double,
    @SerializedName("calculatedDurationMs") val calculatedDurationMs: Int,
    @SerializedName("status") val status: String,
    @SerializedName("createdAt") val createdAt: String,
    @SerializedName("executedAt") val executedAt: String?,
    @SerializedName("issuedBy") val issuedBy: String?
)
