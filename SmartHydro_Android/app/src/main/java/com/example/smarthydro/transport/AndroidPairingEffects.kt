package com.example.smarthydro.transport

import android.content.Context
import com.example.smarthydro.data.StructureStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

// The real effects of pairing a structure's box: asking the phone for permissions, searching, connecting and saving.
// Whatever happens is reported back to the controller by calling send, which is set once the controller exists
class AndroidPairingEffects(
    private val context: Context,
    private val scope: CoroutineScope,
    private val structureId: String,
    private val askForPermissions: (List<String>) -> Unit,
    private val onSaved: (String) -> Unit
) : PairingEffects {

    lateinit var send: (PairingEvent) -> Unit

    private val scanner = BleScanner(context.applicationContext)
    private var scanJob: Job? = null
    private var connectionJob: Job? = null

    override fun requestPermissions(permissions: List<String>) = askForPermissions(permissions)

    // The search reports what it hears until it ends by itself, which the flow is told about
    override fun startScan() {
        scanJob?.cancel()
        scanJob = scope.launch {
            scanner.scan().collect { send(PairingEvent.Scan(it)) }
            send(PairingEvent.ScanEnded)
        }
    }

    override fun stopScan() {
        scanJob?.cancel()
        scanJob = null
    }

    // The connection is kept by BoxConnections, so it carries on after this screen. Only its progress is followed here
    override fun openConnection(address: String) {
        connectionJob?.cancel()
        val box = BoxConnections.open(context, structureId, address)
        connectionJob = scope.launch { box.state.collect { send(PairingEvent.Connection(it)) } }
    }

    override fun closeConnection() {
        connectionJob?.cancel()
        connectionJob = null
        BoxConnections.close(structureId)
    }

    // Once the box is saved its progress no longer matters here, and the connection is left open for the screens to use
    override fun saveAddress(address: String) {
        connectionJob?.cancel()
        connectionJob = null
        scope.launch {
            StructureStore.setBoxBluetoothAddress(structureId, address)
            onSaved(address)
        }
    }
}
