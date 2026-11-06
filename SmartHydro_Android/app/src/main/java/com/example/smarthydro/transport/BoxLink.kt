package com.example.smarthydro.transport

// The ways the phone can reach a box, best first. Bluetooth works with no network at all and the
// hotspot needs the phone on the box's own Wi-Fi, so both keep working on a farm with no signal.
// The internet goes through the cloud and is the last resort for the control screens
enum class BoxLink(val label: String) {
    BLUETOOTH("Bluetooth"),
    HOTSPOT("Hotspot"),
    INTERNET("Internet");

    // What the connection indicator says while this link is in use
    val statusText: String
        get() = "Connected via $label"
}
