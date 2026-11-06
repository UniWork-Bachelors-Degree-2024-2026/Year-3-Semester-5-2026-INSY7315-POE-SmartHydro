package com.example.smarthydro.transport

// What to do about the Bluetooth connection to a structure's saved box
enum class ReconnectDecision {
    // Open the connection now
    CONNECT,

    // Leave it be and look again shortly, because it is working, being made, or will come back by itself
    WAIT,

    // Stop trying, because nothing more can be done without the person
    STOP
}

// Decides whether a saved box should be connected again, so a farmer who paired once does not have to pair again
// after the app restarts or the box goes out of range for a while. It does no Bluetooth itself
object ReconnectPolicy {

    // How long to leave between looks while a connection is working, being made or waiting for Bluetooth
    const val WAIT_MS = 2_000L

    private const val FIRST_BACKOFF_MS = 5_000L
    private const val LONGEST_BACKOFF_MS = 60_000L

    // state is how the connection to this structure's box is doing, or null when none has been opened
    fun decide(hasSavedBox: Boolean, permissionsMissing: Boolean, state: ConnectionState?): ReconnectDecision = when {
        !hasSavedBox -> ReconnectDecision.STOP

        // Asking for permissions is for the pairing screen, so a connection is never opened without them
        permissionsMissing -> ReconnectDecision.STOP

        state == null || state is ConnectionState.Idle -> ReconnectDecision.CONNECT

        state is ConnectionState.Disconnected -> afterEnding(state.reason)

        // Ready, or on its way to it
        else -> ReconnectDecision.WAIT
    }

    // A connection that ended for a reason trying again would not change is left alone, one that was only lost is
    // opened again, and one that ended because Bluetooth was off is waited on until it is switched back on
    private fun afterEnding(reason: DisconnectReason): ReconnectDecision = when (reason) {
        DisconnectReason.CONNECT_FAILED,
        DisconnectReason.LOST_SIGNAL,
        DisconnectReason.BOX_CLOSED,
        DisconnectReason.LOST -> ReconnectDecision.CONNECT

        DisconnectReason.BLUETOOTH_OFF -> ReconnectDecision.WAIT

        // The person chose this, or it needs the person to fix it, or it cannot work on this phone or box
        DisconnectReason.USER,
        DisconnectReason.PAIRING_FAILED,
        DisconnectReason.STALE_PAIRING,
        DisconnectReason.NOT_A_BOX,
        DisconnectReason.MTU_TOO_SMALL,
        DisconnectReason.PERMISSION_MISSING,
        DisconnectReason.NO_BLUETOOTH -> ReconnectDecision.STOP
    }

    // How long to wait before the next attempt after this many have been made without a connection working. It
    // doubles from 5 seconds up to a minute, so a box that is switched off is not tried every few seconds
    fun backoffMs(attemptsMade: Int): Long {
        if (attemptsMade <= 0) return 0L

        var wait = FIRST_BACKOFF_MS
        repeat(attemptsMade - 1) { wait = minOf(wait * 2, LONGEST_BACKOFF_MS) }
        return wait
    }
}
