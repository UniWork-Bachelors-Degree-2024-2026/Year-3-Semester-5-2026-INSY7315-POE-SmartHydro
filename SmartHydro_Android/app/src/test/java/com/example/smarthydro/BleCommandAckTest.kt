package com.example.smarthydro

import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.transport.AckPhase
import com.example.smarthydro.transport.BleCommandAck
import com.example.smarthydro.transport.BleCommandAckParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks how the box's answer to a command is read and worded for the person
class BleCommandAckTest {

    private fun parse(json: String) = BleCommandAckParser.parse(json)

    // ---- reading the answer ----

    @Test
    fun relayAnswerIsAccepted() {
        val ack = parse("""{"cid":7,"ok":true,"reason":"executed","phase":"accepted"}""")!!

        assertEquals(7, ack.commandId)
        assertTrue(ack.ok)
        assertEquals("executed", ack.reason)
        assertEquals(AckPhase.ACCEPTED, ack.phase)
    }

    @Test
    fun doseAnswersCompleteInTwoSteps() {
        assertEquals(AckPhase.COMPLETED, parse("""{"cid":8,"ok":true,"reason":"timer_finished","phase":"completed"}""")!!.phase)
        assertEquals(AckPhase.CANCELLED, parse("""{"cid":8,"ok":true,"reason":"cancelled","phase":"cancelled"}""")!!.phase)
    }

    @Test
    fun refusalCarriesTheReason() {
        val ack = parse("""{"cid":9,"ok":false,"reason":"mixing_lockout","phase":"accepted"}""")!!

        assertFalse(ack.ok)
        assertEquals("mixing_lockout", ack.reason)
    }

    @Test
    fun switchWrittenAsOneOrZeroIsRead() {
        assertTrue(parse("""{"cid":1,"ok":1,"reason":"executed","phase":"accepted"}""")!!.ok)
        assertFalse(parse("""{"cid":1,"ok":0,"reason":"invalid_command","phase":"accepted"}""")!!.ok)
    }

    @Test
    fun idOfZeroIsKeptForAnAnswerThatCouldNotReadTheId() {
        val ack = parse("""{"cid":0,"ok":false,"reason":"invalid_command","phase":"accepted"}""")

        assertNotNull(ack)
        assertEquals(0, ack!!.commandId)
    }

    @Test
    fun missingReasonReadsAsEmpty() {
        assertEquals("", parse("""{"cid":1,"ok":true,"phase":"accepted"}""")!!.reason)
    }

    // ---- bad input ----

    @Test
    fun incompleteAnswersAreRejected() {
        assertNull(parse("""{"ok":true,"reason":"executed","phase":"accepted"}"""))
        assertNull(parse("""{"cid":1,"reason":"executed","phase":"accepted"}"""))
        assertNull(parse("""{"cid":1,"ok":true,"reason":"executed"}"""))
    }

    @Test
    fun aPhaseTheAppDoesNotKnowIsRejected() {
        assertNull(parse("""{"cid":1,"ok":true,"reason":"executed","phase":"paused"}"""))
    }

    @Test
    fun cutOffOrNonJsonAnswersAreRejected() {
        assertNull(parse("""{"cid":1,"ok":true,"rea"""))
        assertNull(parse(""))
        assertNull(parse("[1]"))
        assertNull(parse("hello"))
    }

    // ---- what the screens are told ----

    @Test
    fun acceptedCommandIsDone() {
        assertTrue(parse("""{"cid":1,"ok":true,"reason":"executed","phase":"accepted"}""")!!.toResult() is BoxControlResult.Done)
    }

    @Test
    fun doseTheBoxStoppedIsRefused() {
        val result = parse("""{"cid":1,"ok":true,"reason":"cancelled","phase":"cancelled"}""")!!.toResult()

        assertEquals(BoxControlResult.Refused(BleCommandAck.CANCELLED_MESSAGE), result)
    }

    @Test
    fun finishedDoseIsDone() {
        assertTrue(parse("""{"cid":1,"ok":true,"reason":"timer_finished","phase":"completed"}""")!!.toResult() is BoxControlResult.Done)
    }

    @Test
    fun refusedCommandIsRefusedWithWords() {
        val result = parse("""{"cid":1,"ok":false,"reason":"mixing_lockout","phase":"accepted"}""")!!.toResult()

        assertTrue(result is BoxControlResult.Refused)
        assertEquals(BleCommandAck.refusalMessage("mixing_lockout"), (result as BoxControlResult.Refused).message)
    }

    @Test
    fun everyKnownReasonHasItsOwnWords() {
        val reasons = listOf(
            "sensor_fault", "pump_busy", "mixing_lockout", "ph_hourly_cap", "dose_count_limit",
            "pulse_limit", "outer_safety_limit", "safety_storage", "invalid_command"
        )
        val general = BleCommandAck.refusalMessage("something_new")

        reasons.forEach {
            assertNotEquals("$it has no message", general, BleCommandAck.refusalMessage(it))
        }
    }

    @Test
    fun reasonsThatMeanTheSameThingShareWords() {
        assertEquals(BleCommandAck.refusalMessage("pulse_limit"), BleCommandAck.refusalMessage("invalid_dose"))
        assertEquals(BleCommandAck.refusalMessage("safety_storage"), BleCommandAck.refusalMessage("cutoff_timer"))
    }

    @Test
    fun unknownReasonGetsTheGeneralMessage() {
        assertEquals("The box didn't accept that. Please try again.", BleCommandAck.refusalMessage("something_new"))
        assertEquals("The box didn't accept that. Please try again.", BleCommandAck.refusalMessage(""))
    }
}
