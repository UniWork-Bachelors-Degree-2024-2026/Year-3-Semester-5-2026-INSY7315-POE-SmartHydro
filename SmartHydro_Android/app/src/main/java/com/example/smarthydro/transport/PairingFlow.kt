package com.example.smarthydro.transport

import com.example.smarthydro.models.StructureType

// Why pairing a box over Bluetooth did not work, with the words the person is told
sealed class PairingFailure(val message: String) {
    data object PermissionDenied : PairingFailure("The app can't look for your box without that permission.")
    data object NoBoxFound : PairingFailure("No box found nearby. Make sure it is switched on and close by, then search again.")
    data class Search(val problem: ScanProblem) : PairingFailure(problem.message)
    data class Connect(val reason: DisconnectReason) : PairingFailure(reason.message)
}

// Where pairing a box has got to
sealed interface PairingState {

    // Nothing started yet
    data object Idle : PairingState

    // The phone is asking the person to allow Bluetooth
    data class NeedsPermission(val permissions: List<String>) : PairingState

    // Looking for boxes. finished is true once the search ran out of time, and the list is the boxes of the right kind
    data class Searching(val boxes: List<NearbyBox>, val finished: Boolean) : PairingState

    // A box is waiting for the person to say yes. The list is what was found, to go back to
    data class Confirming(val box: NearbyBox, val boxes: List<NearbyBox>) : PairingState

    // Connecting to the chosen box, as far as the connection has got
    data class Connecting(val box: NearbyBox, val connection: ConnectionState) : PairingState {

        // What to tell the person while it goes on. Pairing is the step where the box's code is needed
        val message: String
            get() = if (connection is ConnectionState.Pairing) {
                "Enter the 6-digit code shown on the box's screen."
            } else {
                "Connecting to ${box.name}…"
            }
    }

    // Paired and connected
    data class Done(val box: NearbyBox) : PairingState

    // Over, with the reason
    data class Failed(val failure: PairingFailure) : PairingState
}

// What the screen tells the flow
sealed interface PairingEvent {

    // The person asked to pair. missingPermissions is what the phone still has to be allowed to do. This also
    // starts the search again after a failure or when the search ran out of time
    data class Start(val missingPermissions: List<String>) : PairingEvent

    // The person answered the permission request
    data class PermissionsAnswered(val allGranted: Boolean) : PairingEvent

    // The search reported something
    data class Scan(val update: ScanUpdate) : PairingEvent

    // The search ran out of time
    data object ScanEnded : PairingEvent

    // The person picked a box from the list
    data class BoxChosen(val address: String) : PairingEvent

    // The person said yes to the box in the sheet
    data object ConfirmConnect : PairingEvent

    // The person closed the sheet
    data object DismissSheet : PairingEvent

    // The connection to the box moved on
    data class Connection(val state: ConnectionState) : PairingEvent

    // The person gave up
    data object Cancel : PairingEvent

    // The person closed the sheet that said the box is connected
    data object Finish : PairingEvent
}

// What the screen has to do next
sealed interface PairingAction {
    data class RequestPermissions(val permissions: List<String>) : PairingAction
    data object StartScan : PairingAction
    data object StopScan : PairingAction
    data class OpenConnection(val address: String) : PairingAction
    data object CloseConnection : PairingAction

    // Keep this box's Bluetooth address on the structure
    data class SaveAddress(val address: String) : PairingAction
}

// The next state, and what to do to get there
data class PairingTransition(val state: PairingState, val action: PairingAction? = null)

// Decides what happens next while pairing a structure's box. It does no Bluetooth itself, so every way pairing can
// go can be tested without a phone. A box that is the structure's own is offered straight away, and when the
// structure has no known box the person picks from the list
class PairingFlow(private val structureMac: String?, private val type: StructureType) {

    fun reduce(state: PairingState, event: PairingEvent): PairingTransition {
        if (event is PairingEvent.Cancel) return cancel(state)

        return when (state) {
            is PairingState.Idle, is PairingState.Failed -> idleOrFailed(state, event)
            is PairingState.NeedsPermission -> needsPermission(state, event)
            is PairingState.Searching -> searching(state, event)
            is PairingState.Confirming -> confirming(state, event)
            is PairingState.Connecting -> connecting(state, event)
            // Once paired nothing undoes it, except closing the sheet that says so
            is PairingState.Done -> if (event is PairingEvent.Finish) PairingTransition(PairingState.Idle) else PairingTransition(state)
        }
    }

