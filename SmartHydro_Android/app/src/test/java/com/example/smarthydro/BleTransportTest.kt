package com.example.smarthydro

import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.transport.AckPhase
import com.example.smarthydro.transport.BleCommandAck
import com.example.smarthydro.transport.BleCommandRunner
import com.example.smarthydro.transport.BleTelemetry
import com.example.smarthydro.transport.BleTelemetryMapper
import com.example.smarthydro.transport.BleTransport
import com.example.smarthydro.transport.BoxLink
import com.example.smarthydro.transport.BoxMode
import com.example.smarthydro.transport.BoxModeInfo
import com.example.smarthydro.transport.BoxTransport
import com.example.smarthydro.transport.CommandIdSource
import com.example.smarthydro.transport.HotspotTransport
import com.google.gson.JsonParser
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.toList
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks the Bluetooth way of reaching a box: whose readings are passed on, when they go quiet and that commands get through
@OptIn(ExperimentalCoroutinesApi::class)
class BleTransportTest {

    private val mac = "A4:CF:12:9B:3E:01"

    private fun frame(boxMac: String, ph: Double = 6.25): BleTelemetry = BleTelemetryMapper.parse(
        """{"m":"$boxMac","p":$ph,"e":1.40,"t":24.5,"h":61.0,"w":1,"s":1,"gl":1,"sf":0}"""
    )!!

    // The fake side of the connection, and a transport wired to it
    private class Setup(deviceMac: String, quietAfterMs: Long = 10_000L) {
        val incoming = MutableSharedFlow<BleTelemetry>(extraBufferCapacity = 16)
        val answers = MutableSharedFlow<BleCommandAck>(extraBufferCapacity = 16)
        val received = mutableListOf<BoxTelemetryDto?>()
        val recorded = mutableListOf<Pair<String, BoxTelemetryDto>>()
        val writes = mutableListOf<String>()

        val transport = BleTransport(
            deviceMac = deviceMac,
            runner = BleCommandRunner(
                deviceMac = deviceMac,
                ids = CommandIdSource(first = 50),
                answers = answers,
                isReady = { true },
                write = { json ->
                    writes += json
                    val cid = JsonParser.parseString(json).asJsonObject["cid"].asInt
                    answers.tryEmit(BleCommandAck(cid, true, "executed", AckPhase.ACCEPTED))
                    true
                }
            ),
            incoming = incoming,
            record = { boxMac, reading -> recorded += boxMac to reading },
            quietAfterMs = quietAfterMs
        )
    }

    // Starts listening for readings and lets the listener get going
    private fun TestScope.listen(setup: Setup) {
        backgroundScope.launch { setup.transport.readings { setup.received += it } }
        runCurrent()
    }

    @Test
    fun theLinkIsBluetooth() = runTest {
        assertEquals(BoxLink.BLUETOOTH, Setup(mac).transport.link)
    }

    // ---- readings ----

    @Test
    fun readingFromThisBoxIsPassedOnAndKept() = runTest {
        val setup = Setup(mac)
        listen(setup)

        setup.incoming.emit(frame(mac, ph = 6.25))
        runCurrent()

        assertEquals(1, setup.received.size)
        assertEquals(6.25, setup.received[0]!!.ph!!, 0.0001)
        assertEquals(1, setup.recorded.size)
        assertEquals(mac, setup.recorded[0].first)
    }

    @Test
    fun readingFromAnotherBoxIsLeftOut() = runTest {
        val setup = Setup(mac)
        listen(setup)

        setup.incoming.emit(frame("B8:D6:1A:44:A7:02"))
        runCurrent()

        assertTrue(setup.received.isEmpty())
        assertTrue(setup.recorded.isEmpty())
    }

    @Test
    fun macInAnotherStyleStillMatches() = runTest {
        val setup = Setup("a4cf129b3e01")
        listen(setup)

        setup.incoming.emit(frame(mac))
        runCurrent()

        assertEquals(1, setup.received.size)
    }

    // ---- going quiet ----

    @Test
    fun noReadingIsReportedOnceTheBoxGoesQuiet() = runTest {
        val setup = Setup(mac, quietAfterMs = 10_000L)
        listen(setup)
        setup.incoming.emit(frame(mac))
        runCurrent()

        advanceTimeBy(9_999L)
        runCurrent()
        assertEquals(1, setup.received.size)

        advanceTimeBy(2L)
        runCurrent()
        assertEquals(2, setup.received.size)
        assertNull(setup.received.last())
    }

