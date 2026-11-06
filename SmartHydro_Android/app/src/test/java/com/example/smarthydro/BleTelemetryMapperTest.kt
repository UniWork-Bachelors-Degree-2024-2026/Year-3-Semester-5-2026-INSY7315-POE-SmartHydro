package com.example.smarthydro

import com.example.smarthydro.transport.BleTelemetryMapper
import com.example.smarthydro.transport.BoxMode
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks how a box's compact Bluetooth telemetry frame becomes the reading the screens show
class BleTelemetryMapperTest {

    // Mock data: a tower frame as the firmware builds it, light and water pump on, everything else off
    private val towerFrame = """{"m":"A4:CF:12:9B:3E:01","p":6.25,"e":1.40,"t":24.5,"h":61.0,"w":1,
        "s":1,"fc":0,"fe":0,"gl":1,"lux":320,"l":320,"pu":0,"pd":0,"nu":0,"nd":0,"r":3,
        "md":0,"pm":0,"mr":0,"sf":0}"""

    private fun parse(json: String) = BleTelemetryMapper.parse(json)

    // ---- the reading ----

    @Test
    fun readingFieldsAreMapped() {
        val reading = parse(towerFrame)!!.reading

        assertEquals(6.25, reading.ph!!, 0.0001)
        assertEquals(1.40, reading.ec!!, 0.0001)
        assertEquals(24.5, reading.temperature!!, 0.0001)
        assertEquals(61.0, reading.humidity!!, 0.0001)
        assertEquals(320.0, reading.light!!, 0.0001)
        assertEquals(true, reading.waterLevelDetected)
    }

    @Test
    fun relaysFollowTheBoxSwitches() {
        val relays = parse(towerFrame)!!.reading.relays!!

        assertEquals(true, relays.growLight)
        assertEquals(true, relays.mainWaterPump)
        assertEquals(false, relays.circulationFan)
        assertEquals(false, relays.extractorFan)
        assertEquals(false, relays.phUpPump)
    }

    @Test
    fun runningDosePumpShowsAsOn() {
        val relays = parse(towerFrame.replace("\"pd\":0", "\"pd\":1"))!!.reading.relays!!

        assertEquals(true, relays.phDownPump)
        assertEquals(false, relays.phUpPump)
    }

    @Test
    fun frameWithoutRelaysLeavesThemOut() {
        val frame = """{"m":"A4:CF:12:9B:3E:01","p":6.25,"e":1.40,"t":24.5,"h":61.0,"w":1,"sf":0}"""

        assertNull(parse(frame)!!.reading.relays)
    }

    @Test
    fun bluetoothFramesCarryNoRawWaterLevelOrPredictions() {
        val reading = parse(towerFrame)!!.reading

        assertNull(reading.waterLevelRaw)
        assertEquals(0, reading.mlPh)
        assertEquals(0, reading.mlEc)
    }

    @Test
    fun lightFallsBackToTheShortKey() {
        val frame = """{"m":"A4:CF:12:9B:3E:01","l":150,"sf":0}"""

        assertEquals(150.0, parse(frame)!!.reading.light!!, 0.0001)
    }

    // ---- failed sensors ----

    @Test
    fun failedSensorsReadAsNoReading() {
        // Bits 1 and 8: pH and humidity have failed. The firmware still sends a number for them
        val frame = towerFrame.replace("\"p\":6.25", "\"p\":-1.00").replace("\"sf\":0", "\"sf\":9")
        val reading = parse(frame)!!.reading

        assertNull(reading.ph)
        assertNull(reading.humidity)
        assertEquals(1.40, reading.ec!!, 0.0001)
        assertEquals(24.5, reading.temperature!!, 0.0001)
    }

    @Test
    fun failedLightSensorReadsAsNoReading() {
        assertNull(parse(towerFrame.replace("\"sf\":0", "\"sf\":16"))!!.reading.light)
    }

    // ---- the fodder box ----

    @Test
    fun spraySystemShowsOnTheWaterSwitch() {
        // The fodder box reports its spray system under the same "s" key the tower uses for its pump
        val frame = """{"m":"B8:D6:1A:44:70:02","p":6.0,"e":1.1,"t":22.0,"h":70.0,"w":1,"s":1,"gl":0,"sf":0}"""

        assertEquals(true, parse(frame)!!.reading.relays!!.mainWaterPump)
    }

    // ---- which box it is ----

    @Test
    fun boxMacIsWrittenInTheAppsFormat() {
        assertEquals("A4:CF:12:9B:3E:01", parse(towerFrame)!!.deviceMac)
        assertEquals("A4:CF:12:9B:3E:01", parse(towerFrame.replace("A4:CF:12:9B:3E:01", "a4cf129b3e01"))!!.deviceMac)
    }

    @Test
    fun frameWithoutAValidMacIsRejected() {
        assertNull(parse("""{"p":6.25,"e":1.40,"sf":0}"""))
        assertNull(parse("""{"m":"not a mac","p":6.25,"sf":0}"""))
        assertNull(parse("""{"m":"","p":6.25,"sf":0}"""))
    }

    // ---- connectivity details ----

    @Test
    fun modeDetailsAreReported() {
        val frame = towerFrame.replace("\"md\":0,\"pm\":0,\"mr\":0", "\"md\":1,\"pm\":0,\"mr\":1")
        val telemetry = parse(frame)!!

        assertEquals(BoxMode.ONLINE, telemetry.mode)
        assertEquals(BoxMode.LORA_BLE, telemetry.preferredMode)
        assertTrue(telemetry.autoFallback)
    }

    @Test
    fun choosingAModeIsNotAFallback() {
        val telemetry = parse(towerFrame)!!

        assertEquals(BoxMode.LORA_BLE, telemetry.mode)
        assertFalse(telemetry.autoFallback)
    }

    @Test
    fun aModeTheAppDoesNotKnowIsLeftOut() {
        assertNull(parse(towerFrame.replace("\"md\":0", "\"md\":9"))!!.mode)
    }

    @Test
    fun alertIsPassedOn() {
        assertEquals("PH_RUNAWAY_ABORT", parse(towerFrame.replace("\"sf\":0", "\"sf\":0,\"al\":\"PH_RUNAWAY_ABORT\""))!!.alert)
        assertNull(parse(towerFrame)!!.alert)
    }

    // ---- bad input ----

    @Test
    fun frameCutShortIsRejected() {
        // What a phone gets when the MTU was never raised: the JSON stops part way through
        assertNull(parse(towerFrame.take(40)))
        assertNull(parse("""{"m":"A4:CF:12:9B:3E:01","p":6.25,"e":"""))
    }

    @Test
    fun notAnObjectIsRejected() {
        assertNull(parse(""))
        assertNull(parse("[1,2,3]"))
        assertNull(parse("hello"))
    }

    @Test
    fun numberTheFirmwareCouldNotWriteCostsOnlyThatField() {
        // Arduino writes a reading it could not take as the bare word nan, which is not valid JSON
        val telemetry = parse(towerFrame.replace("\"p\":6.25", "\"p\":nan"))

        assertNotNull(telemetry)
        assertNull(telemetry!!.reading.ph)
        assertEquals(1.40, telemetry.reading.ec!!, 0.0001)
    }

    @Test
    fun oneBadNumberCostsOnlyThatField() {
        val frame = towerFrame.replace("\"p\":6.25", "\"p\":\"abc\"")
        val telemetry = parse(frame)

        assertNotNull(telemetry)
        assertNull(telemetry!!.reading.ph)
        assertEquals(1.40, telemetry.reading.ec!!, 0.0001)
    }
}
