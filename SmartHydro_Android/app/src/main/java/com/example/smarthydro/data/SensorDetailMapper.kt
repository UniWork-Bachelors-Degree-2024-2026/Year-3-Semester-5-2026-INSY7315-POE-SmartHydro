package com.example.smarthydro.data

import com.example.smarthydro.models.ActuatorHistoryDto
import com.example.smarthydro.models.ActuatorStateResponseDto
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.models.TelemetrySnapshotDto
import com.example.smarthydro.models.ThresholdResponseDto
import com.example.smarthydro.models.ActivityItem
import com.example.smarthydro.models.BadgeType
import com.example.smarthydro.models.SensorDetailData
import com.example.smarthydro.ui.theme.CleanWaterBlue
import com.example.smarthydro.ui.theme.HumidityOrange
import com.example.smarthydro.ui.theme.NutrientsGreen
import com.example.smarthydro.ui.theme.SunlightAmber
import com.example.smarthydro.ui.theme.TempTerracotta
import com.example.smarthydro.ui.theme.WaterBlue
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

// Builds a live Details page for the five hydroponic sensors with a clean real-data mapping.
// "Water" keeps its mock multi-metric detail page, since flow rate and reservoir level aren't
// part of the real telemetry schema, the same way fodder structures keep their mock cards.
object SensorDetailMapper {

    val supportedSensorIds = setOf("water", "clean_water", "temperature", "nutrients", "humidity", "sunlight")

