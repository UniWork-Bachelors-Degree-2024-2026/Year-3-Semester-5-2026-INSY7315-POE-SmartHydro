package com.example.smarthydro

import android.bluetooth.BluetoothDevice
import com.example.smarthydro.transport.BoxCharacteristic
import com.example.smarthydro.transport.BoxGatt
import com.example.smarthydro.transport.ConnectionAction
import com.example.smarthydro.transport.ConnectionEvent
import com.example.smarthydro.transport.ConnectionMachine
import com.example.smarthydro.transport.ConnectionState
import com.example.smarthydro.transport.DisconnectReason
import com.example.smarthydro.transport.GattStatus
import com.example.smarthydro.transport.ScanProblem
import com.example.smarthydro.transport.Transition
import com.example.smarthydro.transport.statusText
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks every path through a connection to a box, from the first tap to a lost signal
class ConnectionMachineTest {

    private val mtu = 247

    private fun step(state: ConnectionState, event: ConnectionEvent) = ConnectionMachine.reduce(state, event)

    // Plays events one after another and returns where it ends up
    private fun play(start: ConnectionState, vararg events: ConnectionEvent): Transition =
        events.fold(Transition(start)) { last, event -> step(last.state, event) }

    private val connected = ConnectionEvent.GattConnected(GattStatus.SUCCESS)
    private val mtuAgreed = ConnectionEvent.MtuChanged(mtu, GattStatus.SUCCESS)
    private fun discovered(bonded: Boolean) = ConnectionEvent.ServicesDiscovered(GattStatus.SUCCESS, true, bonded)
    private fun enabled(characteristic: BoxCharacteristic, status: Int = GattStatus.SUCCESS) =
        ConnectionEvent.NotificationsEnabled(characteristic, status)

    // A connection up to the point where it is turning on the box's answers
    private fun subscribingToAck(bonded: Boolean = true) = ConnectionState.Subscribing(BoxCharacteristic.ACK, mtu, bonded)

    // ---- the whole way through ----

    @Test
    fun firstConnectionAsksForTheCodeThenGetsReady() {
        var transition = Transition(ConnectionState.Idle)

        transition = step(transition.state, ConnectionEvent.ConnectRequested)
        assertEquals(ConnectionState.Connecting, transition.state)
        assertNull(transition.action)

        transition = step(transition.state, connected)
        assertEquals(ConnectionState.NegotiatingMtu, transition.state)
        assertEquals(ConnectionAction.RequestMtu(BoxGatt.REQUESTED_MTU), transition.action)

        transition = step(transition.state, mtuAgreed)
        assertEquals(ConnectionState.DiscoveringServices(mtu), transition.state)
        assertEquals(ConnectionAction.DiscoverServices, transition.action)

        transition = step(transition.state, discovered(bonded = false))
        assertEquals(ConnectionState.Subscribing(BoxCharacteristic.TELEMETRY, mtu, false), transition.state)
        assertEquals(ConnectionAction.EnableNotifications(BoxCharacteristic.TELEMETRY), transition.action)

        transition = step(transition.state, enabled(BoxCharacteristic.TELEMETRY))
        assertEquals(ConnectionState.Pairing(mtu), transition.state)
        assertEquals(ConnectionAction.CreateBond, transition.action)

        transition = step(transition.state, ConnectionEvent.BondChanged(BluetoothDevice.BOND_BONDING))
        assertEquals(ConnectionState.Pairing(mtu), transition.state)
        assertNull(transition.action)

        transition = step(transition.state, ConnectionEvent.BondChanged(BluetoothDevice.BOND_BONDED))
        assertEquals(subscribingToAck(), transition.state)
        assertEquals(ConnectionAction.EnableNotifications(BoxCharacteristic.ACK), transition.action)

        transition = step(transition.state, enabled(BoxCharacteristic.ACK))
        assertEquals(ConnectionState.Ready(mtu), transition.state)
    }

    @Test
    fun phoneAlreadyPairedSkipsTheCode() {
        val transition = play(
            ConnectionState.Idle,
            ConnectionEvent.ConnectRequested, connected, mtuAgreed, discovered(bonded = true),
            enabled(BoxCharacteristic.TELEMETRY)
        )

        assertEquals(subscribingToAck(), transition.state)
        assertEquals(ConnectionAction.EnableNotifications(BoxCharacteristic.ACK), transition.action)
    }

