package com.example.smarthydro.transport

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import java.util.concurrent.atomic.AtomicReference

// Decides from what Android says about a network whether it really has internet. A network that only claims it
// should be able to reach the internet is not enough, because the box's own hotspot claims that too, so Android
// must also have checked that it works (Android Open Source Project, 2026)
object InternetStatus {

    fun isOnline(claimsInternet: Boolean, validated: Boolean): Boolean = claimsInternet && validated
}

// Tells whether the phone has working internet right now, as a flow that changes when it does. It follows the
// network the phone uses by default, and reports no internet until Android says otherwise
class InternetMonitor(private val context: Context) {

    fun status(): Flow<Boolean> = callbackFlow {
        val manager = context.getSystemService(ConnectivityManager::class.java)
        if (manager == null) {
            trySend(false)
            close()
            return@callbackFlow
        }

        // The default network can change to another one, and the old one is then reported lost, which must not
        // be taken for the new one being lost
        val inUse = AtomicReference<Network?>(null)

        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onCapabilitiesChanged(network: Network, capabilities: NetworkCapabilities) {
                inUse.set(network)
                trySend(
                    InternetStatus.isOnline(
                        claimsInternet = capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET),
                        validated = capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
                    )
                )
            }

            override fun onLost(network: Network) {
                if (inUse.compareAndSet(network, null)) trySend(false)
            }
        }

        trySend(false)
        try {
            manager.registerDefaultNetworkCallback(callback)
        } catch (e: RuntimeException) {
            // Android allows only so many callbacks at once, or the permission is missing
            close()
            return@callbackFlow
        }

        awaitClose { manager.unregisterNetworkCallback(callback) }
    }.distinctUntilChanged()
}

//Reference Lists:

//Android Open Source Project, 2026. NetworkCapabilities.java. [online] Available at: <https://android.googlesource.com/platform/packages/modules/Connectivity/+/refs/heads/main/framework/src/android/net/NetworkCapabilities.java> [Accessed 29 October 2026].
