package com.example.smarthydro.transport

import android.Manifest
import android.os.Build

// Which Bluetooth permissions the app has to ask the person for while it runs. Android 12 and newer ask for
// scanning and connecting as "Nearby devices", and Android 11 and older ask for location because a scan could be
// used to find out where the person is (Android Developers, 2026a)
object BluetoothPermissions {

    // The permissions to request on a phone running this Android version (the API level of Build.VERSION.SDK_INT)
    fun required(sdkInt: Int): List<String> =
        if (sdkInt >= Build.VERSION_CODES.S) {
            listOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            listOf(Manifest.permission.ACCESS_FINE_LOCATION)
        }

    // The permissions still to ask for, going by what isGranted says about each one
    fun missing(sdkInt: Int, isGranted: (String) -> Boolean): List<String> =
        required(sdkInt).filterNot(isGranted)

    // Says why the phone is about to ask for the permissions
    fun reason(sdkInt: Int): String =
        if (sdkInt >= Build.VERSION_CODES.S) {
            "To find and control your box over Bluetooth, the app needs to use Nearby devices."
        } else {
            "To find your box over Bluetooth, this version of Android needs the app to use your location. " +
                "The app doesn't track where you are."
        }
}

//Reference Lists:

//Android Developers, 2026a. Bluetooth permissions. [online] Available at: <https://developer.android.com/develop/connectivity/bluetooth/bt-permissions> [Accessed 29 October 2026].

//Android Developers, 2026b. Set up Bluetooth. [online] Available at: <https://developer.android.com/develop/connectivity/bluetooth/setup> [Accessed 29 October 2026].
