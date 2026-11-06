package com.example.smarthydro.data

import com.example.smarthydro.models.ActuatorCommandDto
import com.example.smarthydro.models.ActuatorStateResponseDto
import com.example.smarthydro.models.TelemetrySnapshotDto
import com.example.smarthydro.models.ThresholdResponseDto
import com.example.smarthydro.models.BadgeType
import com.example.smarthydro.models.SensorNode
import com.example.smarthydro.ui.theme.CleanWaterBlue
import com.example.smarthydro.ui.theme.HumidityOrange
import com.example.smarthydro.ui.theme.NutrientsGreen
import com.example.smarthydro.ui.theme.SunlightAmber
import com.example.smarthydro.ui.theme.TempTerracotta
import com.example.smarthydro.ui.theme.WaterBlue
import java.util.Locale

// Turns a structure's real telemetry and actuator state into the sensor cards the readings
// screen already knows how to draw. Fodder structures keep their mock sensor list for now,
// since the telemetry schema has no field that clearly stands in for tray "moisture" the way
// it does for the other hydroponic readings.
object TelemetryMapper {

    fun hydroponicSensorNodes(
        telemetry: TelemetrySnapshotDto?,
        actuators: ActuatorStateResponseDto?,
        thresholds: Map<String, ThresholdResponseDto>,
        thresholdsLoaded: Boolean = true
    ): List<SensorNode> {
        val phBadge = telemetry?.phLevel?.let { badgeFor(it, thresholds["PH"]) }
        val tempBadge = telemetry?.temperatureC?.let { badgeFor(it, thresholds["TEMPERATURE"]) }
        val ecBadge = telemetry?.ecMs?.let { badgeFor(it, thresholds["EC"]) }
        val humidityBadge = telemetry?.humidityPercent?.let { badgeFor(it, thresholds["HUMIDITY"]) }

        return listOf(
            SensorNode(
                id = "water",
                hasReading = telemetry?.waterLevelDetected != null,
                title = "Water",
                valueDisplay = when (telemetry?.waterLevelDetected) {
                    true -> "Water Detected"
                    false -> "No Water Detected"
                    null -> "-- "
                },
                statusBadgeText = null,
                controlLabel = "Water Pump",
                controlTag = "PUMP",
                controlOn = actuators?.mainWaterPump ?: false,
                infoText = if (actuators?.mainWaterPump == true) "Currently running" else "Currently off",
                accentColor = WaterBlue,
                iconType = "water_drop"
            ),
            SensorNode(
                id = "clean_water",
                hasReading = telemetry?.phLevel != null,
                title = "Clean Water",
                valueDisplay = telemetry?.phLevel?.let { String.format(Locale.US, "%.1f pH", it) } ?: "-- pH",
                statusBadgeText = phBadge?.first,
                statusBadgeType = phBadge?.second ?: BadgeType.GOOD,
                controlLabel = "pH Pump",
                controlTag = "PUMP",
                controlOn = actuators?.phUpPump == true || actuators?.phDownPump == true,
                infoText = if (!thresholdsLoaded) "Loading range…" else rangeText(thresholds["PH"], "pH"),
                accentColor = CleanWaterBlue,
                iconType = "ph_droplet"
            ),
            SensorNode(
                id = "temperature",
                hasReading = telemetry?.temperatureC != null,
                title = "Temperature",
                valueDisplay = telemetry?.temperatureC?.let { String.format(Locale.US, "%.0f °C", it) } ?: "-- °C",
                statusBadgeText = tempBadge?.first,
                statusBadgeType = tempBadge?.second ?: BadgeType.GOOD,
                controlLabel = "Circulation Fans",
                controlTag = "FANS",
                controlOn = actuators?.circulationFan ?: false,
                infoText = if (!thresholdsLoaded) "Loading range…" else rangeText(thresholds["TEMPERATURE"], "°C"),
                accentColor = TempTerracotta,
                iconType = "thermometer"
            ),
            SensorNode(
                id = "sunlight",
                hasReading = telemetry?.lightLux != null,
                title = "Sunlight",
                valueDisplay = telemetry?.lightLux?.let { String.format(Locale.US, "%.0f %%", it) } ?: "-- %",
                statusBadgeText = null,
                controlLabel = "Lights",
                controlTag = "LIGHT",
                controlOn = actuators?.growLight ?: false,
                // No threshold type exists for light yet, so this reading has no optimal range to compare against
                infoText = "Live reading, no optimal range configured yet",
                accentColor = SunlightAmber,
                iconType = "sun"
            ),
            SensorNode(
                id = "nutrients",
                hasReading = telemetry?.ecMs != null,
                title = "Nutrients (EC)",
                valueDisplay = telemetry?.ecMs?.let { String.format(Locale.US, "%.0f µS/cm", it * ThresholdCatalog.Ec.scale) } ?: "-- µS/cm",
                statusBadgeText = ecBadge?.first,
                statusBadgeType = ecBadge?.second ?: BadgeType.GOOD,
                controlLabel = "Auto-Dosing",
                controlTag = "DOSER",
                controlOn = actuators?.nutrientUpPump == true || actuators?.dilutionDownPump == true,
                infoText = if (!thresholdsLoaded) "Loading range…" else rangeText(thresholds["EC"], "µS/cm", ThresholdCatalog.Ec.scale),
                accentColor = NutrientsGreen,
                iconType = "nutrients_bottle"
            ),
            SensorNode(
                id = "humidity",
                hasReading = telemetry?.humidityPercent != null,
                title = "Humidity",
                valueDisplay = telemetry?.humidityPercent?.let { String.format(Locale.US, "%.0f %%", it) } ?: "-- %",
                statusBadgeText = humidityBadge?.first,
                statusBadgeType = humidityBadge?.second ?: BadgeType.GOOD,
                controlLabel = "Extractor Fans",
                controlTag = "FANS",
                controlOn = actuators?.extractorFan ?: false,
                infoText = if (!thresholdsLoaded) "Loading range…" else rangeText(thresholds["HUMIDITY"], "%"),
                accentColor = HumidityOrange,
                iconType = "humidity_droplet"
            )
        )
    }

