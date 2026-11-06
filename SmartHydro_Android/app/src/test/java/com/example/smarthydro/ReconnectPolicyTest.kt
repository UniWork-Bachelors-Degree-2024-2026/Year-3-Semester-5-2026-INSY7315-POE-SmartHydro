package com.example.smarthydro

import com.example.smarthydro.transport.BoxCharacteristic
import com.example.smarthydro.transport.ConnectionState
import com.example.smarthydro.transport.DisconnectReason
import com.example.smarthydro.transport.ReconnectDecision
import com.example.smarthydro.transport.ReconnectPolicy
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks when a saved box is connected again and how long to wait between attempts
class ReconnectPolicyTest {

    private val mtu = 247

    private fun decide(state: ConnectionState?, saved: Boolean = true, permissionsMissing: Boolean = false) =
        ReconnectPolicy.decide(saved, permissionsMissing, state)

    // ---- when there is nothing to do ----

    @Test
    fun noSavedBoxMeansNothingToConnect() {
        assertEquals(ReconnectDecision.STOP, decide(null, saved = false))
        assertEquals(ReconnectDecision.STOP, decide(ConnectionState.Ready(mtu), saved = false))
    }

    @Test
    fun missingPermissionsMeansNoConnectionIsOpened() {
        assertEquals(ReconnectDecision.STOP, decide(null, permissionsMissing = true))
        assertEquals(ReconnectDecision.STOP, decide(ConnectionState.Idle, permissionsMissing = true))
    }

    // ---- a saved box with no connection yet ----

    @Test
    fun savedBoxWithNoConnectionOpensOne() {
        assertEquals(ReconnectDecision.CONNECT, decide(null))
        assertEquals(ReconnectDecision.CONNECT, decide(ConnectionState.Idle))
    }

    // ---- a connection that is working or on its way ----

    @Test
    fun workingOrStartingConnectionIsLeftAlone() {
        listOf(
            ConnectionState.Ready(mtu), ConnectionState.Connecting, ConnectionState.NegotiatingMtu,
            ConnectionState.DiscoveringServices(mtu), ConnectionState.Pairing(mtu),
            ConnectionState.Subscribing(BoxCharacteristic.TELEMETRY, mtu, true)
        ).forEach { assertEquals("$it", ReconnectDecision.WAIT, decide(it)) }
    }

    // ---- a connection that ended ----

    @Test
    fun lostConnectionIsOpenedAgain() {
        listOf(
            DisconnectReason.LOST_SIGNAL, DisconnectReason.BOX_CLOSED, DisconnectReason.LOST, DisconnectReason.CONNECT_FAILED
        ).forEach { assertEquals("$it", ReconnectDecision.CONNECT, decide(ConnectionState.Disconnected(it))) }
    }

    @Test
    fun bluetoothSwitchedOffIsWaitedOn() {
        assertEquals(ReconnectDecision.WAIT, decide(ConnectionState.Disconnected(DisconnectReason.BLUETOOTH_OFF)))
    }

    @Test
    fun connectionThatNeedsThePersonIsNotRetried() {
        listOf(
            DisconnectReason.USER, DisconnectReason.PAIRING_FAILED, DisconnectReason.STALE_PAIRING,
            DisconnectReason.NOT_A_BOX, DisconnectReason.MTU_TOO_SMALL, DisconnectReason.PERMISSION_MISSING,
            DisconnectReason.NO_BLUETOOTH
        ).forEach { assertEquals("$it", ReconnectDecision.STOP, decide(ConnectionState.Disconnected(it))) }
    }

    @Test
    fun everyReasonIsDecidedOn() {
        DisconnectReason.entries.forEach { decide(ConnectionState.Disconnected(it)) }
    }

    // ---- the wait between attempts ----

    @Test
    fun noWaitBeforeTheFirstAttempt() {
        assertEquals(0L, ReconnectPolicy.backoffMs(0))
        assertEquals(0L, ReconnectPolicy.backoffMs(-1))
    }

    @Test
    fun waitDoublesFromFiveSeconds() {
        assertEquals(5_000L, ReconnectPolicy.backoffMs(1))
        assertEquals(10_000L, ReconnectPolicy.backoffMs(2))
        assertEquals(20_000L, ReconnectPolicy.backoffMs(3))
        assertEquals(40_000L, ReconnectPolicy.backoffMs(4))
    }

    @Test
    fun waitNeverGoesPastAMinute() {
        assertEquals(60_000L, ReconnectPolicy.backoffMs(5))
        assertEquals(60_000L, ReconnectPolicy.backoffMs(6))
        assertEquals(60_000L, ReconnectPolicy.backoffMs(500))
    }

    @Test
    fun waitNeverShrinks() {
        val waits = (1..20).map { ReconnectPolicy.backoffMs(it) }

        assertTrue(waits.zipWithNext().all { (a, b) -> b >= a })
    }
}
