package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 4. ACTUATORS MODELS (/api/Actuators)

// Request payload for continuous manual actuator commands
data class ActuatorCommandDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("growLight") val growLight: Boolean? = null,
    @SerializedName("circulationFan") val circulationFan: Boolean? = null,
    @SerializedName("mainWaterPump") val mainWaterPump: Boolean? = null,
    @SerializedName("extractorFan") val extractorFan: Boolean? = null,
    @SerializedName("phUpPump") val phUpPump: Boolean? = null,
    @SerializedName("phDownPump") val phDownPump: Boolean? = null,
    @SerializedName("nutrientUpPump") val nutrientUpPump: Boolean? = null,
    @SerializedName("dilutionDownPump") val dilutionDownPump: Boolean? = null,
    @SerializedName("spraySystem") val spraySystem: Boolean? = null,
    @SerializedName("changedBy") val changedBy: String = "AndroidApp"
)

// Request payload for one-shot timed actuator commands (e.g. Fodder mist spray)
data class TimedActuatorCommandDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("actuatorName") val actuatorName: String = "SpraySystem",
    @SerializedName("durationSeconds") val durationSeconds: Int = 8,
    @SerializedName("changedBy") val changedBy: String = "AndroidApp"
)

// Current state response for all 9 relay output channels on an edge controller
data class ActuatorStateResponseDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("growLight") val growLight: Boolean,
    @SerializedName("circulationFan") val circulationFan: Boolean,
    @SerializedName("mainWaterPump") val mainWaterPump: Boolean,
    @SerializedName("extractorFan") val extractorFan: Boolean,
    @SerializedName("phUpPump") val phUpPump: Boolean,
    @SerializedName("phDownPump") val phDownPump: Boolean,
    @SerializedName("nutrientUpPump") val nutrientUpPump: Boolean,
    @SerializedName("dilutionDownPump") val dilutionDownPump: Boolean,
    @SerializedName("spraySystem") val spraySystem: Boolean,
    @SerializedName("updatedAt") val updatedAt: String
)

// Historical log entry of relay state changes
data class ActuatorHistoryDto(
    @SerializedName("id") val id: Long,
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("actuatorName") val actuatorName: String,
    @SerializedName("previousState") val previousState: Boolean? = null,
    @SerializedName("newState") val newState: Boolean,
    @SerializedName("changedBy") val changedBy: String? = null,
    // The API calls this field "changedAt"
    @SerializedName("changedAt") val timestamp: String? = null
)
