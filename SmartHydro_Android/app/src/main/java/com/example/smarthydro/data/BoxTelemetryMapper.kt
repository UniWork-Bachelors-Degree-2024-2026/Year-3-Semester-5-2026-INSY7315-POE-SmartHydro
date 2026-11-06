package com.example.smarthydro.data

import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.models.SensorNode
import com.example.smarthydro.ui.theme.CleanWaterBlue
import com.example.smarthydro.ui.theme.HumidityOrange
import com.example.smarthydro.ui.theme.NutrientsGreen
import com.example.smarthydro.ui.theme.SunlightAmber
import com.example.smarthydro.ui.theme.TempTerracotta
import com.example.smarthydro.ui.theme.WaterBlue
import java.util.Locale

// Turns a reading straight from the box's own SoftAP into sensor cards. The box's local
// endpoint only reports raw readings, not thresholds or actuator state (that lives in the
// cloud dashboard summary), so there are no status badges or real on/off control here yet -
// only the readings a farmer can see without any cloud connection at all. With no reading
// (the box has not answered), every card is built as "no reading" instead of showing sample numbers.
object BoxTelemetryMapper {

    fun sensorNodes(telemetry: BoxTelemetryDto?): List<SensorNode> = listOf(
        SensorNode(
            id = "water",
            title = "Water",
            hasReading = telemetry?.waterLevelDetected != null,
            valueDisplay = when (telemetry?.waterLevelDetected) {
                true -> "Water Detected"
                false -> "No Water Detected"
                null -> "-- "
            },
            controlLabel = "Water Pump",
            controlTag = "PUMP",
            controlOn = false,
            infoText = "Read directly from the box, not the cloud",
            accentColor = WaterBlue,
            iconType = "water_drop"
        ),
        SensorNode(
            id = "clean_water",
            title = "Clean Water",
            hasReading = telemetry?.ph != null,
            valueDisplay = telemetry?.ph?.let { String.format(Locale.US, "%.1f pH", it) } ?: "-- pH",
            controlLabel = "pH Pump",
            controlTag = "PUMP",
            controlOn = false,
            infoText = "Read directly from the box, not the cloud",
            accentColor = CleanWaterBlue,
            iconType = "ph_droplet"
        ),
        SensorNode(
            id = "temperature",
            title = "Temperature",
            hasReading = telemetry?.temperature != null,
            valueDisplay = telemetry?.temperature?.let { String.format(Locale.US, "%.0f °C", it) } ?: "-- °C",
            controlLabel = "Circulation Fans",
            controlTag = "FANS",
            controlOn = false,
            infoText = "Read directly from the box, not the cloud",
            accentColor = TempTerracotta,
            iconType = "thermometer"
        ),
        SensorNode(
            id = "sunlight",
            title = "Sunlight",
            hasReading = telemetry?.light != null,
            valueDisplay = telemetry?.light?.let { String.format(Locale.US, "%.0f %%", it) } ?: "-- %",
            controlLabel = "Lights",
            controlTag = "LIGHT",
            controlOn = false,
            infoText = "Read directly from the box, not the cloud",
            accentColor = SunlightAmber,
            iconType = "sun"
        ),
        SensorNode(
            id = "nutrients",
            title = "Nutrients (EC)",
            hasReading = telemetry?.ec != null,
            valueDisplay = telemetry?.ec?.let { String.format(Locale.US, "%.0f µS/cm", it * ThresholdCatalog.Ec.scale) } ?: "-- µS/cm",
            controlLabel = "Auto-Dosing",
            controlTag = "DOSER",
            controlOn = false,
            infoText = "Read directly from the box, not the cloud",
            accentColor = NutrientsGreen,
            iconType = "nutrients_bottle"
        ),
        SensorNode(
            id = "humidity",
            title = "Humidity",
            hasReading = telemetry?.humidity != null,
            valueDisplay = telemetry?.humidity?.let { String.format(Locale.US, "%.0f %%", it) } ?: "-- %",
            controlLabel = "Extractor Fans",
            controlTag = "FANS",
            controlOn = false,
            infoText = "Read directly from the box, not the cloud",
            accentColor = HumidityOrange,
            iconType = "humidity_droplet"
        )
    ).map { node -> node.copy(controlOn = BoxControl.isOn(node.id, telemetry?.relays)) }
}
