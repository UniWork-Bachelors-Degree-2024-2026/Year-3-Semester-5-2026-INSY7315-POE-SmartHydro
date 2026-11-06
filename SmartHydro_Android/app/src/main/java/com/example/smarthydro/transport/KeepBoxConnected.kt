package com.example.smarthydro.transport

import android.content.pm.PackageManager
import android.os.Build
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.platform.LocalContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive

// Keeps the Bluetooth connection to a structure's saved box open while a screen is showing, opening it when the app
// has just started and again after it drops, with a longer wait between attempts the longer the box stays away.
// ReconnectPolicy decides each time, so this only does what it says. enabled is false for a structure that has no box
@Composable
fun KeepBoxConnected(structureId: String, savedAddress: String?, enabled: Boolean) {
    val context = LocalContext.current

    LaunchedEffect(structureId, savedAddress, enabled) {
        if (!enabled || savedAddress == null) return@LaunchedEffect

        var attempts = 0
        while (isActive) {
            val permissionsMissing = BluetoothPermissions.missing(Build.VERSION.SDK_INT) {
                context.checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED
            }.isNotEmpty()
            val state = BoxConnections.registry.get(structureId)?.state?.value

            when (ReconnectPolicy.decide(hasSavedBox = true, permissionsMissing = permissionsMissing, state = state)) {
                ReconnectDecision.CONNECT -> {
                    BoxLog.i("reconnect", "opening $savedAddress for structure $structureId, attempt ${attempts + 1}, was $state")
                    BoxConnections.open(context, structureId, savedAddress)
                    attempts++
                    delay(ReconnectPolicy.backoffMs(attempts))
                }

                ReconnectDecision.WAIT -> {
                    // A connection that is working starts the count again, so the next drop is tried at once
                    if (state is ConnectionState.Ready) attempts = 0
                    delay(ReconnectPolicy.WAIT_MS)
                }

                ReconnectDecision.STOP -> {
                    BoxLog.i("reconnect", "stopped for structure $structureId: state=$state, permissionsMissing=$permissionsMissing")
                    return@LaunchedEffect
                }
            }
        }
    }
}
