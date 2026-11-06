package com.example.smarthydro.transport

import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.flowOf

// A BoxSession together with the scope it runs in, so closing it stops everything it started
class OpenBoxSession(val session: BoxSession, private val scope: CoroutineScope) {

    fun close() {
        scope.cancel()
    }

    companion object {

        // A session that reaches the box over its hotspot, and over Bluetooth too when a connection to it is open.
        // The internet is not offered yet
        fun create(boxIpAddress: String, deviceMac: String, bluetooth: BleBox?): OpenBoxSession {
            val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
            val transports = buildMap<BoxLink, BoxTransport> {
                put(BoxLink.HOTSPOT, HotspotTransport(boxIpAddress, deviceMac))
                if (bluetooth != null) put(BoxLink.BLUETOOTH, BleTransport.forBox(bluetooth, deviceMac))
            }
            val session = BoxSession(
                scope = scope,
                transports = transports,
                bluetoothState = bluetooth?.state ?: flowOf(ConnectionState.Idle),
                internetAvailable = flowOf(false)
            )
            return OpenBoxSession(session, scope)
        }
    }
}

// The session for a box while a screen is showing, or null when the screen has no box to reach. It is closed when the
// screen goes away, and made again when any of the keys change, which is how a pull to refresh polls the box again.
// It also changes when a Bluetooth connection to this box is opened or replaced, which is how Bluetooth gets added
@Composable
fun rememberBoxSession(key: Any, refreshKey: Int, boxIpAddress: String, deviceMac: String, enabled: Boolean): BoxSession? {
    val connections by BoxConnections.registry.all.collectAsState()
    val bluetooth = connections[key.toString()]?.connection

    val open = remember(key, refreshKey, boxIpAddress, deviceMac, enabled, bluetooth) {
        if (enabled) OpenBoxSession.create(boxIpAddress, deviceMac, bluetooth) else null
    }
    DisposableEffect(open) { onDispose { open?.close() } }
    return open?.session
}
