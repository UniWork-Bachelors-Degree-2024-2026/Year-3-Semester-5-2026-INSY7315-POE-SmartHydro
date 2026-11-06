package com.example.smarthydro.transport

import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.models.BoxTelemetryDto
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.emptyFlow

// One way of talking to one box. The screens only ever see this, so switching a relay or running
// a dose works the same whichever link carries it, and a new link needs no screen changes
interface BoxTransport {

    companion object {

        // What the person is told when a link cannot change the box's mode
        const val MODE_NEEDS_BLUETOOTH_MESSAGE = "The box's mode can only be changed over Bluetooth."
    }

    // Which link this transport uses, for the connection indicator
    val link: BoxLink

    // Switches one relay on or off. The actuator names are the ones BoxControl.actuatorFor returns
    suspend fun setRelay(actuator: String, on: Boolean): BoxControlResult

    // Runs one dose pump for a set volume. The type is the one DosingCatalog gives
    suspend fun dose(type: String, volumeMl: Double): BoxControlResult

    // Reads the box until the calling coroutine is cancelled. onReading gets the newest reading,
    // or null once the box has stopped answering, so a stale reading is never shown as live
    suspend fun readings(onReading: suspend (BoxTelemetryDto?) -> Unit)

    // Asks the box to change its connectivity mode. Only a link that can do this says yes, and any other refuses
    suspend fun setMode(mode: BoxMode): BoxControlResult = BoxControlResult.Refused(MODE_NEEDS_BLUETOOTH_MESSAGE)

    // What the box reports about its connectivity mode, for a link that carries it. The others report nothing
    fun modes(): Flow<BoxModeInfo> = emptyFlow()
}
