package com.example.smarthydro

import com.example.smarthydro.data.BoxTelemetryMapper
import com.example.smarthydro.data.SensorDetailMapper
import com.example.smarthydro.data.TelemetryMapper
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.models.TelemetrySnapshotDto
import com.example.smarthydro.models.ThresholdResponseDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NoReadingsTest {

    private fun snapshot(
        ph: Double? = 6.2,
        ec: Double? = 1.4,
        light: Double? = 300.0,
        water: Boolean? = true,
        temp: Double? = 24.0,
        humidity: Double? = 60.0
    ) = TelemetrySnapshotDto(
        phLevel = ph, ecMs = ec, lightLux = light, waterLevelRaw = 1, waterLevelDetected = water,
        temperatureC = temp, humidityPercent = humidity, recordedAt = "2026-10-07T08:00:00Z"
    )

    private fun box(
        ph: Double? = 6.2,
        ec: Double? = 1.4,
        temp: Double? = 24.0,
        humidity: Double? = 60.0,
        light: Double? = 300.0,
        water: Boolean? = true
    ) = BoxTelemetryDto(
        ph = ph, ec = ec, temperature = temp, humidity = humidity, light = light,
        waterLevelRaw = 1, waterLevelDetected = water, mlPh = 0, mlEc = 0, mlTemp = 0, mlHumidity = 0
    )

    // ---- cloud readings ----

    @Test
    fun noTelemetryAtAll_putsEverySensorInNoReadings() {
        val nodes = TelemetryMapper.hydroponicSensorNodes(null, null, emptyMap())

        assertEquals(6, nodes.size)
        assertTrue(nodes.none { it.hasReading })
    }

    @Test
    fun oneMissingSensor_onlyThatSensorHasNoReading() {
        val nodes = TelemetryMapper.hydroponicSensorNodes(snapshot(ph = null), null, emptyMap())

        assertFalse(nodes.first { it.id == "clean_water" }.hasReading)
        assertTrue(nodes.filter { it.id != "clean_water" }.all { it.hasReading })
    }

    @Test
    fun aRealZeroIsStillAReading() {
        val nodes = TelemetryMapper.hydroponicSensorNodes(snapshot(light = 0.0), null, emptyMap())

        assertTrue(nodes.first { it.id == "sunlight" }.hasReading)
    }

    @Test
    fun aMissingWaterLevelIsNoReading_andFalseIsStillAReading() {
        val missing = TelemetryMapper.hydroponicSensorNodes(snapshot(water = null), null, emptyMap())
        val dry = TelemetryMapper.hydroponicSensorNodes(snapshot(water = false), null, emptyMap())

        assertFalse(missing.first { it.id == "water" }.hasReading)
        assertTrue(dry.first { it.id == "water" }.hasReading)
        assertEquals("No Water Detected", dry.first { it.id == "water" }.valueDisplay)
    }

    // ---- readings straight from a box ----

    @Test
    fun boxWithEverySensor_hasNoSilentCards() {
        assertTrue(BoxTelemetryMapper.sensorNodes(box()).all { it.hasReading })
    }

    @Test
    fun boxMissingASensor_marksOnlyThatOneAndNeverShowsAZero() {
        val nodes = BoxTelemetryMapper.sensorNodes(box(ph = null, humidity = null))

        assertFalse(nodes.first { it.id == "clean_water" }.hasReading)
        assertEquals("-- pH", nodes.first { it.id == "clean_water" }.valueDisplay)
        assertFalse(nodes.first { it.id == "humidity" }.hasReading)
        assertTrue(nodes.first { it.id == "temperature" }.hasReading)
    }

    // ---- the offline detail page ----

    @Test
    fun boxDetail_placesAReadingOnTheSensorsOwnScale() {
        assertEquals(0.5f, SensorDetailMapper.buildFromBox("clean_water", box(ph = 7.0))!!.gaugeProgress!!, 0.0001f)
        assertEquals(0.5f, SensorDetailMapper.buildFromBox("temperature", box(temp = 25.0))!!.gaugeProgress!!, 0.0001f)
        assertEquals(0.5f, SensorDetailMapper.buildFromBox("humidity", box(humidity = 50.0))!!.gaugeProgress!!, 0.0001f)
    }

    @Test
    fun boxDetail_keepsTheArcInsideTheGauge() {
        assertEquals(1f, SensorDetailMapper.buildFromBox("clean_water", box(ph = 30.0))!!.gaugeProgress!!, 0f)
        assertEquals(0f, SensorDetailMapper.buildFromBox("temperature", box(temp = -5.0))!!.gaugeProgress!!, 0f)
    }

    @Test
    fun boxDetail_withNoReadingYetShowsDashesAndAnEmptyArc() {
        val data = SensorDetailMapper.buildFromBox("clean_water", null)!!

        assertEquals("--", data.currentValue)
        assertNull(data.gaugeProgress)
    }

    @Test
    fun boxDetail_aSensorWithoutAScaleLeavesTheArcEmptyButShowsTheNumber() {
        val ec = SensorDetailMapper.buildFromBox("nutrients", box(ec = 1.4))!!
        val light = SensorDetailMapper.buildFromBox("sunlight", box(light = 5300.0))!!

        assertNull(ec.gaugeProgress)
        assertEquals("1400", ec.currentValue)
        assertNull(light.gaugeProgress)
        assertEquals("5300", light.currentValue)
    }

    @Test
    fun boxDetail_hasNoRangeMarkingsBecauseTheBoxDoesNotKnowThem() {
        val data = SensorDetailMapper.buildFromBox("clean_water", box())!!

        assertNull(data.gaugeBandStart)
        assertNull(data.gaugeBandEnd)
        assertNull(data.statusBadge)
    }

    @Test
    fun boxDetail_forASensorWithoutADetailPageIsNull() {
        assertNull(SensorDetailMapper.buildFromBox("moisture", box()))
    }

    // ---- a detail page knows when its sensor has no reading ----

    @Test
    fun detailPage_withNoTelemetryHasNoReadingForEverySupportedSensor() {
        for (id in SensorDetailMapper.supportedSensorIds) {
            val data = SensorDetailMapper.build(id, null, null, emptyList(), emptyMap())!!
            assertFalse(id, data.hasReading)
        }
    }

    @Test
    fun detailPage_withAReadingHasOne_includingARealZero() {
        val data = SensorDetailMapper.build("sunlight", snapshot(light = 0.0), null, emptyList(), emptyMap())!!

        assertTrue(data.hasReading)
        assertEquals("0", data.currentValue)
    }

    @Test
    fun detailPage_onlyTheMissingSensorHasNoReading() {
        val missingPh = SensorDetailMapper.build("clean_water", snapshot(ph = null), null, emptyList(), emptyMap())!!
        val temperature = SensorDetailMapper.build("temperature", snapshot(ph = null), null, emptyList(), emptyMap())!!

        assertFalse(missingPh.hasReading)
        assertTrue(temperature.hasReading)
    }

    @Test
    fun boxDetail_aMissingSensorHasNoReadingButAPresentOneDoes() {
        assertFalse(SensorDetailMapper.buildFromBox("clean_water", box(ph = null))!!.hasReading)
        assertTrue(SensorDetailMapper.buildFromBox("clean_water", box(ph = 6.2))!!.hasReading)
    }

    // ---- a box that has not answered ----

    @Test
    fun boxCards_withNoReadingAtAll_putEverySensorInNoReadings() {
        val nodes = BoxTelemetryMapper.sensorNodes(null)

        assertEquals(6, nodes.size)
        assertTrue(nodes.none { it.hasReading })
        assertEquals("-- µS/cm", nodes.first { it.id == "nutrients" }.valueDisplay)
    }

    // ---- EC is shown in µS/cm everywhere ----

    @Test
    fun boxCards_showEcInMicrosiemens() {
        val nutrients = BoxTelemetryMapper.sensorNodes(box(ec = 1.4)).first { it.id == "nutrients" }

        assertEquals("1400 µS/cm", nutrients.valueDisplay)
    }

    @Test
    fun homeCards_showEcAndItsRangeInMicrosiemens() {
        val range = ThresholdResponseDto(
            id = 2, structureType = "VERTICAL_TOWER", sensorType = "EC", minValue = 1.3, maxValue = 1.5
        )

        val nodes = TelemetryMapper.hydroponicSensorNodes(snapshot(ec = 1.4), null, mapOf("EC" to range))
        val nutrients = nodes.first { it.id == "nutrients" }

        assertEquals("1400 µS/cm", nutrients.valueDisplay)
        assertEquals("Optimal Range: 1300-1500µS/cm", nutrients.infoText)
    }
}