    @Test
    fun pairedPhoneReachesReady() {
        val transition = play(
            ConnectionState.Idle,
            ConnectionEvent.ConnectRequested, connected, mtuAgreed, discovered(bonded = true),
            enabled(BoxCharacteristic.TELEMETRY), enabled(BoxCharacteristic.ACK)
        )

        assertEquals(ConnectionState.Ready(mtu), transition.state)
    }

    // ---- the message size ----

    @Test
    fun smallMessageSizeStillWorksButIsFlagged() {
        assertTrue(ConnectionState.Ready(BoxGatt.MIN_MTU).limitedMtu)
        assertTrue(ConnectionState.Ready(BoxGatt.FULL_READING_MTU - 1).limitedMtu)
        assertFalse(ConnectionState.Ready(BoxGatt.FULL_READING_MTU).limitedMtu)
        assertFalse(ConnectionState.Ready(256).limitedMtu)
    }

    @Test
    fun messageSizeBelowTheMinimumEndsTheConnection() {
        val transition = step(ConnectionState.NegotiatingMtu, ConnectionEvent.MtuChanged(BoxGatt.MIN_MTU - 1, GattStatus.SUCCESS))

        assertEquals(ConnectionState.Disconnected(DisconnectReason.MTU_TOO_SMALL), transition.state)
        assertEquals(ConnectionAction.Disconnect, transition.action)
    }

    @Test
    fun minimumMessageSizeIsAccepted() {
        val transition = step(ConnectionState.NegotiatingMtu, ConnectionEvent.MtuChanged(BoxGatt.MIN_MTU, GattStatus.SUCCESS))

        assertEquals(ConnectionState.DiscoveringServices(BoxGatt.MIN_MTU), transition.state)
    }

    @Test
    fun failedMessageSizeRequestEndsTheConnection() {
        val transition = step(ConnectionState.NegotiatingMtu, ConnectionEvent.MtuChanged(23, 133))

        assertEquals(ConnectionState.Disconnected(DisconnectReason.MTU_TOO_SMALL), transition.state)
    }

    // ---- things going wrong while connecting ----

    @Test
    fun failedAttemptEndsTheConnection() {
        val transition = step(ConnectionState.Connecting, ConnectionEvent.GattConnected(133))

        assertEquals(ConnectionState.Disconnected(DisconnectReason.CONNECT_FAILED), transition.state)
        assertEquals(ConnectionAction.Disconnect, transition.action)
    }

    @Test
    fun dropWhileConnectingIsAFailedAttempt() {
        val transition = step(ConnectionState.Connecting, ConnectionEvent.GattDisconnected(133))

        assertEquals(ConnectionState.Disconnected(DisconnectReason.CONNECT_FAILED), transition.state)
    }

    @Test
    fun deviceWithoutTheBoxServiceIsNotABox() {
        val missing = ConnectionEvent.ServicesDiscovered(GattStatus.SUCCESS, hasBoxService = false, isBonded = false)
        val transition = step(ConnectionState.DiscoveringServices(mtu), missing)

        assertEquals(ConnectionState.Disconnected(DisconnectReason.NOT_A_BOX), transition.state)
        assertEquals(ConnectionAction.Disconnect, transition.action)
    }

    @Test
    fun failedServiceLookupIsNotABox() {
        val failed = ConnectionEvent.ServicesDiscovered(129, hasBoxService = true, isBonded = false)

        assertEquals(
            ConnectionState.Disconnected(DisconnectReason.NOT_A_BOX),
            step(ConnectionState.DiscoveringServices(mtu), failed).state
        )
    }

    @Test
    fun failedReadingsSubscriptionEndsTheConnection() {
        val state = ConnectionState.Subscribing(BoxCharacteristic.TELEMETRY, mtu, true)

        assertEquals(
            ConnectionState.Disconnected(DisconnectReason.CONNECT_FAILED),
            step(state, enabled(BoxCharacteristic.TELEMETRY, status = 133)).state
        )
    }

    // ---- pairing ----

    @Test
    fun cancelledOrWrongCodeFailsPairing() {
        val transition = step(ConnectionState.Pairing(mtu), ConnectionEvent.BondChanged(BluetoothDevice.BOND_NONE))

        assertEquals(ConnectionState.Disconnected(DisconnectReason.PAIRING_FAILED), transition.state)
        assertEquals(ConnectionAction.Disconnect, transition.action)
    }

    @Test
    fun boxDroppingTheLinkWhilePairingFailsPairing() {
        // The box ends the connection itself when a phone fails to authenticate
        val transition = step(ConnectionState.Pairing(mtu), ConnectionEvent.GattDisconnected(GattStatus.PEER_TERMINATED))

        assertEquals(ConnectionState.Disconnected(DisconnectReason.PAIRING_FAILED), transition.state)
    }

