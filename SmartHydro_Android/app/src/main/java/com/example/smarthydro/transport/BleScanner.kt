package com.example.smarthydro.transport

import android.annotation.SuppressLint
import android.bluetooth.BluetoothManager
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.ParcelUuid
import android.os.SystemClock
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.launch
import java.util.UUID

// What a search for boxes reports as it goes
sealed interface ScanUpdate {

    // The boxes heard so far, strongest signal first
    data class Boxes(val boxes: List<NearbyBox>) : ScanUpdate

    // The search could not run or stopped early
    data class Problem(val problem: ScanProblem) : ScanUpdate
}

// Searches for boxes over Bluetooth. A search stops by itself after a time limit and never repeats, because a
// scan left running drains the battery (Android Developers, 2026). The permissions have to be granted first,
// see BluetoothPermissions
class BleScanner(private val context: Context) {

    companion object {
        const val DEFAULT_TIME_LIMIT_MS = 15_000L
        private const val REFRESH_MS = 500L
    }

    // Searches until the time limit or until the collector stops collecting, whichever comes first. Only boxes
    // advertising the SmartHydro service are looked for, and the flow ends once the search is over
    @SuppressLint("MissingPermission")
    fun scan(timeLimitMs: Long = DEFAULT_TIME_LIMIT_MS): Flow<ScanUpdate> = callbackFlow {
        val permissionsMissing = BluetoothPermissions.missing(Build.VERSION.SDK_INT) {
            context.checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED
        }.isNotEmpty()
        val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter

        // The scanner is only there while Bluetooth is on (Android Developers, 2026)
        val scanner = if (permissionsMissing || adapter == null || !adapter.isEnabled) null else adapter.bluetoothLeScanner
        val problem = ScanProblem.beforeScanning(permissionsMissing, adapter != null, scanner != null)
        if (problem != null || scanner == null) {
            BoxLog.w("scan", "cannot start: ${problem ?: ScanProblem.FAILED}")
            trySend(ScanUpdate.Problem(problem ?: ScanProblem.FAILED))
            close()
            return@callbackFlow
        }

        BoxLog.i("scan", "start, ${timeLimitMs / 1000} seconds, looking for the SmartHydro service")
        val heard = NearbyBoxList()

        // Each device is written to the log once, so the list of what was heard can be read back without a line per advertisement
        val logged = mutableSetOf<String>()

        val callback = object : ScanCallback() {
            override fun onScanResult(callbackType: Int, result: ScanResult) = record(result)

            override fun onBatchScanResults(results: MutableList<ScanResult>) {
                results.forEach { record(it) }
            }

            override fun onScanFailed(errorCode: Int) {
                BoxLog.e("scan", "Android reported the scan failed, error code $errorCode")
                trySend(ScanUpdate.Problem(ScanProblem.fromErrorCode(errorCode)))
                close()
            }

            // The name is the one the box advertises, and anything not named like a box is left out
            private fun record(result: ScanResult) {
                val name = result.scanRecord?.deviceName
                val box = BoxAdvertisement.parse(name, result.device.address, result.rssi, SystemClock.elapsedRealtime())

                if (logged.add(result.device.address)) {
                    if (box != null) {
                        BoxLog.i("scan", "heard ${box.name} at ${box.address}, signal ${box.rssi}")
                    } else {
                        // It advertises the SmartHydro service but is not named like a box, so the name is what to look at
                        BoxLog.w("scan", "heard ${result.device.address} with the SmartHydro service but the name '$name' is not a box's")
                    }
                }
                box?.let { heard.onResult(it) }
            }
        }

        val filter = ScanFilter.Builder()
            .setServiceUuid(ParcelUuid(UUID.fromString(BoxAdvertisement.SERVICE_UUID)))
            .build()
        val settings = ScanSettings.Builder()
            .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
            .build()

        try {
            scanner.startScan(listOf(filter), settings, callback)
        } catch (e: Exception) {
            BoxLog.e("scan", "starting the scan threw ${e.javaClass.simpleName}: ${e.message}")
            trySend(ScanUpdate.Problem(ScanProblem.FAILED))
            close()
            return@callbackFlow
        }

        // Reports what has been heard twice a second, dropping boxes that have gone quiet, then ends the search
        val reporter = launch {
            val startedAt = SystemClock.elapsedRealtime()
            while (SystemClock.elapsedRealtime() - startedAt < timeLimitMs) {
                delay(REFRESH_MS)
                trySend(ScanUpdate.Boxes(heard.snapshot(SystemClock.elapsedRealtime())))
            }
            close()
        }

        awaitClose {
            BoxLog.i("scan", "stopped, ${logged.size} device(s) heard")
            reporter.cancel()
            try {
                scanner.stopScan(callback)
            } catch (e: Exception) {
                // Bluetooth was switched off while searching, so there is nothing left to stop
            }
        }
    }
}

//Reference Lists:

//Android Developers, 2026. Find BLE devices. [online] Available at: <https://developer.android.com/develop/connectivity/bluetooth/ble/find-ble-devices> [Accessed 29 October 2026].
