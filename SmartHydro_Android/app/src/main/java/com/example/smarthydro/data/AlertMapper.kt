package com.example.smarthydro.data

import androidx.compose.ui.graphics.Color
import com.example.smarthydro.models.AlertItem
import com.example.smarthydro.models.AlertResponseDto
import com.example.smarthydro.models.AlertSeverity
import com.example.smarthydro.ui.theme.CleanWaterBlue
import com.example.smarthydro.ui.theme.ForestGreen
import com.example.smarthydro.ui.theme.HumidityOrange
import com.example.smarthydro.ui.theme.MoistureBlue
import com.example.smarthydro.ui.theme.NutrientsGreen
import com.example.smarthydro.ui.theme.SunlightAmber
import com.example.smarthydro.ui.theme.TempTerracotta
import com.example.smarthydro.ui.theme.WaterBlue
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter

// The icon, colour and sensor detail screen for an alert
private data class AlertStyle(val iconType: String, val accentColor: Color, val sensorId: String?)

// Same names as MVC's ComponentNames
private val componentNames = mapOf(
    "PH_SENSOR" to "pH Sensor",
    "EC_SENSOR" to "EC Sensor",
    "TEMPERATURE_SENSOR" to "Temperature Sensor",
    "WATER_RESERVOIR" to "Water Reservoir"
)

private val timeFormatter = DateTimeFormatter.ofPattern("d MMM, h:mm a")

// Converts one alert as the API returns it into the shape the alert screens expect
fun AlertResponseDto.toAlertItem(): AlertItem {
    val style = styleFor(component)
    val happenedAt = parseOccurredAt(occurredAt)
    val location = structureName?.takeIf { it.isNotBlank() } ?: "Unknown structure"

    return AlertItem(
        id = id.toString(),
        title = displayName(component),
        timeAndLocation = "${happenedAt?.format(timeFormatter) ?: occurredAt} · $location",
        accentColor = style.accentColor,
        iconType = style.iconType,
        targetSensorId = style.sensorId,
        structureId = structureId.toString(),
        severity = if (severity.equals("CRITICAL", ignoreCase = true)) AlertSeverity.ALERT else AlertSeverity.CAUTION,
        isResolved = isResolved,
        value = value ?: "",
        detail = detail ?: "",
        clearedBy = clearedBy,
        location = location,
        occurredAt = happenedAt
    )
}

// "pH Sensor"
private fun displayName(component: String): String {
    if (component.isBlank()) return "System"
    componentNames[component.uppercase()]?.let { return it }
    return component.split('_', ' ')
        .filter { it.isNotBlank() }
        .joinToString(" ") { word -> word.lowercase().replaceFirstChar { it.uppercase() } }
}

private fun styleFor(component: String): AlertStyle {
    val name = component.uppercase()
    return when {
        "PH" in name -> AlertStyle("ph_droplet", CleanWaterBlue, "clean_water")
        "EC" in name || "NUTRIENT" in name -> AlertStyle("nutrients_bottle", NutrientsGreen, "nutrients")
        "TEMP" in name -> AlertStyle("thermometer", TempTerracotta, "temperature")
        "HUMID" in name -> AlertStyle("humidity", HumidityOrange, "humidity")
        "LIGHT" in name || "LUX" in name -> AlertStyle("sun", SunlightAmber, "light")
        "SPRAY" in name -> AlertStyle("sprinkler", WaterBlue, null)
        "MOIST" in name -> AlertStyle("water_drop", MoistureBlue, null)
        "WATER" in name || "PUMP" in name -> AlertStyle("water_drop", WaterBlue, "water")
        else -> AlertStyle("system", ForestGreen, null)
    }
}

// The API sends UTC times, sometimes without a zone marker, so both shapes are accepted and
// shown in the phone's own time zone
private fun parseOccurredAt(value: String): ZonedDateTime? {
    val zone = ZoneId.systemDefault()
    return try {
        Instant.parse(value).atZone(zone)
    } catch (e: Exception) {
        try {
            LocalDateTime.parse(value).atZone(ZoneOffset.UTC).withZoneSameInstant(zone)
        } catch (e: Exception) {
            null
        }
    }
}