package com.example.smarthydro

import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.transport.AckPhase
import com.example.smarthydro.transport.BleCommandAck
import com.example.smarthydro.transport.BleCommandRunner
import com.example.smarthydro.transport.BoxMode
import com.example.smarthydro.transport.CommandIdSource
import com.google.gson.JsonParser
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks how a command is sent to a box over Bluetooth and its answer waited for, with no phone and a fake box
@OptIn(ExperimentalCoroutinesApi::class)
class BleCommandRunnerTest {

    private val mac = "A4:CF:12:9B:3E:01"

    // The fake box: every write is kept, and onWrite decides what the box does about it
    private class FakeBox(
        val answers: MutableSharedFlow<BleCommandAck> = MutableSharedFlow(extraBufferCapacity = 16),
        var ready: Boolean = true,
        var deliver: Boolean = true,
        var onWrite: (Int, Int) -> Unit = { _, _ -> }
    ) {
        val writes = mutableListOf<String>()

        suspend fun write(json: String): Boolean {
            writes += json
            if (!deliver) return false
            onWrite(JsonParser.parseString(json).asJsonObject["cid"].asInt, writes.size)
            return true
        }
    }

    private fun runner(box: FakeBox, first: Int = 100) = BleCommandRunner(
        deviceMac = mac,
        ids = CommandIdSource(first),
        answers = box.answers,
        isReady = { box.ready },
        write = box::write
    )

    private fun FakeBox.reply(ok: Boolean = true, reason: String = "executed", phase: AckPhase = AckPhase.ACCEPTED): (Int, Int) -> Unit =
        { cid, _ -> answers.tryEmit(BleCommandAck(cid, ok, reason, phase)) }

    // ---- relays and mode ----

    @Test
    fun relayAcceptedIsDone() = runTest {
        val box = FakeBox()
        box.onWrite = box.reply()

        val result = runner(box).setRelay("growLight", true)

        assertTrue(result is BoxControlResult.Done)
        val sent = JsonParser.parseString(box.writes.single()).asJsonObject
        assertEquals("growLight", sent["actuator"].asString)
        assertEquals(true, sent["state"].asBoolean)
        assertEquals(mac, sent["m"].asString)
    }

    @Test
    fun relayRefusedGivesTheBoxsReason() = runTest {
        val box = FakeBox()
        box.onWrite = box.reply(ok = false, reason = "invalid_command")

        val result = runner(box).setRelay("growLight", true)

        assertEquals(BoxControlResult.Refused(BleCommandAck.refusalMessage("invalid_command")), result)
    }

    @Test
    fun modeChangeIsSentAndAnswered() = runTest {
        val box = FakeBox()
        box.onWrite = box.reply()

        val result = runner(box).setMode(BoxMode.ONLINE)

        assertTrue(result is BoxControlResult.Done)
        assertEquals(1, JsonParser.parseString(box.writes.single()).asJsonObject["mode"].asInt)
    }

    // ---- doses ----

    @Test
    fun doseIsDoneOnceThePumpStartsAndSaysHowLongItRuns() = runTest {
        val box = FakeBox()
        box.onWrite = box.reply()

        val result = runner(box).dose("NUTRIENT_UP", 20.0)

        assertEquals(BoxControlResult.Done(durationMs = 16_000), result)
        assertEquals(20.0, JsonParser.parseString(box.writes.single()).asJsonObject["nu"].asDouble, 0.0001)
    }

    @Test
    fun doseRefusedByTheBoxGivesItsReason() = runTest {
        val box = FakeBox()
        box.onWrite = box.reply(ok = false, reason = "mixing_lockout")

        val result = runner(box).dose("PH_UP", 2.0)

        assertEquals(BoxControlResult.Refused(BleCommandAck.refusalMessage("mixing_lockout")), result)
    }

    @Test
    fun doseTheBoxWouldNotRunIsRefusedWithoutSendingAnything() = runTest {
        val box = FakeBox()

        val result = runner(box).dose("PH_UP", 10.0)

        assertEquals(BoxControlResult.Refused(BleCommandAck.refusalMessage("pulse_limit")), result)
        assertTrue(box.writes.isEmpty())
    }

    @Test
    fun doseStoppedBeforeItFinishedIsRefused() = runTest {
        val box = FakeBox()
        box.onWrite = box.reply(reason = "cancelled", phase = AckPhase.CANCELLED)

        val result = runner(box).dose("NUTRIENT_UP", 20.0)

        assertEquals(BoxControlResult.Refused(BleCommandAck.CANCELLED_MESSAGE), result)
    }

    // ---- when the box cannot be reached ----

    @Test
    fun notConnectedMeansOfflineAndNothingIsSent() = runTest {
        val box = FakeBox(ready = false)

        assertEquals(BoxControlResult.Offline, runner(box).setRelay("growLight", true))
        assertTrue(box.writes.isEmpty())
    }

    @Test
    fun writeThatCouldNotBeDeliveredMeansOffline() = runTest {
        val box = FakeBox(deliver = false)

        assertEquals(BoxControlResult.Offline, runner(box).setRelay("growLight", true))
        assertEquals(1, box.writes.size)
    }

    // ---- no answer ----

    @Test
    fun missingAnswerIsRetriedOnceWithTheSameId() = runTest {
        val box = FakeBox()

        val result = runner(box).setRelay("growLight", true)

        assertEquals(BoxControlResult.Refused(BleCommandRunner.UNCONFIRMED_MESSAGE), result)
        assertEquals(2, box.writes.size)
        assertEquals(box.writes[0], box.writes[1])
    }

    @Test
    fun answerToTheRetryIsUsed() = runTest {
        val box = FakeBox()
        val answer = box.reply()
        box.onWrite = { cid, count -> if (count == 2) answer(cid, count) }

        val result = runner(box).setRelay("growLight", true)

        assertTrue(result is BoxControlResult.Done)
        assertEquals(2, box.writes.size)
    }

    @Test
    fun retryThatCannotBeDeliveredIsUnconfirmedNotOffline() = runTest {
        val box = FakeBox()
        box.onWrite = { _, count -> if (count == 1) box.deliver = false }

        val result = runner(box).setRelay("growLight", true)

        assertEquals(BoxControlResult.Refused(BleCommandRunner.UNCONFIRMED_MESSAGE), result)
    }

    @Test
    fun answersToOtherCommandsAreIgnored() = runTest {
        val box = FakeBox()
        box.onWrite = { cid, _ -> box.answers.tryEmit(BleCommandAck(cid + 1, false, "pump_busy", AckPhase.ACCEPTED)) }

        val result = runner(box).setRelay("growLight", true)

        assertEquals(BoxControlResult.Refused(BleCommandRunner.UNCONFIRMED_MESSAGE), result)
    }

    // ---- ids ----

    @Test
    fun eachCommandGetsItsOwnId() = runTest {
        val box = FakeBox()
        box.onWrite = box.reply()
        val run = runner(box, first = 100)

        run.setRelay("growLight", true)
        run.setRelay("growLight", false)

        val ids = box.writes.map { JsonParser.parseString(it).asJsonObject["cid"].asInt }
        assertEquals(listOf(100, 101), ids)
        assertNotEquals(ids[0], ids[1])
    }

    @Test
    fun refusedDoseUsesNoId() = runTest {
        val box = FakeBox()
        box.onWrite = box.reply()
        val run = runner(box, first = 100)

        run.dose("PH_UP", 10.0)
        run.setRelay("growLight", true)

        assertEquals(100, JsonParser.parseString(box.writes.single()).asJsonObject["cid"].asInt)
    }
}
