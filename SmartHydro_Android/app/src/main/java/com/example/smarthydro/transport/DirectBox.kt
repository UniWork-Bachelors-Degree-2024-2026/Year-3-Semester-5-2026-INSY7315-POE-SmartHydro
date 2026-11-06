package com.example.smarthydro.transport

// When a screen talks to the box itself instead of going through the cloud. A person who is not signed in has nothing
// else to use, so they always do. A signed-in person uses the cloud until a direct link to the box is working, then
// the box itself, which is quicker when the cloud is slow
object DirectBox {

    // Whether a screen opens a session to the box. A signed-in person only does once they have paired the box over
    // Bluetooth, so the cloud screens stay as they were for everyone who has not
    fun sessionWanted(signedIn: Boolean, hasBoxMac: Boolean, hasSavedBluetooth: Boolean): Boolean =
        hasBoxMac && (!signedIn || hasSavedBluetooth)

    // Whether readings and switches go to the box. Offline always does. A signed-in person does while Bluetooth or the
    // hotspot is delivering readings, and goes back to the cloud when they stop
    fun inUse(signedIn: Boolean, link: BoxLink?, hasReading: Boolean): Boolean =
        !signedIn || (hasReading && (link == BoxLink.BLUETOOTH || link == BoxLink.HOTSPOT))

    // Whether a signed-in person is offered a way to pair the box over Bluetooth: they have a box but have not paired it
    fun pairingOffered(signedIn: Boolean, hasBoxMac: Boolean, hasSavedBluetooth: Boolean): Boolean =
        signedIn && hasBoxMac && !hasSavedBluetooth
}