    @Test
    fun refusedAnswersForAPairedPhoneMeansTheBoxForgotIt() {
        val auth = step(subscribingToAck(bonded = true), enabled(BoxCharacteristic.ACK, GattStatus.INSUFFICIENT_AUTHENTICATION))
        val encryption = step(subscribingToAck(bonded = true), enabled(BoxCharacteristic.ACK, GattStatus.INSUFFICIENT_ENCRYPTION))

        assertEquals(ConnectionState.Disconnected(DisconnectReason.STALE_PAIRING), auth.state)
        assertEquals(ConnectionState.Disconnected(DisconnectReason.STALE_PAIRING), encryption.state)
        assertEquals(ConnectionAction.Disconnect, auth.action)
    }

    @Test
    fun otherAnswerFailuresAreAFailedConnection() {
        assertEquals(
            ConnectionState.Disconnected(DisconnectReason.CONNECT_FAILED),
            step(subscribingToAck(), enabled(BoxCharacteristic.ACK, status = 133)).state
        )
    }

    // ---- a connection that was working ----

    @Test
    fun lostSignalIsReportedAsOutOfRange() {
        listOf(GattStatus.CONNECTION_TIMEOUT, GattStatus.LMP_TIMEOUT).forEach {
            val transition = step(ConnectionState.Ready(mtu), ConnectionEvent.GattDisconnected(it))

            assertEquals("status $it", ConnectionState.Disconnected(DisconnectReason.LOST_SIGNAL), transition.state)
            assertEquals(ConnectionAction.Disconnect, transition.action)
        }
    }

    @Test
    fun boxHangingUpIsReportedAsTheBoxEndingIt() {
        listOf(GattStatus.PEER_TERMINATED, GattStatus.SUCCESS).forEach {
            assertEquals(
                "status $it",
                ConnectionState.Disconnected(DisconnectReason.BOX_CLOSED),
                step(ConnectionState.Ready(mtu), ConnectionEvent.GattDisconnected(it)).state
            )
        }
    }

    @Test
    fun anythingElseIsALostConnection() {
        assertEquals(
            ConnectionState.Disconnected(DisconnectReason.LOST),
            step(ConnectionState.Ready(mtu), ConnectionEvent.GattDisconnected(133)).state
        )
    }

    // ---- the person closing it, and trying again ----

    @Test
    fun closingFromAnyActiveStateEndsItAsTheUsers() {
        listOf(
            ConnectionState.Connecting,
            ConnectionState.NegotiatingMtu,
            ConnectionState.DiscoveringServices(mtu),
            subscribingToAck(),
            ConnectionState.Pairing(mtu),
            ConnectionState.Ready(mtu)
        ).forEach {
            val transition = step(it, ConnectionEvent.DisconnectRequested)

            assertEquals("from $it", ConnectionState.Disconnected(DisconnectReason.USER), transition.state)
            assertEquals(ConnectionAction.Disconnect, transition.action)
        }
    }

    @Test
    fun closingWhenThereIsNothingToCloseChangesNothing() {
        val over = ConnectionState.Disconnected(DisconnectReason.LOST_SIGNAL)

        assertEquals(Transition(ConnectionState.Idle), step(ConnectionState.Idle, ConnectionEvent.DisconnectRequested))
        assertEquals(Transition(over), step(over, ConnectionEvent.DisconnectRequested))
    }

    @Test
    fun tryingAgainAfterADropStartsOver() {
        val over = ConnectionState.Disconnected(DisconnectReason.LOST_SIGNAL)

        assertEquals(Transition(ConnectionState.Connecting), step(over, ConnectionEvent.ConnectRequested))
    }

    @Test
    fun askingToConnectWhileAlreadyConnectedChangesNothing() {
        assertEquals(Transition(ConnectionState.Ready(mtu)), step(ConnectionState.Ready(mtu), ConnectionEvent.ConnectRequested))
        assertEquals(Transition(ConnectionState.Connecting), step(ConnectionState.Connecting, ConnectionEvent.ConnectRequested))
    }

    // ---- events that arrive at the wrong time ----

    @Test
    fun lateEventsAfterTheEndAreIgnored() {
        val over = ConnectionState.Disconnected(DisconnectReason.USER)

        listOf(
            ConnectionEvent.GattDisconnected(0), mtuAgreed, connected,
            enabled(BoxCharacteristic.ACK), ConnectionEvent.BondChanged(BluetoothDevice.BOND_BONDED)
        ).forEach { assertEquals("$it", Transition(over), step(over, it)) }
    }