    @Test
    fun aNewReadingKeepsTheBoxFromGoingQuiet() = runTest {
        val setup = Setup(mac, quietAfterMs = 10_000L)
        listen(setup)

        repeat(5) {
            setup.incoming.emit(frame(mac))
            runCurrent()
            advanceTimeBy(6_000L)
            runCurrent()
        }

        assertEquals(5, setup.received.size)
        assertTrue(setup.received.all { it != null })
    }

    @Test
    fun boxNeverHeardFromGoesQuietToo() = runTest {
        val setup = Setup(mac, quietAfterMs = 10_000L)
        listen(setup)

        advanceTimeBy(9_999L)
        runCurrent()
        assertTrue(setup.received.isEmpty())

        advanceTimeBy(2L)
        runCurrent()
        assertEquals(listOf<BoxTelemetryDto?>(null), setup.received)
    }

    @Test
    fun readingsComeBackAfterAQuietSpell() = runTest {
        val setup = Setup(mac, quietAfterMs = 10_000L)
        listen(setup)
        setup.incoming.emit(frame(mac, ph = 6.0))
        runCurrent()
        advanceTimeBy(10_001L)
        runCurrent()

        setup.incoming.emit(frame(mac, ph = 7.0))
        runCurrent()

        assertNull(setup.received[1])
        assertEquals(7.0, setup.received[2]!!.ph!!, 0.0001)
        assertEquals(2, setup.recorded.size)
    }

    @Test
    fun noReadingIsNeverKept() = runTest {
        val setup = Setup(mac, quietAfterMs = 10_000L)
        listen(setup)

        advanceTimeBy(10_001L)
        runCurrent()

        assertEquals(1, setup.received.size)
        assertNull(setup.received[0])
        assertTrue(setup.recorded.isEmpty())
    }

    // ---- commands ----

    @Test
    fun relayCommandGoesThroughTheRunner() = runTest {
        val setup = Setup(mac)

        val result = setup.transport.setRelay("growLight", true)

        assertTrue(result is BoxControlResult.Done)
        assertEquals("growLight", JsonParser.parseString(setup.writes.single()).asJsonObject["actuator"].asString)
    }

    @Test
    fun modeChangeGoesThroughTheRunner() = runTest {
        val setup = Setup(mac)

        val result = setup.transport.setMode(BoxMode.ONLINE)

        assertTrue(result is BoxControlResult.Done)
        assertEquals(1, JsonParser.parseString(setup.writes.single()).asJsonObject["mode"].asInt)
    }

    @Test
    fun theHotspotCannotChangeTheMode() = runTest {
        val hotspot = HotspotTransport("192.168.4.1", mac)

        assertEquals(
            BoxControlResult.Refused(BoxTransport.MODE_NEEDS_BLUETOOTH_MESSAGE),
            hotspot.setMode(BoxMode.LORA_BLE)
        )
    }

    @Test
    fun theHotspotReportsNoModeDetails() = runTest {
        assertEquals(emptyList<BoxModeInfo>(), HotspotTransport("192.168.4.1", mac).modes().toList())
    }

    // ---- the mode the box reports ----

    private fun modeFrame(boxMac: String, md: Int, pm: Int, mr: Int): BleTelemetry = BleTelemetryMapper.parse(
        """{"m":"$boxMac","p":6.25,"e":1.40,"t":24.5,"h":61.0,"w":1,"s":1,"gl":1,"md":$md,"pm":$pm,"mr":$mr,"sf":0}"""
    )!!

    @Test
    fun theBoxsModeDetailsArePassedOn() = runTest {
        val setup = Setup(mac)
        val heard = mutableListOf<BoxModeInfo>()
        backgroundScope.launch { setup.transport.modes().collect { heard += it } }
        runCurrent()

        setup.incoming.emit(modeFrame(mac, md = 1, pm = 0, mr = 1))
        runCurrent()

        assertEquals(listOf(BoxModeInfo(BoxMode.ONLINE, BoxMode.LORA_BLE, autoFallback = true)), heard)
    }

    @Test
    fun modeDetailsFromAnotherBoxAreLeftOut() = runTest {
        val setup = Setup(mac)
        val heard = mutableListOf<BoxModeInfo>()
        backgroundScope.launch { setup.transport.modes().collect { heard += it } }
        runCurrent()

        setup.incoming.emit(modeFrame("B8:D6:1A:44:A7:02", md = 2, pm = 2, mr = 0))
        runCurrent()

        assertTrue(heard.isEmpty())
    }

    @Test
    fun doseCommandGoesThroughTheRunner() = runTest {
        val setup = Setup(mac)

        val result = setup.transport.dose("NUTRIENT_UP", 20.0)

        assertEquals(BoxControlResult.Done(durationMs = 16_000), result)
    }
}
