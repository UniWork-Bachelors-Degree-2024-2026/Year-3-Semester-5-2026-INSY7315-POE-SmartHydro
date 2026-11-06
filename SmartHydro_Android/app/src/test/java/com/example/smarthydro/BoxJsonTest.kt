package com.example.smarthydro

import com.example.smarthydro.models.BoxTelemetryDto
import com.google.gson.GsonBuilder
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

// Checks the app reads a box's reply the way the box (or the mock box) writes it
class BoxJsonTest {

    // The same settings BoxApiClient uses
    private val gson = GsonBuilder().create()

    // Mock data: replies as the box writes them
    private val fullReading = """{"ph":6.45,"ec":1.51,"temperature":24.6,"humidity":53.6,"light":0,"waterLevelRaw":2650,
        "waterLevelDetected":true,"mlPh":0,"mlEc":0,"mlTemp":0,"mlHumidity":0}"""

    private val readingWithZeroLight = """{"ph":6.4,"ec":1.5,"temperature":24.0,"humidity":55.0,"light":0}"""

    private val readingWithNullSensors = """{"ph":null,"ec":1.51,"temperature":24.4,"humidity":null,"light":0,"waterLevelRaw":2650,
        "waterLevelDetected":true,"mlPh":0,"mlEc":0,"mlTemp":0,"mlHumidity":0}"""

    private val readingWithSensorsLeftOut = """{"ec":1.5,"temperature":24.0,"light":100.0}"""

    private val readingWithNoWaterSensor = """{"ph":6.2,"waterLevelRaw":null,"waterLevelDetected":null}"""

    private val readingWithDryTank = """{"ph":6.2,"waterLevelRaw":0,"waterLevelDetected":false}"""

    private fun parse(json: String) = gson.fromJson(json, BoxTelemetryDto::class.java)

    @Test
    fun aFullReadingIsReadInFull() {
        val box = parse(fullReading)

        assertEquals(6.45, box.ph!!, 0.0)
        assertEquals(1.51, box.ec!!, 0.0)
        assertEquals(24.6, box.temperature!!, 0.0)
        assertEquals(53.6, box.humidity!!, 0.0)
        assertEquals(2650, box.waterLevelRaw)
        assertEquals(true, box.waterLevelDetected)
    }

    @Test
    fun aZeroIsAReadingNotAMissingValue() {
        val box = parse(readingWithZeroLight)

        assertEquals(0.0, box.light!!, 0.0)
    }

    @Test
    fun aSensorSentAsNullIsReadAsNoReading() {
        val box = parse(readingWithNullSensors)

        assertNull(box.ph)
        assertNull(box.humidity)
        assertEquals(1.51, box.ec!!, 0.0)
        assertEquals(24.4, box.temperature!!, 0.0)
    }

    @Test
    fun aSensorLeftOutEntirelyIsReadAsNoReading() {
        val box = parse(readingWithSensorsLeftOut)

        assertNull(box.ph)
        assertNull(box.humidity)
        assertNull(box.waterLevelDetected)
        assertEquals(1.5, box.ec!!, 0.0)
    }

    @Test
    fun aMissingWaterSensorIsNoReading() {
        val box = parse(readingWithNoWaterSensor)

        assertNull(box.waterLevelDetected)
        assertNull(box.waterLevelRaw)
    }

    @Test
    fun aDryTankIsAReading() {
        val box = parse(readingWithDryTank)

        assertEquals(false, box.waterLevelDetected)
        assertEquals(0, box.waterLevelRaw)
    }
}