    private fun idleOrFailed(state: PairingState, event: PairingEvent): PairingTransition =
        if (event is PairingEvent.Start) start(event) else PairingTransition(state)

    private fun start(event: PairingEvent.Start): PairingTransition =
        if (event.missingPermissions.isNotEmpty()) {
            PairingTransition(PairingState.NeedsPermission(event.missingPermissions), PairingAction.RequestPermissions(event.missingPermissions))
        } else {
            PairingTransition(PairingState.Searching(emptyList(), finished = false), PairingAction.StartScan)
        }

    private fun needsPermission(state: PairingState.NeedsPermission, event: PairingEvent): PairingTransition {
        if (event !is PairingEvent.PermissionsAnswered) return PairingTransition(state)

        return if (event.allGranted) {
            PairingTransition(PairingState.Searching(emptyList(), finished = false), PairingAction.StartScan)
        } else {
            PairingTransition(PairingState.Failed(PairingFailure.PermissionDenied))
        }
    }

    private fun searching(state: PairingState.Searching, event: PairingEvent): PairingTransition = when (event) {
        is PairingEvent.Start -> start(event)

        is PairingEvent.Scan -> when (val update = event.update) {
            is ScanUpdate.Problem -> PairingTransition(PairingState.Failed(PairingFailure.Search(update.problem)), PairingAction.StopScan)
            is ScanUpdate.Boxes -> {
                val boxes = visibleBoxes(update.boxes)
                val own = boxes.firstOrNull { isOwn(it) }
                if (own != null) {
                    PairingTransition(PairingState.Confirming(own, boxes), PairingAction.StopScan)
                } else {
                    PairingTransition(PairingState.Searching(boxes, state.finished))
                }
            }
        }

        is PairingEvent.ScanEnded ->
            if (state.boxes.isEmpty()) {
                PairingTransition(PairingState.Failed(PairingFailure.NoBoxFound))
            } else {
                PairingTransition(PairingState.Searching(state.boxes, finished = true))
            }

        is PairingEvent.BoxChosen -> {
            val chosen = state.boxes.firstOrNull { it.address == event.address }
            if (chosen == null) PairingTransition(state) else PairingTransition(PairingState.Confirming(chosen, state.boxes), PairingAction.StopScan)
        }

        else -> PairingTransition(state)
    }

    private fun confirming(state: PairingState.Confirming, event: PairingEvent): PairingTransition = when (event) {
        is PairingEvent.ConfirmConnect ->
            PairingTransition(PairingState.Connecting(state.box, ConnectionState.Connecting), PairingAction.OpenConnection(state.box.address))

        // Closing the sheet goes back to the list, where the box can be picked again
        is PairingEvent.DismissSheet -> PairingTransition(PairingState.Searching(state.boxes, finished = true))

        else -> PairingTransition(state)
    }

    private fun connecting(state: PairingState.Connecting, event: PairingEvent): PairingTransition {
        if (event !is PairingEvent.Connection) return PairingTransition(state)

        return when (val connection = event.state) {
            is ConnectionState.Ready -> PairingTransition(PairingState.Done(state.box), PairingAction.SaveAddress(state.box.address))
            is ConnectionState.Disconnected ->
                PairingTransition(PairingState.Failed(PairingFailure.Connect(connection.reason)), PairingAction.CloseConnection)
            else -> PairingTransition(PairingState.Connecting(state.box, connection))
        }
    }

    private fun cancel(state: PairingState): PairingTransition = when (state) {
        is PairingState.Searching -> PairingTransition(PairingState.Idle, PairingAction.StopScan)
        is PairingState.Connecting -> PairingTransition(PairingState.Idle, PairingAction.CloseConnection)
        is PairingState.Done -> PairingTransition(state)
        else -> PairingTransition(PairingState.Idle)
    }

    // The boxes of the kind this structure uses, the structure's own first and then the strongest signal
    fun visibleBoxes(boxes: List<NearbyBox>): List<NearbyBox> =
        boxes.filter { it.kind == BoxAdvertisement.kindFor(type) }
            .sortedWith(compareByDescending<NearbyBox> { isOwn(it) }.thenByDescending { it.rssi }.thenBy { it.name })

    // Whether a box is the one this structure uses. A structure with no known box has none of its own
    private fun isOwn(box: NearbyBox): Boolean =
        structureMac != null && BoxAdvertisement.matches(box, structureMac, type)
}
