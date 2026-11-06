package com.example.smarthydro

import com.example.smarthydro.data.SensorDetailMapper
import com.example.smarthydro.data.StructureDataCache
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.models.TelemetrySnapshotDto
import com.example.smarthydro.models.ThresholdResponseDto
import com.example.smarthydro.models.BadgeType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.time.ZoneOffset

class WaterAndLoadingTest {

    private fun snapshot(water: Boolean?, raw: Int? = 2650) = TelemetrySnapshotDto(
        phLevel = 6.2, ecMs = 1.4, lightLux = 300.0, waterLevelRaw = raw, waterLevelDetected = water,
        temperatureC = 24.0, humidityPercent = 60.0, recordedAt = "2026-10-07T08:00:00Z"
    )

    private fun water(snapshot: TelemetrySnapshotDto?) =
        SensorDetailMapper.build("water", snapshot, null, emptyList(), emptyMap())!!

    // ---- the Water page ----

    @Test
    fun waterHasItsOwnDetailPage() {
        assertTrue("water" in SensorDetailMapper.supportedSensorIds)
    }

    @Test
    fun waterDetected_isAGoodStatusWithTheRawValue() {
        val data = water(snapshot(true))

        assertEquals("Water detected", data.currentValue)
        assertTrue(data.hasReading)
        assertEquals(BadgeType.GOOD, data.statusBadgeType)
        assertEquals("Sensor value: 2650", data.gaugeSublabel)
    }

    @Test
    fun noWater_isAReadingThatNeedsAttention() {
        val data = water(snapshot(false))

        assertEquals("No water detected", data.currentValue)
        assertTrue(data.hasReading)
        assertEquals(BadgeType.CAUTION, data.statusBadgeType)
    }

    @Test
    fun aMissingWaterSensor_hasNoReading() {
        val data = water(snapshot(null, raw = null))

        assertFalse(data.hasReading)
        assertEquals("--", data.currentValue)
        assertEquals("", data.gaugeSublabel)
    }

    @Test
    fun noTelemetryAtAll_hasNoReading() {
        assertFalse(water(null).hasReading)
    }

    @Test
    fun theWaterPageHasNoChartAndNoGauge() {
        val data = water(snapshot(true))

        assertFalse(data.showTrend)
        assertNull(data.gaugeProgress)
        assertNull(data.optimalRangeText)
    }

    @Test
    fun waterFromABox_isReadTheSameWay() {
        val box = BoxTelemetryDto(
            ph = 6.2, ec = 1.4, temperature = 24.0, humidity = 60.0, light = 300.0,
            waterLevelRaw = 10, waterLevelDetected = false, mlPh = 0, mlEc = 0, mlTemp = 0, mlHumidity = 0
        )
        val data = SensorDetailMapper.buildFromBox("water", box)!!

        assertEquals("No water detected", data.currentValue)
        assertTrue(data.hasReading)
    }

    // ---- recent activity says which day ----

    private val now = Instant.parse("2026-10-07T20:00:00Z")

    @Test
    fun activityToday_saysToday() {
        val text = SensorDetailMapper.activityTimeText(Instant.parse("2026-10-07T16:15:00Z"), now, ZoneOffset.UTC)

        assertEquals("Today, 4:15 PM", text)
    }

    @Test
    fun activityYesterday_saysYesterday() {
        val text = SensorDetailMapper.activityTimeText(Instant.parse("2026-10-06T08:05:00Z"), now, ZoneOffset.UTC)

        assertEquals("Yesterday, 8:05 AM", text)
    }

    @Test
    fun olderActivity_saysTheDay() {
        val text = SensorDetailMapper.activityTimeText(Instant.parse("2026-10-03T16:15:00Z"), now, ZoneOffset.UTC)

        assertEquals("Sat 3 Oct, 4:15 PM", text)
    }

    @Test
    fun theDayFollowsThePhonesTimeZone() {
        // 23:30 UTC on the 6th is already the 7th in a UTC+2 zone
        val text = SensorDetailMapper.activityTimeText(
            Instant.parse("2026-10-06T23:30:00Z"), now, ZoneOffset.ofHours(2)
        )

        assertEquals("Today, 1:30 AM", text)
    }

    // ---- the short-lived cache ----

    private fun range() = mapOf("PH" to ThresholdResponseDto(
        id = 1, structureType = "VERTICAL_TOWER", sensorType = "PH", minValue = 5.5, maxValue = 6.5
    ))

    @Test
    fun freshness_coversTheWindowAndNothingOutsideIt() {
        assertTrue(StructureDataCache.isFresh(1_000, 1_000, 500))
        assertTrue(StructureDataCache.isFresh(1_000, 1_500, 500))
        assertFalse(StructureDataCache.isFresh(1_000, 1_501, 500))
    }

    @Test
    fun freshness_doesNotTrustATimeInTheFuture() {
        assertFalse(StructureDataCache.isFresh(5_000, 4_000, 500))
    }

    @Test
    fun savedRanges_areReturnedWhileFreshAndDroppedWhenOld() {
        StructureDataCache.clear()
        StructureDataCache.putThresholds(7, range(), nowMs = 10_000)

        assertNotNull(StructureDataCache.thresholds(7, nowMs = 10_000 + StructureDataCache.THRESHOLDS_FRESH_MS))
        assertNull(StructureDataCache.thresholds(7, nowMs = 10_001 + StructureDataCache.THRESHOLDS_FRESH_MS))
        assertNull(StructureDataCache.thresholds(8, nowMs = 10_000))
    }

    @Test
    fun clearingForgetsEverything() {
        StructureDataCache.putThresholds(7, range(), nowMs = 10_000)
        StructureDataCache.clear()

        assertNull(StructureDataCache.thresholds(7, nowMs = 10_000))
    }
}
