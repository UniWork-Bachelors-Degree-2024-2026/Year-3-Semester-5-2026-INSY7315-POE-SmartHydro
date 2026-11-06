package com.example.smarthydro.transport

// What a button in the pairing sheet does when it is pressed. NONE is a button that is only there to show progress
enum class PairSheetAction { CONNECT, TRY_AGAIN, CLOSE, NONE }

// What the flow is told when a button of the sheet is pressed, going by the step the sheet is showing. Closing means
// something different at each step, and a button that is only showing progress does nothing
fun PairSheetAction.toEvent(state: PairingState, missingPermissions: List<String>): PairingEvent? = when (this) {
    PairSheetAction.CONNECT -> PairingEvent.ConfirmConnect
    PairSheetAction.TRY_AGAIN -> PairingEvent.Start(missingPermissions)
    PairSheetAction.NONE -> null
    PairSheetAction.CLOSE -> when (state) {
        is PairingState.Confirming -> PairingEvent.DismissSheet
        is PairingState.Done -> PairingEvent.Finish
        else -> PairingEvent.Cancel
    }
}

// What the pairing sheet shows. The sheet only draws this, so the words and the buttons for each step of pairing
// are decided here and can be tested
data class PairSheetContent(
    val title: String,
    val subtitle: String,
    val primaryLabel: String,
    val primaryAction: PairSheetAction,
    val secondaryLabel: String?,

    // Shows the box with dots on its screen, where the code will appear
    val showCode: Boolean = false,

    // Shows the box with a tick
    val connected: Boolean = false,

    // Shows a spinner on the main button while the connection is being made
    val working: Boolean = false
) {

    companion object {

        // The sheet for a step of pairing, or null for the steps that have no sheet, which the screen shows itself
        fun from(state: PairingState): PairSheetContent? = when (state) {
            is PairingState.Confirming -> PairSheetContent(
                title = state.box.name,
                subtitle = "Tap to connect to this box",
                primaryLabel = "Connect",
                primaryAction = PairSheetAction.CONNECT,
                secondaryLabel = "Not this box"
            )

            is PairingState.Connecting -> PairSheetContent(
                title = state.box.name,
                subtitle = state.message,
                primaryLabel = "Connecting…",
                primaryAction = PairSheetAction.NONE,
                secondaryLabel = "Cancel",
                showCode = state.connection is ConnectionState.Pairing,
                working = true
            )

            is PairingState.Done -> PairSheetContent(
                title = "Connected",
                subtitle = "${state.box.name} is ready. You can control your box over Bluetooth.",
                primaryLabel = "Done",
                primaryAction = PairSheetAction.CLOSE,
                secondaryLabel = null,
                connected = true
            )

            is PairingState.Failed -> (state.failure as? PairingFailure.Connect)?.let {
                PairSheetContent(
                    title = "Couldn't connect",
                    subtitle = it.message,
                    primaryLabel = "Try again",
                    primaryAction = PairSheetAction.TRY_AGAIN,
                    secondaryLabel = "Close"
                )
            }

            else -> null
        }
    }
}