    // Builds the single-field command for a sensor card's on/off switch. pH and nutrients
    // each drive two independent pumps (up/down) on the real hardware, but the card only has
    // one switch, so it controls the "up" pump; the other pump isn't reachable from this
    // switch, only from the dedicated dosing controls once those are wired to a real endpoint.
    fun continuousCommandFor(sensorId: String, deviceMac: String, turnOn: Boolean): ActuatorCommandDto? = when (sensorId) {
        "water" -> ActuatorCommandDto(deviceMac = deviceMac, mainWaterPump = turnOn)
        "clean_water" -> ActuatorCommandDto(deviceMac = deviceMac, phUpPump = turnOn)
        "temperature" -> ActuatorCommandDto(deviceMac = deviceMac, circulationFan = turnOn)
        "sunlight" -> ActuatorCommandDto(deviceMac = deviceMac, growLight = turnOn)
        "nutrients" -> ActuatorCommandDto(deviceMac = deviceMac, nutrientUpPump = turnOn)
        "humidity" -> ActuatorCommandDto(deviceMac = deviceMac, extractorFan = turnOn)
        else -> null
    }

    // Good when the reading is within the configured range, Caution otherwise
    private fun badgeFor(value: Double, threshold: ThresholdResponseDto?): Pair<String, BadgeType>? {
        if (threshold == null) return null
        return if (value in threshold.minValue..threshold.maxValue) {
            "Good" to BadgeType.GOOD
        } else {
            "Caution" to BadgeType.CAUTION
        }
    }

    // scale turns the API's unit into the shown one
    private fun rangeText(threshold: ThresholdResponseDto?, unit: String, scale: Double = 1.0): String {
        if (threshold == null) return "No optimal range configured yet"
        return "Optimal Range: ${formatNumber(threshold.minValue * scale)}-${formatNumber(threshold.maxValue * scale)}$unit"
    }

    private fun formatNumber(value: Double): String =
        ThresholdEditor.show(value)
}
