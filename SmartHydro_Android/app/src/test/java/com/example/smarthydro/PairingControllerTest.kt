package com.example.smarthydro

import com.example.smarthydro.models.StructureType
import com.example.smarthydro.transport.BoxAdvertisement
import com.example.smarthydro.transport.ConnectionState
import com.example.smarthydro.transport.DisconnectReason
import com.example.smarthydro.transport.PairSheetAction
import com.example.smarthydro.transport.PairingController
import com.example.smarthydro.transport.PairingEffects
import com.example.smarthydro.transport.PairingEvent
import com.example.smarthydro.transport.PairingFailure
import com.example.smarthydro.transport.PairingFlow
import com.example.smarthydro.transport.PairingState
import com.example.smarthydro.transport.ScanUpdate
import com.example.smarthydro.transport.toEvent
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks that pairing does what the flow says, through fake effects, and what the sheet's buttons mean
class PairingControllerTest {

    // Writes down everything pairing asked for
    private class FakeEffects : PairingEffects {
        val calls = mutableListOf<String>()
        override fun requestPermissions(permissions: List<String>) { calls += "permissions:${permissions.size}" }
        override fun startScan() { calls += "startScan" }
        override fun stopScan() { calls += "stopScan" }
        override fun openConnection(address: String) { calls += "open:$address" }
        override fun closeConnection() { calls += "close" }
        override fun saveAddress(address: String) { calls += "save:$address" }
    }

    private val ownMac = "A4:CF:12:9B:3E:01"
    private val ownBox = BoxAdvertisement.parse("SmartHydro-Tower-9B3E01", "own", -50, 0L)!!
    private val permissions = listOf("a", "b")

    private val effects = FakeEffects()
    private val controller = PairingController(PairingFlow(ownMac, StructureType.HYDROPONIC), effects)

    private fun send(vararg events: PairingEvent) = events.forEach { controller.send(it) }

    private fun heardOwnBox() = PairingEvent.Scan(ScanUpdate.Boxes(listOf(ownBox)))

    // ---- doing what the flow says ----

    @Test
    fun startsIdleAndDoesNothing() {
        assertEquals(PairingState.Idle, controller.state.value)
        assertTrue(effects.calls.isEmpty())
    }

    @Test
    fun missingPermissionsAreAskedFor() {
        send(PairingEvent.Start(permissions))

        assertEquals(listOf("permissions:2"), effects.calls)
        assertEquals(PairingState.NeedsPermission(permissions), controller.state.value)
    }

    @Test
    fun allowingPermissionsStartsTheSearch() {
        send(PairingEvent.Start(permissions), PairingEvent.PermissionsAnswered(true))

        assertEquals(listOf("permissions:2", "startScan"), effects.calls)
    }

    @Test
    fun findingTheOwnBoxStopsTheSearchAndOffersIt() {
        send(PairingEvent.Start(emptyList()), heardOwnBox())

        assertEquals(listOf("startScan", "stopScan"), effects.calls)
        assertTrue(controller.state.value is PairingState.Confirming)
    }

    @Test
    fun sayingYesOpensTheConnectionToThatBox() {
        send(PairingEvent.Start(emptyList()), heardOwnBox(), PairingEvent.ConfirmConnect)

        assertEquals("open:own", effects.calls.last())
        assertTrue(controller.state.value is PairingState.Connecting)
    }

    @Test
    fun aReadyConnectionSavesTheBox() {
        send(
            PairingEvent.Start(emptyList()), heardOwnBox(), PairingEvent.ConfirmConnect,
            PairingEvent.Connection(ConnectionState.Pairing(247)),
            PairingEvent.Connection(ConnectionState.Ready(247))
        )

        assertEquals("save:own", effects.calls.last())
        assertEquals(PairingState.Done(ownBox), controller.state.value)
    }

    @Test
    fun aFailedConnectionClosesIt() {
        send(
            PairingEvent.Start(emptyList()), heardOwnBox(), PairingEvent.ConfirmConnect,
            PairingEvent.Connection(ConnectionState.Disconnected(DisconnectReason.PAIRING_FAILED))
        )

        assertEquals("close", effects.calls.last())
        assertEquals(PairingState.Failed(PairingFailure.Connect(DisconnectReason.PAIRING_FAILED)), controller.state.value)
    }

    @Test
    fun givingUpWhileConnectingClosesTheConnection() {
        send(PairingEvent.Start(emptyList()), heardOwnBox(), PairingEvent.ConfirmConnect, PairingEvent.Cancel)

        assertEquals("close", effects.calls.last())
        assertEquals(PairingState.Idle, controller.state.value)
    }

    @Test
    fun eventsThatDoNothingAskForNothing() {
        send(PairingEvent.ConfirmConnect, PairingEvent.ScanEnded, PairingEvent.Cancel)

        assertTrue(effects.calls.isEmpty())
    }

    @Test
    fun closingTheConnectedSheetGoesBackToIdleWithoutTouchingTheConnection() {
        send(
            PairingEvent.Start(emptyList()), heardOwnBox(), PairingEvent.ConfirmConnect,
            PairingEvent.Connection(ConnectionState.Ready(247))
        )
        val before = effects.calls.size

        send(PairingEvent.Finish)

        assertEquals(PairingState.Idle, controller.state.value)
        assertEquals(before, effects.calls.size)
    }

    // ---- what the sheet's buttons mean ----

    private val confirming = PairingState.Confirming(ownBox, listOf(ownBox))
    private val connecting = PairingState.Connecting(ownBox, ConnectionState.Pairing(247))
    private val done = PairingState.Done(ownBox)
    private val failed = PairingState.Failed(PairingFailure.Connect(DisconnectReason.LOST))

    @Test
    fun connectAndTryAgainMeanTheSameWhateverTheStep() {
        assertEquals(PairingEvent.ConfirmConnect, PairSheetAction.CONNECT.toEvent(confirming, permissions))
        assertEquals(PairingEvent.Start(permissions), PairSheetAction.TRY_AGAIN.toEvent(failed, permissions))
    }

    @Test
    fun closingMeansSomethingDifferentAtEachStep() {
        assertEquals(PairingEvent.DismissSheet, PairSheetAction.CLOSE.toEvent(confirming, permissions))
        assertEquals(PairingEvent.Cancel, PairSheetAction.CLOSE.toEvent(connecting, permissions))
        assertEquals(PairingEvent.Cancel, PairSheetAction.CLOSE.toEvent(failed, permissions))
        assertEquals(PairingEvent.Finish, PairSheetAction.CLOSE.toEvent(done, permissions))
    }

    @Test
    fun aButtonThatOnlyShowsProgressDoesNothing() {
        assertNull(PairSheetAction.NONE.toEvent(connecting, permissions))
    }

    // ---- the words for the list ----

    @Test
    fun signalIsDescribedInAWord() {
        assertEquals("Strong signal", ownBox.copy(rssi = -45).signalLabel)
        assertEquals("Strong signal", ownBox.copy(rssi = -60).signalLabel)
        assertEquals("Good signal", ownBox.copy(rssi = -61).signalLabel)
        assertEquals("Good signal", ownBox.copy(rssi = -75).signalLabel)
        assertEquals("Weak signal", ownBox.copy(rssi = -76).signalLabel)
    }
}
