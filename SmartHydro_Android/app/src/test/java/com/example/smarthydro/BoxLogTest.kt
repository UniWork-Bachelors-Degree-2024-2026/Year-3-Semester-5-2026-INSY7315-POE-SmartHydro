package com.example.smarthydro

import com.example.smarthydro.models.StructureType
import com.example.smarthydro.transport.BleCommandAck
import com.example.smarthydro.transport.BleCommandRunner
import com.example.smarthydro.transport.AckPhase
import com.example.smarthydro.transport.BoxAdvertisement
import com.example.smarthydro.transport.BoxLog
import com.example.smarthydro.transport.BoxLogSink
import com.example.smarthydro.transport.CommandIdSource
import com.example.smarthydro.transport.LogLevel
import com.example.smarthydro.transport.PairingController
import com.example.smarthydro.transport.PairingEffects
import com.example.smarthydro.transport.PairingEvent
import com.example.smarthydro.transport.PairingFlow
import com.example.smarthydro.transport.ScanUpdate
import com.google.gson.JsonParser
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.test.runTest
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

// Checks the log of what the Bluetooth side does, which is what a test on a real box is read back from
@OptIn(ExperimentalCoroutinesApi::class)
class BoxLogTest {

    private class Line(val level: LogLevel, val tag: String, val message: String)

    private val lines = mutableListOf<Line>()
    private val original = BoxLog.sink

    @Before
    fun catchTheLog() {
        BoxLog.sink = BoxLogSink { level, tag, message -> lines += Line(level, tag, message) }
    }

    @After
    fun putTheLogBack() {
        BoxLog.sink = original
    }

    private fun said(text: String) = lines.any { it.message.contains(text) }

    // ---- the log itself ----

    @Test
    fun everyLineHasTheTagTheLevelAndWhereItCameFrom() {
        BoxLog.d("conn", "one")
        BoxLog.i("scan", "two")
        BoxLog.w("cmd", "three")
        BoxLog.e("link", "four")

        assertEquals(listOf(LogLevel.DEBUG, LogLevel.INFO, LogLevel.WARN, LogLevel.ERROR), lines.map { it.level })
        assertTrue(lines.all { it.tag == "SmartHydroBle" })
        assertEquals(listOf("[conn] one", "[scan] two", "[cmd] three", "[link] four"), lines.map { it.message })
    }

    @Test
    fun aShortFrameIsKeptWhole() {
        assertEquals("""{"m":"A4"}""", BoxLog.clip("""{"m":"A4"}"""))
    }

    @Test
    fun aLongFrameIsCutAndSaysHowLongItWas() {
        val long = "x".repeat(500)
        val clipped = BoxLog.clip(long)

        assertTrue(clipped.startsWith("x".repeat(160)))
        assertTrue(clipped.endsWith("(500 characters)"))
        assertFalse(clipped.contains("x".repeat(161)))
    }

    @Test
    fun theUsualLogDoesNotBreakWhereThereIsNoPhone() {
        BoxLog.sink = original

        BoxLog.i("conn", "writing with no Android must not throw")
        BoxLog.e("conn", "not even an error")
    }

    // ---- what the app writes ----

    @Test
    fun aCommandAndItsAnswerAreWritten() = runTest {
        val answers = MutableSharedFlow<BleCommandAck>(extraBufferCapacity = 16)
        val runner = BleCommandRunner(
            deviceMac = "A4:CF:12:9B:3E:01", ids = CommandIdSource(first = 7), answers = answers, isReady = { true },
            write = { json ->
                answers.tryEmit(BleCommandAck(JsonParser.parseString(json).asJsonObject["cid"].asInt, true, "executed", AckPhase.ACCEPTED))
                true
            }
        )

        runner.setRelay("growLight", true)

        assertTrue(said("send cid=7"))
        assertTrue(said("answer to cid=7: ok=true reason=executed phase=ACCEPTED"))
    }

    @Test
    fun aMissingAnswerAndTheRetryAreWritten() = runTest {
        val runner = BleCommandRunner(
            deviceMac = "A4:CF:12:9B:3E:01", ids = CommandIdSource(first = 8), answers = MutableSharedFlow(extraBufferCapacity = 16),
            isReady = { true }, write = { true }
        )

        runner.setRelay("growLight", true)

        assertTrue(said("no answer to cid=8 after attempt 1 of 2"))
        assertTrue(said("gave up on cid=8"))
    }

    @Test
    fun aCommandSentWithNoConnectionIsWritten() = runTest {
        val runner = BleCommandRunner(
            deviceMac = "A4:CF:12:9B:3E:01", ids = CommandIdSource(), answers = MutableSharedFlow(),
            isReady = { false }, write = { true }
        )

        runner.setRelay("growLight", true)

        assertTrue(said("not sent, the Bluetooth connection is not ready"))
    }

    @Test
    fun theSearchDoesNotFloodTheLog() {
        val own = BoxAdvertisement.parse("SmartHydro-Tower-9B3E01", "own", -50, 0L)!!
        val controller = PairingController(PairingFlow(null, StructureType.HYDROPONIC), object : PairingEffects {
            override fun requestPermissions(permissions: List<String>) {}
            override fun startScan() {}
            override fun stopScan() {}
            override fun openConnection(address: String) {}
            override fun closeConnection() {}
            override fun saveAddress(address: String) {}
        })

        controller.send(PairingEvent.Start(emptyList()))
        val afterStart = lines.size
        repeat(50) { controller.send(PairingEvent.Scan(ScanUpdate.Boxes(listOf(own)))) }

        assertEquals(afterStart, lines.size)
        assertTrue(said("Idle -> Searching(0 found), then StartScan"))
    }

    @Test
    fun aChangeOfStepIsWrittenInAFewWords() {
        val own = BoxAdvertisement.parse("SmartHydro-Tower-9B3E01", "own", -50, 0L)!!
        val controller = PairingController(PairingFlow("A4:CF:12:9B:3E:01", StructureType.HYDROPONIC), object : PairingEffects {
            override fun requestPermissions(permissions: List<String>) {}
            override fun startScan() {}
            override fun stopScan() {}
            override fun openConnection(address: String) {}
            override fun closeConnection() {}
            override fun saveAddress(address: String) {}
        })

        controller.send(PairingEvent.Start(emptyList()))
        controller.send(PairingEvent.Scan(ScanUpdate.Boxes(listOf(own))))

        assertTrue(said("-> Confirming(SmartHydro-Tower-9B3E01), then StopScan"))
        assertFalse("the list of boxes is not written out", lines.any { it.message.contains("NearbyBox(") })
    }
}
