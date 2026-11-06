package com.example.smarthydro

import com.example.smarthydro.data.HistoryLogic
import com.example.smarthydro.data.TrendMapper
import com.example.smarthydro.data.local.DailyAverageRow
import com.example.smarthydro.models.TelemetryDailyHistoryDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class TrendMapperTest {

    // A Wednesday, so the seven days run Thursday to Wednesday
    private val today = LocalDate.of(2026, 10, 7)

    @Test
    fun fromApi_alwaysReturnsTheLastSevenDaysEndingToday() {
        val points = TrendMapper.fromApi("clean_water", emptyList(), today)

        assertEquals(listOf("Thu", "Fri", "Sat", "Sun", "Mon", "Tue", "Today"), points.map { it.label })
    }

    @Test
    fun fromApi_placesEachDayOnItsOwnBarAndLeavesGapsForMissingDays() {
        val history = listOf(
            apiRow("2026-10-05T00:00:00", ph = 6.1),
            apiRow("2026-10-07", ph = 6.34)
        )
        val points = TrendMapper.fromApi("clean_water", history, today)

        assertEquals(6.1f, points[4].value!!, 0.0001f)
        assertEquals("6.1", points[4].valueText)
        assertEquals(6.3f, points[6].value!!, 0.05f)
        assertEquals("6.3", points[6].valueText)

        assertNull(points[5].value)
        assertEquals("–", points[5].valueText)
        assertEquals(5, points.count { it.value == null })
    }

    @Test
    fun fromApi_aDayTheCloudHasNoAverageForIsAGapNotAZero() {
        val points = TrendMapper.fromApi("clean_water", listOf(apiRow("2026-10-07", ph = null)), today)

        assertNull(points[6].value)
    }

    @Test
    fun fromApi_aRealZeroIsKeptAsAZero() {
        val points = TrendMapper.fromApi("sunlight", listOf(apiRow("2026-10-07", light = 0.0)), today)

        assertEquals(0f, points[6].value!!, 0f)
        assertEquals("0", points[6].valueText)
    }

    @Test
    fun fromApi_picksTheRightFieldAndPrecisionForEachSensor() {
        val history = listOf(apiRow("2026-10-07", ph = 6.2, ec = 1.4, light = 5300.0, temp = 24.6, humidity = 61.4))

        assertEquals("6.2", TrendMapper.fromApi("clean_water", history, today)[6].valueText)
        assertEquals("1400", TrendMapper.fromApi("nutrients", history, today)[6].valueText)
        assertEquals("5300", TrendMapper.fromApi("sunlight", history, today)[6].valueText)
        assertEquals("25", TrendMapper.fromApi("temperature", history, today)[6].valueText)
        assertEquals("61", TrendMapper.fromApi("humidity", history, today)[6].valueText)
    }

    @Test
    fun fromApi_aSensorWithNoDailyHistoryGivesNoPoints() {
        assertTrue(TrendMapper.fromApi("water", listOf(apiRow("2026-10-07", ph = 6.0)), today).isEmpty())
        assertTrue(TrendMapper.fromApi("moisture", emptyList(), today).isEmpty())
    }

    @Test
    fun fromApi_ignoresADateItCannotRead() {
        val points = TrendMapper.fromApi("clean_water", listOf(apiRow("not a date", ph = 6.0)), today)

        assertEquals(7, points.size)
        assertTrue(points.all { it.value == null })
    }

    @Test
    fun fromLocal_usesTheDaysItIsGivenAndLabelsToday() {
        val days = HistoryLogic.lastDays(
            today, 7,
            listOf(localRow("2026-10-06", ph = 6.5), localRow("2026-10-07", ph = 6.7))
        )
        val points = TrendMapper.fromLocal("clean_water", days, today)

        assertEquals(7, points.size)
        assertEquals("Tue", points[5].label)
        assertEquals("6.5", points[5].valueText)
        assertEquals("Today", points[6].label)
        assertEquals("6.7", points[6].valueText)
        assertEquals(5, points.count { it.value == null })
    }

    @Test
    fun fromLocal_aSensorThatNeverReportedIsAllGaps() {
        val days = HistoryLogic.lastDays(today, 7, listOf(localRow("2026-10-07", ph = 6.7)))
        val points = TrendMapper.fromLocal("nutrients", days, today)

        assertTrue(points.all { it.value == null })
    }

    private fun apiRow(
        date: String,
        ph: Double? = 6.0,
        ec: Double? = 1.0,
        light: Double? = 100.0,
        temp: Double? = 22.0,
        humidity: Double? = 55.0
    ) = TelemetryDailyHistoryDto(
        id = 1L, deviceMac = "A4:CF:12:9B:3E:01", summaryDate = date,
        avgPh = ph, avgEc = ec, avgLight = light, avgTemperatureC = temp, avgHumidityPercent = humidity,
        totalSamples = 100
    )

    private fun localRow(day: String, ph: Double?) = DailyAverageRow(
        day = day, avgPh = ph, avgEc = null, avgLight = null,
        avgTemperature = null, avgHumidity = null, samples = 12
    )
}