    fun build(
        sensorId: String,
        telemetry: TelemetrySnapshotDto?,
        actuators: ActuatorStateResponseDto?,
        actuatorHistory: List<ActuatorHistoryDto>,
        thresholds: Map<String, ThresholdResponseDto>
    ): SensorDetailData? {
        return when (sensorId) {
            "clean_water" -> {
                val threshold = thresholds["PH"]
                val badge = telemetry?.phLevel?.let { badgeFor(it, threshold) }
                SensorDetailData(
                    sensorId = sensorId,
                    title = "Clean Water (pH)",
                    toggleLabel = "pH Dosing Pump",
                    toggleOn = actuators?.phUpPump == true || actuators?.phDownPump == true,
                    isDosingControl = true,
                    hasMinusButton = true,
                    hasPlusButton = true,
                    currentValue = telemetry?.phLevel?.let { format(it, 1) } ?: "--",
                    hasReading = telemetry?.phLevel != null,
                    unit = "pH",
                    gaugeSublabel = "Potential of Hydrogen",
                    gaugeProgress = progressFor(telemetry?.phLevel, threshold),
                    gaugeBandStart = bandStart(threshold),
                    gaugeBandEnd = bandEnd(threshold),
                    statusBadge = badge?.first,
                    statusBadgeType = badge?.second ?: BadgeType.GOOD,
                    optimalRangeText = rangeText(threshold, "pH"),
                    accentColor = CleanWaterBlue,
                    recentActivities = activities(actuatorHistory, "ph_up_pump", "ph_down_pump")
                )
            }
            "temperature" -> {
                val threshold = thresholds["TEMPERATURE"]
                val badge = telemetry?.temperatureC?.let { badgeFor(it, threshold) }
                SensorDetailData(
                    sensorId = sensorId,
                    title = "Temperature",
                    toggleLabel = "Circulation Fans",
                    toggleOn = actuators?.circulationFan ?: false,
                    currentValue = telemetry?.temperatureC?.let { format(it, 0) } ?: "--",
                    hasReading = telemetry?.temperatureC != null,
                    unit = "°C",
                    gaugeSublabel = "Temperature",
                    gaugeProgress = progressFor(telemetry?.temperatureC, threshold),
                    gaugeBandStart = bandStart(threshold),
                    gaugeBandEnd = bandEnd(threshold),
                    statusBadge = badge?.first,
                    statusBadgeType = badge?.second ?: BadgeType.GOOD,
                    optimalRangeText = rangeText(threshold, "°C"),
                    accentColor = TempTerracotta,
                    recentActivities = activities(actuatorHistory, "circulation_fan")
                )
            }
            "nutrients" -> {
                val threshold = thresholds["EC"]
                val badge = telemetry?.ecMs?.let { badgeFor(it, threshold) }
                SensorDetailData(
                    sensorId = sensorId,
                    title = "Nutrients (EC)",
                    toggleLabel = "Auto-Dosing Pump",
                    toggleOn = actuators?.nutrientUpPump == true || actuators?.dilutionDownPump == true,
                    isDosingControl = true,
                    hasMinusButton = true,
                    hasPlusButton = true,
                    doseActionLabel = "Tap + to add nutrients or − to dilute",
                    currentValue = telemetry?.ecMs?.let { format(it * ThresholdCatalog.Ec.scale, 0) } ?: "--",
                    hasReading = telemetry?.ecMs != null,
                    unit = "µS/cm",
                    gaugeSublabel = "Nutrient solution",
                    gaugeProgress = progressFor(telemetry?.ecMs, threshold),
                    gaugeBandStart = bandStart(threshold),
                    gaugeBandEnd = bandEnd(threshold),
                    statusBadge = badge?.first,
                    statusBadgeType = badge?.second ?: BadgeType.GOOD,
                    optimalRangeText = rangeText(threshold, "µS/cm", ThresholdCatalog.Ec.scale),
                    accentColor = NutrientsGreen,
                    recentActivities = activities(actuatorHistory, "nutrient_up_pump", "dilution_down_pump")
                )
            }
            "humidity" -> {
                val threshold = thresholds["HUMIDITY"]
                val badge = telemetry?.humidityPercent?.let { badgeFor(it, threshold) }
                SensorDetailData(
                    sensorId = sensorId,
                    title = "Humidity",
                    toggleLabel = "Extractor Fans",
                    toggleOn = actuators?.extractorFan ?: false,
                    currentValue = telemetry?.humidityPercent?.let { format(it, 0) } ?: "--",
                    hasReading = telemetry?.humidityPercent != null,
                    unit = "%",
                    gaugeSublabel = "Humidity",
                    gaugeProgress = progressFor(telemetry?.humidityPercent, threshold),
                    gaugeBandStart = bandStart(threshold),
                    gaugeBandEnd = bandEnd(threshold),
                    statusBadge = badge?.first,
                    statusBadgeType = badge?.second ?: BadgeType.GOOD,
                    optimalRangeText = rangeText(threshold, "%"),
                    accentColor = HumidityOrange,
                    recentActivities = activities(actuatorHistory, "extractor_fan")
                )
            }
            "sunlight" -> {
                // No threshold type exists for light yet, so there's no range to badge against
                SensorDetailData(
                    sensorId = sensorId,
                    title = "Sunlight",
                    toggleLabel = "Lights",
                    toggleOn = actuators?.growLight ?: false,
                    currentValue = telemetry?.lightLux?.let { format(it, 0) } ?: "--",
                    hasReading = telemetry?.lightLux != null,
                    unit = "%",
                    gaugeSublabel = "Sunlight",
                    gaugeProgress = null,
                    statusBadge = null,
                    optimalRangeText = "No optimal range configured yet",
                    accentColor = SunlightAmber,
                    recentActivities = activities(actuatorHistory, "grow_light")
                )
            }
            "water" -> {
                // The tank sensor only says whether water is there, so this page is a status and a pump
                // switch, with no gauge and no trend
                val detected = telemetry?.waterLevelDetected
                SensorDetailData(
                    sensorId = sensorId,
                    title = "Water",
                    toggleLabel = "Water Pump",
                    toggleOn = actuators?.mainWaterPump ?: false,
                    currentValue = when (detected) {
                        true -> "Water detected"
                        false -> "No water detected"
                        null -> "--"
                    },
                    hasReading = detected != null,
                    unit = "",
                    gaugeSublabel = telemetry?.waterLevelRaw?.let { "Sensor value: $it" } ?: "",
                    gaugeProgress = null,
                    statusBadge = null,
                    statusBadgeType = if (detected == false) BadgeType.CAUTION else BadgeType.GOOD,
                    optimalRangeText = null,
                    accentColor = WaterBlue,
                    showTrend = false,
                    recentActivities = activities(actuatorHistory, "main_water_pump")
                )
            }
            else -> null
        }
    }

    // A farmer's detail page, built from a reading taken straight from their box. The box does not
    // know the configured ranges or which relays are on, so there is no range marking and no badge;
    // the arc shows where the reading sits on the sensor's own scale where it has one
    fun buildFromBox(sensorId: String, box: BoxTelemetryDto?): SensorDetailData? {
        val snapshot = box?.let {
            TelemetrySnapshotDto(
                phLevel = it.ph,
                ecMs = it.ec,
                lightLux = it.light,
                waterLevelRaw = it.waterLevelRaw,
                waterLevelDetected = it.waterLevelDetected,
                temperatureC = it.temperature,
                humidityPercent = it.humidity,
                recordedAt = ""
            )
        }
        val data = build(sensorId, snapshot, null, emptyList(), emptyMap()) ?: return null

        val value = when (sensorId) {
            "clean_water" -> box?.ph
            "temperature" -> box?.temperature
            "humidity" -> box?.humidity
            else -> null
        }

        return data.copy(
            toggleOn = BoxControl.isOn(sensorId, box?.relays),
            gaugeProgress = scaleProgress(sensorId, value),
            optimalRangeText = "Optimal range isn't available without the cloud"
        )
    }

