package com.example.smarthydro.transport

import android.bluetooth.le.ScanCallback

// Why a search for boxes could not run or stopped, with the words the person is told
enum class ScanProblem(val message: String) {
    PERMISSION_MISSING("The app isn't allowed to use Bluetooth yet."),
    NO_BLUETOOTH("This phone doesn't have Bluetooth."),
    BLUETOOTH_OFF("Bluetooth is turned off. Turn it on to find your box."),
    BUSY("Bluetooth is busy. Try again in a moment."),
    FAILED("Couldn't search for boxes. Please try again.");

    companion object {

        // What stops a search before it starts, or null when it can go ahead
        fun beforeScanning(permissionsMissing: Boolean, hasBluetooth: Boolean, bluetoothOn: Boolean): ScanProblem? = when {
            permissionsMissing -> PERMISSION_MISSING
            !hasBluetooth -> NO_BLUETOOTH
            !bluetoothOn -> BLUETOOTH_OFF
            else -> null
        }

        // The problem behind the error code Android gives when a search fails to start
        fun fromErrorCode(errorCode: Int): ScanProblem = when (errorCode) {
            ScanCallback.SCAN_FAILED_ALREADY_STARTED,
            ScanCallback.SCAN_FAILED_OUT_OF_HARDWARE_RESOURCES,
            ScanCallback.SCAN_FAILED_SCANNING_TOO_FREQUENTLY -> BUSY
            else -> FAILED
        }
    }
}
