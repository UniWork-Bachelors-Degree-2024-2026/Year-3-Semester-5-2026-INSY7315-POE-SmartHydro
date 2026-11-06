package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// Matches the JSON the box's own SoftAP serves at /api/telemetry/live (see Provisioning.h,
// startOfflineSoftAP). Field names deliberately mirror the firmware's own JSON keys.
data class BoxTelemetryDto(
    @SerializedName("ph") val ph: Double?,
    @SerializedName("ec") val ec: Double?,
    @SerializedName("temperature") val temperature: Double?,
    @SerializedName("humidity") val humidity: Double?,
    @SerializedName("light") val light: Double?,
    @SerializedName("waterLevelRaw") val waterLevelRaw: Int?,
    @SerializedName("waterLevelDetected") val waterLevelDetected: Boolean?,
    @SerializedName("mlPh") val mlPh: Int,
    @SerializedName("mlEc") val mlEc: Int,
    @SerializedName("mlTemp") val mlTemp: Int,
    @SerializedName("mlHumidity") val mlHumidity: Int,
    // Which relays are on. A box without local control leaves this out, so it is optional
    @SerializedName("relays") val relays: BoxRelaysDto? = null
)

// Matches /api/sync/bundle - the box's buffered offline readings, field names as the
// firmware sends them (shorter/different from BoxTelemetryDto's own field names)
data class BoxSyncBundleDto(
    @SerializedName("records") val records: List<BoxSyncRecordDto>
)

data class BoxSyncRecordDto(
    @SerializedName("timestamp") val timestamp: Long,
    @SerializedName("ph") val ph: Double,
    @SerializedName("ec") val ec: Double,
    @SerializedName("temp") val temp: Double,
    @SerializedName("hum") val hum: Double,
    @SerializedName("lux") val lux: Double,
    @SerializedName("waterRaw") val waterRaw: Int,
    @SerializedName("waterOk") val waterOk: Boolean
)


// The relays of a box as it reports them. Every field is optional because an older box leaves some or all of them out
data class BoxRelaysDto(
    @SerializedName("growLight") val growLight: Boolean? = null,
    @SerializedName("circulationFan") val circulationFan: Boolean? = null,
    @SerializedName("mainWaterPump") val mainWaterPump: Boolean? = null,
    @SerializedName("extractorFan") val extractorFan: Boolean? = null,
    @SerializedName("phUpPump") val phUpPump: Boolean? = null,
    @SerializedName("phDownPump") val phDownPump: Boolean? = null,
    @SerializedName("nutrientUpPump") val nutrientUpPump: Boolean? = null,
    @SerializedName("dilutionDownPump") val dilutionDownPump: Boolean? = null
)

// Body of POST /api/control on the box: one relay to switch on or off
data class BoxControlRequestDto(
    @SerializedName("actuator") val actuator: String,
    @SerializedName("state") val state: Boolean
)

// What the box answers to a control request: whether it did it, and the relays as they are afterwards
data class BoxControlResponseDto(
    @SerializedName("ok") val ok: Boolean? = null,
    @SerializedName("relays") val relays: BoxRelaysDto? = null,
    @SerializedName("message") val message: String? = null
)

// Body of POST /api/dose on the box: one dose pump to run for a set volume
data class BoxDoseRequestDto(
    @SerializedName("type") val type: String,
    @SerializedName("volumeMl") val volumeMl: Double
)

// What the box answers to a dose request: whether it started, and how long the pump runs
data class BoxDoseResponseDto(
    @SerializedName("ok") val ok: Boolean? = null,
    @SerializedName("durationMs") val durationMs: Int? = null,
    @SerializedName("message") val message: String? = null
)
