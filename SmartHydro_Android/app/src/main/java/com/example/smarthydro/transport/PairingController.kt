package com.example.smarthydro.transport

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

// The things pairing has to do outside the flow. The real ones talk to Android, and the tests use fake ones
interface PairingEffects {
    fun requestPermissions(permissions: List<String>)
    fun startScan()
    fun stopScan()
    fun openConnection(address: String)
    fun closeConnection()
    fun saveAddress(address: String)
}

// Runs a PairingFlow: every event goes through the flow, and the action it hands back is done through the effects.
// Events come from the screen and from Bluetooth on different threads, so they take turns
class PairingController(private val flow: PairingFlow, private val effects: PairingEffects) {

    private val current = MutableStateFlow<PairingState>(PairingState.Idle)

    // Where pairing has got to
    val state: StateFlow<PairingState> = current

    @Synchronized
    fun send(event: PairingEvent) {
        val before = current.value
        val transition = flow.reduce(before, event)
        current.value = transition.state

        // Only a change of step or something to do is written, since the search reports twice a second
        if (transition.state::class != before::class || transition.action != null) {
            BoxLog.i("pair", "${describe(before)} -> ${describe(transition.state)}${transition.action?.let { ", then $it" }.orEmpty()}")
        }

        when (val action = transition.action) {
            is PairingAction.RequestPermissions -> effects.requestPermissions(action.permissions)
            is PairingAction.StartScan -> effects.startScan()
            is PairingAction.StopScan -> effects.stopScan()
            is PairingAction.OpenConnection -> effects.openConnection(action.address)
            is PairingAction.CloseConnection -> effects.closeConnection()
            is PairingAction.SaveAddress -> effects.saveAddress(action.address)
            null -> Unit
        }
    }

    // A step in a few words, without the list of boxes the search has heard
    private fun describe(state: PairingState): String = when (state) {
        is PairingState.Failed -> "Failed(${state.failure.message})"
        is PairingState.Connecting -> "Connecting(${state.box.name}, ${state.connection})"
        is PairingState.Confirming -> "Confirming(${state.box.name})"
        is PairingState.Done -> "Done(${state.box.name})"
        is PairingState.Searching -> "Searching(${state.boxes.size} found${if (state.finished) ", finished" else ""})"
        else -> state::class.simpleName ?: "?"
    }
}
