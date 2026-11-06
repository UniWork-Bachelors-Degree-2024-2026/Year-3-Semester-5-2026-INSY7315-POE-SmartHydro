package com.example.smarthydro

import com.example.smarthydro.transport.BoxAdvertisement
import com.example.smarthydro.transport.ConnectionState
import com.example.smarthydro.transport.DisconnectReason
import com.example.smarthydro.transport.PairSheetAction
import com.example.smarthydro.transport.PairSheetContent
import com.example.smarthydro.transport.PairingFailure
import com.example.smarthydro.transport.PairingState
import com.example.smarthydro.transport.ScanProblem
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks the words and buttons the pairing sheet shows at each step of pairing
class PairSheetContentTest {

    private val box = BoxAdvertisement.parse("SmartHydro-Tower-9B3E01", "own", -50, 0L)!!

    private fun content(state: PairingState) = PairSheetContent.from(state)!!

    // ---- asking ----

    @Test
    fun theSheetOffersTheBoxByName() {
        val sheet = content(PairingState.Confirming(box, listOf(box)))

        assertEquals("SmartHydro-Tower-9B3E01", sheet.title)
        assertEquals("Tap to connect to this box", sheet.subtitle)
        assertEquals("Connect", sheet.primaryLabel)
        assertEquals(PairSheetAction.CONNECT, sheet.primaryAction)
        assertEquals("Not this box", sheet.secondaryLabel)
        assertFalse(sheet.working)
        assertFalse(sheet.showCode)
        assertFalse(sheet.connected)
    }

    // ---- connecting ----

    @Test
    fun whileConnectingTheMainButtonShowsProgressAndDoesNothing() {
        val sheet = content(PairingState.Connecting(box, ConnectionState.NegotiatingMtu))

        assertEquals("Connecting to SmartHydro-Tower-9B3E01…", sheet.subtitle)
        assertEquals("Connecting…", sheet.primaryLabel)
        assertEquals(PairSheetAction.NONE, sheet.primaryAction)
        assertTrue(sheet.working)
        assertEquals("Cancel", sheet.secondaryLabel)
        assertFalse(sheet.showCode)
    }

    @Test
    fun whilePairingTheBoxScreenShowsWhereTheCodeWillBe() {
        val sheet = content(PairingState.Connecting(box, ConnectionState.Pairing(247)))

        assertEquals("Enter the 6-digit code shown on the box's screen.", sheet.subtitle)
        assertTrue(sheet.showCode)
        assertTrue(sheet.working)
    }

    // ---- done ----

    @Test
    fun whenDoneTheSheetSaysConnectedAndOffersDone() {
        val sheet = content(PairingState.Done(box))

        assertEquals("Connected", sheet.title)
        assertTrue(sheet.subtitle.contains("SmartHydro-Tower-9B3E01"))
        assertEquals("Done", sheet.primaryLabel)
        assertEquals(PairSheetAction.CLOSE, sheet.primaryAction)
        assertNull(sheet.secondaryLabel)
        assertTrue(sheet.connected)
        assertFalse(sheet.working)
    }

    // ---- failing ----

    @Test
    fun aFailedConnectionGivesItsReasonAndAChanceToTryAgain() {
        val failure = PairingFailure.Connect(DisconnectReason.PAIRING_FAILED)
        val sheet = content(PairingState.Failed(failure))

        assertEquals("Couldn't connect", sheet.title)
        assertEquals(DisconnectReason.PAIRING_FAILED.message, sheet.subtitle)
        assertEquals("Try again", sheet.primaryLabel)
        assertEquals(PairSheetAction.TRY_AGAIN, sheet.primaryAction)
        assertEquals("Close", sheet.secondaryLabel)
    }

    @Test
    fun aStaleParingTellsThePersonHowToFixIt() {
        val sheet = content(PairingState.Failed(PairingFailure.Connect(DisconnectReason.STALE_PAIRING)))

        assertTrue(sheet.subtitle.contains("Bluetooth settings"))
    }

    // ---- the steps that have no sheet ----

    @Test
    fun stepsTheScreenShowsItselfHaveNoSheet() {
        assertNull(PairSheetContent.from(PairingState.Idle))
        assertNull(PairSheetContent.from(PairingState.NeedsPermission(listOf("p"))))
        assertNull(PairSheetContent.from(PairingState.Searching(listOf(box), finished = false)))
        assertNull(PairSheetContent.from(PairingState.Failed(PairingFailure.NoBoxFound)))
        assertNull(PairSheetContent.from(PairingState.Failed(PairingFailure.PermissionDenied)))
        assertNull(PairSheetContent.from(PairingState.Failed(PairingFailure.Search(ScanProblem.BLUETOOTH_OFF))))
    }

    @Test
    fun everyStepWithASheetGetsOne() {
        val states = listOf(
            PairingState.Confirming(box, listOf(box)),
            PairingState.Connecting(box, ConnectionState.Connecting),
            PairingState.Done(box),
            PairingState.Failed(PairingFailure.Connect(DisconnectReason.LOST))
        )

        states.forEach { assertNotNull("$it", PairSheetContent.from(it)) }
    }

    @Test
    fun onlyAConnectingSheetHasNothingToPress() {
        val states = listOf(
            PairingState.Confirming(box, listOf(box)),
            PairingState.Done(box),
            PairingState.Failed(PairingFailure.Connect(DisconnectReason.LOST))
        )

        states.forEach { assertFalse("$it", content(it).primaryAction == PairSheetAction.NONE) }
        assertEquals(PairSheetAction.NONE, content(PairingState.Connecting(box, ConnectionState.Connecting)).primaryAction)
    }
}
