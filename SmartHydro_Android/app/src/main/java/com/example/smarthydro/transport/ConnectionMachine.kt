package com.example.smarthydro.transport

import android.bluetooth.BluetoothDevice

// Decides what happens next in a connection to a box, from where it is and what Android just reported.
// It does no Bluetooth itself: the thin wrapper around Android's GATT connection feeds it events and does
// the action it hands back, so every path through a connection can be tested without a phone
object ConnectionMachine {

    fun reduce(state: ConnectionState, event: ConnectionEvent): Transition {
        // Closing and reopening work from anywhere
        if (event is ConnectionEvent.DisconnectRequested) {
            return if (state is ConnectionState.Idle || state is ConnectionState.Disconnected) {
                Transition(state)
            } else {
                Transition(ConnectionState.Disconnected(DisconnectReason.USER), ConnectionAction.Disconnect)
            }
        }
        if (event is ConnectionEvent.ConnectRequested) {
            return if (state is ConnectionState.Idle || state is ConnectionState.Disconnected) {
                Transition(ConnectionState.Connecting)
            } else {
                Transition(state)
            }
        }

        // A drop ends the connection from any state that is not already over
        if (event is ConnectionEvent.GattDisconnected) {
            return when (state) {
                is ConnectionState.Idle, is ConnectionState.Disconnected -> Transition(state)
                is ConnectionState.Pairing -> end(DisconnectReason.PAIRING_FAILED)
                is ConnectionState.Ready -> end(lossReason(event.status))
                else -> end(DisconnectReason.CONNECT_FAILED)
            }
        }

        return when (state) {
            is ConnectionState.Connecting -> whileConnecting(event)
            is ConnectionState.NegotiatingMtu -> whileNegotiating(event)
            is ConnectionState.DiscoveringServices -> whileDiscovering(state, event)
            is ConnectionState.Subscribing -> whileSubscribing(state, event)
            is ConnectionState.Pairing -> whilePairing(state, event)
            else -> Transition(state)
        }
    }

    private fun whileConnecting(event: ConnectionEvent): Transition =
        if (event is ConnectionEvent.GattConnected) {
            if (event.status == GattStatus.SUCCESS) {
                Transition(ConnectionState.NegotiatingMtu, ConnectionAction.RequestMtu(BoxGatt.REQUESTED_MTU))
            } else {
                end(DisconnectReason.CONNECT_FAILED)
            }
        } else {
            Transition(ConnectionState.Connecting)
        }

    // A failed request leaves the phone on the small default size, which is no use for the box's readings
    private fun whileNegotiating(event: ConnectionEvent): Transition {
        if (event !is ConnectionEvent.MtuChanged) return Transition(ConnectionState.NegotiatingMtu)

        return if (event.status != GattStatus.SUCCESS || event.mtu < BoxGatt.MIN_MTU) {
            end(DisconnectReason.MTU_TOO_SMALL)
        } else {
            Transition(ConnectionState.DiscoveringServices(event.mtu), ConnectionAction.DiscoverServices)
        }
    }

    // Readings come first because any phone may read them, so they show while the person is still pairing
    private fun whileDiscovering(state: ConnectionState.DiscoveringServices, event: ConnectionEvent): Transition {
        if (event !is ConnectionEvent.ServicesDiscovered) return Transition(state)

        return if (event.status == GattStatus.SUCCESS && event.hasBoxService) {
            Transition(
                ConnectionState.Subscribing(BoxCharacteristic.TELEMETRY, state.mtu, event.isBonded),
                ConnectionAction.EnableNotifications(BoxCharacteristic.TELEMETRY)
            )
        } else {
            end(DisconnectReason.NOT_A_BOX)
        }
    }

    private fun whileSubscribing(state: ConnectionState.Subscribing, event: ConnectionEvent): Transition {
        if (event !is ConnectionEvent.NotificationsEnabled || event.characteristic != state.next) return Transition(state)

        if (event.status == GattStatus.SUCCESS) {
            return when {
                state.next == BoxCharacteristic.ACK -> Transition(ConnectionState.Ready(state.mtu))
                state.bonded -> Transition(
                    ConnectionState.Subscribing(BoxCharacteristic.ACK, state.mtu, true),
                    ConnectionAction.EnableNotifications(BoxCharacteristic.ACK)
                )
                else -> Transition(ConnectionState.Pairing(state.mtu), ConnectionAction.CreateBond)
            }
        }

        // A phone that was paired before and is refused now has keys the box no longer has, for example after
        // the box was reflashed
        val refusedForSecurity = event.status == GattStatus.INSUFFICIENT_AUTHENTICATION ||
            event.status == GattStatus.INSUFFICIENT_ENCRYPTION
        return if (state.next == BoxCharacteristic.ACK && refusedForSecurity) {
            end(DisconnectReason.STALE_PAIRING)
        } else {
            end(DisconnectReason.CONNECT_FAILED)
        }
    }

    private fun whilePairing(state: ConnectionState.Pairing, event: ConnectionEvent): Transition {
        if (event !is ConnectionEvent.BondChanged) return Transition(state)

        return when (event.bondState) {
            BluetoothDevice.BOND_BONDED -> Transition(
                ConnectionState.Subscribing(BoxCharacteristic.ACK, state.mtu, true),
                ConnectionAction.EnableNotifications(BoxCharacteristic.ACK)
            )
            BluetoothDevice.BOND_NONE -> end(DisconnectReason.PAIRING_FAILED)
            else -> Transition(state)
        }
    }

    // Why a connection that was working dropped
    private fun lossReason(status: Int): DisconnectReason = when (status) {
        GattStatus.CONNECTION_TIMEOUT, GattStatus.LMP_TIMEOUT -> DisconnectReason.LOST_SIGNAL
        GattStatus.SUCCESS, GattStatus.PEER_TERMINATED -> DisconnectReason.BOX_CLOSED
        else -> DisconnectReason.LOST
    }

    private fun end(reason: DisconnectReason) =
        Transition(ConnectionState.Disconnected(reason), ConnectionAction.Disconnect)
}
