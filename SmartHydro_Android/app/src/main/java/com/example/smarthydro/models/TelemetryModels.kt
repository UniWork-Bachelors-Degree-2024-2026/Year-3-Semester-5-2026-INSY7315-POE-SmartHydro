package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 5. TELEMETRY MODELS (/api/Telemetry)

// Payload for ingesting live sensor metrics from microcontroller edge nodes
data class TelemetryIngestDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("localIp") val localIp: String? = null,
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
    @SerializedName("ml_humidity") val mlHumidity: Int = 0
)

// Real-time sensor telemetry record returned when querying a device or structure
data class TelemetryRealtimeDto(
    @SerializedName("id") val id: Long,
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("phLevel") val phLevel: Double?,
    @SerializedName("ecMs") val ecMs: Double?,
    @SerializedName("lightLux") val lightLux: Double?,
    @SerializedName("waterLevelRaw") val waterLevelRaw: Int?,
    @SerializedName("waterLevelDetected") val waterLevelDetected: Boolean?,
    @SerializedName("temperatureC") val temperatureC: Double?,
    @SerializedName("humidityPercent") val humidityPercent: Double?,
    @SerializedName("mlPhPrediction") val mlPhPrediction: Int,
    @SerializedName("mlEcPrediction") val mlEcPrediction: Int,
    @SerializedName("mlTempPrediction") val mlTempPrediction: Int,
    @SerializedName("mlHumidityPrediction") val mlHumidityPrediction: Int,
    @SerializedName("recordedAt") val recordedAt: String
)

// Daily aggregated metrics for historical charts and trend graphs
data class TelemetryDailyHistoryDto(
    @SerializedName("id") val id: Long,
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("summaryDate") val summaryDate: String,
    @SerializedName("avgPh") val avgPh: Double?,
    @SerializedName("avgEc") val avgEc: Double?,
    @SerializedName("avgLight") val avgLight: Double?,
    @SerializedName("avgTemperatureC") val avgTemperatureC: Double?,
    @SerializedName("avgHumidityPercent") val avgHumidityPercent: Double?,
    @SerializedName("totalSamples") val totalSamples: Int
)
