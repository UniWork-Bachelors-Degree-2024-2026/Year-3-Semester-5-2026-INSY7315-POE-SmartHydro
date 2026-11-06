package com.example.smarthydro.data

import com.example.smarthydro.models.TelemetryDailyHistoryDto
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

// One bar of the 7-day chart. A day nothing was recorded has no value, so it is drawn as a gap
// and never as a zero
data class TrendPoint(val label: String, val value: Float?, val valueText: String)

// Turns a sensor's daily history into the seven bars the chart draws, always the last seven
// calendar days ending today, whichever source the history came from
object TrendMapper {

    const val DAYS = 7

    // How many decimals each sensor's daily average is written with
    private val decimals = mapOf(
        "clean_water" to 1,
        "temperature" to 0,
        "nutrients" to 0,
        "humidity" to 0,
        "sunlight" to 0
    )

    // The cloud and the box report EC in mS/cm, the chart shows it in µS/cm like the rest of the app
    private fun shown(sensorId: String, value: Double?): Double? =
        if (sensorId == "nutrients" && value != null) value * ThresholdCatalog.Ec.scale else value

    // Daily averages from the cloud, which only has a row for a day it received readings
    fun fromApi(
        sensorId: String,
        history: List<TelemetryDailyHistoryDto>,
        today: LocalDate = LocalDate.now()
    ): List<TrendPoint> {
        val places = decimals[sensorId] ?: return emptyList()

        val byDay = history.mapNotNull { row ->
            val day = parseDay(row.summaryDate) ?: return@mapNotNull null
            val value = when (sensorId) {
                "clean_water" -> row.avgPh
                "temperature" -> row.avgTemperatureC
                "nutrients" -> row.avgEc
                "humidity" -> row.avgHumidityPercent
                else -> row.avgLight
            }
            day to shown(sensorId, value)
        }.toMap()

        return (DAYS - 1 downTo 0).map { back ->
            val date = today.minusDays(back.toLong())
            point(date, byDay[date], places, today)
        }
    }

    // Daily averages from the readings saved on the phone, one entry per day already
    fun fromLocal(
        sensorId: String,
        days: List<HistoryLogic.DayPoint>,
        today: LocalDate = LocalDate.now()
    ): List<TrendPoint> {
        val places = decimals[sensorId] ?: return emptyList()

        return days.map { day ->
            val row = day.row
            val value = when (sensorId) {
                "clean_water" -> row?.avgPh
                "temperature" -> row?.avgTemperature
                "nutrients" -> row?.avgEc
                "humidity" -> row?.avgHumidity
                else -> row?.avgLight
            }
            point(day.date, shown(sensorId, value), places, today)
        }
    }

    private fun point(date: LocalDate, value: Double?, places: Int, today: LocalDate): TrendPoint {
        val label = if (date == today) "Today" else date.format(DateTimeFormatter.ofPattern("EEE", Locale.ENGLISH))

        return if (value == null) {
            TrendPoint(label, null, "–")
        } else {
            TrendPoint(label, value.toFloat(), String.format(Locale.US, "%.${places}f", value))
        }
    }

    private fun parseDay(raw: String): LocalDate? = try {
        LocalDate.parse(raw.take(10))
    } catch (e: Exception) {
        null
    }
}