    // Where a reading sits on the sensor's own physical scale, for the sensors that have one
    private fun scaleProgress(sensorId: String, value: Double?): Float? {
        if (value == null) return null

        val (low, high) = when (sensorId) {
            "clean_water" -> 0.0 to 14.0
            "temperature" -> 0.0 to 50.0
            "humidity" -> 0.0 to 100.0
            else -> return null
        }
        return (((value - low) / (high - low)).coerceIn(0.0, 1.0)).toFloat()
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

    // Normalises a value onto the gauge's 0..1 arc, padding the configured range by half its
    // own width on each side so a reading near the edge doesn't sit right at the arc's end.
    // Without a reading or a range there is nothing to place, so the arc is left empty
    private fun progressFor(value: Double?, threshold: ThresholdResponseDto?): Float? {
        if (value == null || threshold == null) return null
        val span = (threshold.maxValue - threshold.minValue).coerceAtLeast(0.001)
        val padding = span / 2
        val low = threshold.minValue - padding
        val high = threshold.maxValue + padding
        return (((value - low) / (high - low)).coerceIn(0.0, 1.0)).toFloat()
    }

    // Because progressFor pads the range by half its width each side, the configured range
    // always sits on the middle half of the arc
    private fun bandStart(threshold: ThresholdResponseDto?): Float? = if (threshold == null) null else 0.25f

    private fun bandEnd(threshold: ThresholdResponseDto?): Float? = if (threshold == null) null else 0.75f

    // scale turns the API's unit into the shown one
    private fun rangeText(threshold: ThresholdResponseDto?, unit: String, scale: Double = 1.0): String {
        if (threshold == null) return "No optimal range configured yet"
        return "Optimal Range: ${formatNumber(threshold.minValue * scale)}-${formatNumber(threshold.maxValue * scale)}$unit"
    }

    private fun formatNumber(value: Double): String = ThresholdEditor.show(value)

    private fun format(value: Double, decimals: Int): String =
        String.format(Locale.US, "%.${decimals}f", value)

    // The day and time of a state change. A change from earlier than today says which day, so it
    // is never mistaken for one that just happened
    fun activityTimeText(
        at: Instant,
        now: Instant = Instant.now(),
        zone: ZoneId = ZoneId.systemDefault()
    ): String {
        val date = at.atZone(zone).toLocalDate()
        val today = now.atZone(zone).toLocalDate()
        val time = DateTimeFormatter.ofPattern("h:mm a", Locale.ENGLISH).format(at.atZone(zone))

        return when (date) {
            today -> "Today, $time"
            today.minusDays(1) -> "Yesterday, $time"
            else -> date.format(DateTimeFormatter.ofPattern("EEE d MMM", Locale.ENGLISH)) + ", " + time
        }
    }

    // The most recent real state changes for the actuators this sensor's control drives
    private fun activities(actuatorHistory: List<ActuatorHistoryDto>, vararg actuatorNames: String): List<ActivityItem> {
        return actuatorHistory
            .filter { it.actuatorName in actuatorNames }
            .mapNotNull { entry ->
                // An entry with no readable time cannot be placed, so it is left out rather than shown wrongly
                val at = try {
                    Instant.parse(entry.timestamp ?: return@mapNotNull null)
                } catch (e: Exception) {
                    return@mapNotNull null
                }
                at to entry
            }
            .sortedByDescending { it.first }
            .take(5)
            .map { (at, entry) ->
                ActivityItem(
                    activityTimeText(at),
                    "${friendlyActuatorName(entry.actuatorName)} turned ${if (entry.newState) "ON" else "OFF"}"
                )
            }
    }

    private fun friendlyActuatorName(name: String): String = when (name) {
        "grow_light" -> "Grow lights"
        "circulation_fan" -> "Circulation fans"
        "main_water_pump" -> "Water pump"
        "extractor_fan" -> "Extractor fans"
        "ph_up_pump" -> "pH up pump"
        "ph_down_pump" -> "pH down pump"
        "nutrient_up_pump" -> "Nutrient pump"
        "dilution_down_pump" -> "Dilution pump"
        "spray_system" -> "Spray system"
        else -> name.replace('_', ' ').replaceFirstChar { it.uppercase() }
    }
}