    @Test
    fun eventsBeforeAnythingStartedAreIgnored() {
        listOf(ConnectionEvent.GattDisconnected(0), connected, mtuAgreed, enabled(BoxCharacteristic.TELEMETRY))
            .forEach { assertEquals("$it", Transition(ConnectionState.Idle), step(ConnectionState.Idle, it)) }
    }

    @Test
    fun anAnswerForTheWrongCharacteristicIsIgnored() {
        val state = ConnectionState.Subscribing(BoxCharacteristic.TELEMETRY, mtu, true)

        assertEquals(Transition(state), step(state, enabled(BoxCharacteristic.ACK)))
    }

    @Test
    fun readyStateIgnoresSetupEvents() {
        val ready = ConnectionState.Ready(mtu)

        assertEquals(Transition(ready), step(ready, mtuAgreed))
        assertEquals(Transition(ready), step(ready, enabled(BoxCharacteristic.ACK)))
    }

    // ---- the words ----

    @Test
    fun everyReasonHasItsOwnWords() {
        val messages = DisconnectReason.entries.map { it.message }

        assertTrue(messages.all { it.isNotBlank() })
        assertEquals(messages.size, messages.toSet().size)
    }

    @Test
    fun savedBoxStatusSaysHowTheConnectionIsDoing() {
        assertEquals("Connected over Bluetooth", ConnectionState.Ready(mtu).statusText())
        assertEquals("Not connected", ConnectionState.Idle.statusText())
        assertEquals("Waiting for the code on the box's screen…", ConnectionState.Pairing(mtu).statusText())
        assertEquals(DisconnectReason.LOST_SIGNAL.message, ConnectionState.Disconnected(DisconnectReason.LOST_SIGNAL).statusText())
        listOf(
            ConnectionState.Connecting, ConnectionState.NegotiatingMtu, ConnectionState.DiscoveringServices(mtu),
            ConnectionState.Subscribing(BoxCharacteristic.ACK, mtu, true)
        ).forEach { assertEquals("Connecting…", it.statusText()) }
    }

    @Test
    fun aConnectionThatCouldNotStartHasWordsForWhy() {
        assertEquals(DisconnectReason.PERMISSION_MISSING, DisconnectReason.fromProblem(ScanProblem.PERMISSION_MISSING))
        assertEquals(DisconnectReason.NO_BLUETOOTH, DisconnectReason.fromProblem(ScanProblem.NO_BLUETOOTH))
        assertEquals(DisconnectReason.BLUETOOTH_OFF, DisconnectReason.fromProblem(ScanProblem.BLUETOOTH_OFF))
        assertEquals(DisconnectReason.CONNECT_FAILED, DisconnectReason.fromProblem(ScanProblem.BUSY))
        assertEquals(DisconnectReason.CONNECT_FAILED, DisconnectReason.fromProblem(ScanProblem.FAILED))
    }

    @Test
    fun theSameWordsAreUsedForASearchAndAConnection() {
        assertEquals(ScanProblem.BLUETOOTH_OFF.message, DisconnectReason.BLUETOOTH_OFF.message)
        assertEquals(ScanProblem.PERMISSION_MISSING.message, DisconnectReason.PERMISSION_MISSING.message)
    }

    @Test
    fun characteristicIsFoundByItsId() {
        assertEquals(BoxCharacteristic.TELEMETRY, BoxCharacteristic.fromUuid("0000ff01-0000-1000-8000-00805f9b34fb"))
        assertEquals(BoxCharacteristic.ACK, BoxCharacteristic.fromUuid("0000FF03-0000-1000-8000-00805F9B34FB"))
        assertNull(BoxCharacteristic.fromUuid("00002902-0000-1000-8000-00805f9b34fb"))
        assertNull(BoxCharacteristic.fromUuid(null))
    }

    @Test
    fun characteristicIdsMatchTheFirmware() {
        assertEquals("0000ff01-0000-1000-8000-00805f9b34fb", BoxCharacteristic.TELEMETRY.uuid)
        assertEquals("0000ff02-0000-1000-8000-00805f9b34fb", BoxCharacteristic.COMMAND.uuid)
        assertEquals("0000ff03-0000-1000-8000-00805f9b34fb", BoxCharacteristic.ACK.uuid)
    }
}
