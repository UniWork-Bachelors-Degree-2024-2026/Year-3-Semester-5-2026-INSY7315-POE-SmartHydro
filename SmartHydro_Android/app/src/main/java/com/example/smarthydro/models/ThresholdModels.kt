package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 9. THRESHOLDS MODELS (/api/Thresholds)

// Request payload for creating or updating a sensor limit threshold
data class UpdateThresholdDto(
    @SerializedName("structureId") val structureId: Int,
    @SerializedName("sensorType") val sensorType: String = "PH",
    @SerializedName("minValue") val minValue: Double,
    @SerializedName("maxValue") val maxValue: Double
)

// Configured boundary threshold record for a structure type and sensor metric
data class ThresholdResponseDto(
    @SerializedName("id") val id: Int,
    @SerializedName("structureId") val structureId: Int = 0,
    @SerializedName("structureType") val structureType: String,
    @SerializedName("sensorType") val sensorType: String,
    @SerializedName("minValue") val minValue: Double,
    @SerializedName("maxValue") val maxValue: Double,
    @SerializedName("isDefault") val isDefault: Boolean = false
)

// The ranges of one structure. A reading that has not been customised comes back as its healthy default
data class StructureThresholdsDto(
    @SerializedName("structureId") val structureId: Int,
    @SerializedName("structureName") val structureName: String,
    @SerializedName("structureType") val structureType: String,
    @SerializedName("thresholds") val thresholds: List<ThresholdResponseDto>
)