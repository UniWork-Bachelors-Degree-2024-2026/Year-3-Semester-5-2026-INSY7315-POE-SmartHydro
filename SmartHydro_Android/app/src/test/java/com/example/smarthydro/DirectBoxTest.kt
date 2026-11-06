package com.example.smarthydro

import com.example.smarthydro.transport.BoxLink
import com.example.smarthydro.transport.DirectBox
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks when a signed-in person talks to the box directly, and that nothing changes for someone who is not signed in
class DirectBoxTest {

    // ---- opening a session to the box ----

    @Test
    fun offlinePersonWithABoxAlwaysOpensASession() {
        assertTrue(DirectBox.sessionWanted(signedIn = false, hasBoxMac = true, hasSavedBluetooth = false))
        assertTrue(DirectBox.sessionWanted(signedIn = false, hasBoxMac = true, hasSavedBluetooth = true))
    }

    @Test
    fun signedInPersonOpensASessionOnlyAfterPairingOverBluetooth() {
        assertFalse(DirectBox.sessionWanted(signedIn = true, hasBoxMac = true, hasSavedBluetooth = false))
        assertTrue(DirectBox.sessionWanted(signedIn = true, hasBoxMac = true, hasSavedBluetooth = true))
    }

    @Test
    fun noSessionWithoutABox() {
        assertFalse(DirectBox.sessionWanted(signedIn = false, hasBoxMac = false, hasSavedBluetooth = false))
        assertFalse(DirectBox.sessionWanted(signedIn = true, hasBoxMac = false, hasSavedBluetooth = true))
    }

    // ---- using the box instead of the cloud ----

    @Test
    fun offlinePersonAlwaysUsesTheBox() {
        assertTrue(DirectBox.inUse(signedIn = false, link = null, hasReading = false))
        assertTrue(DirectBox.inUse(signedIn = false, link = BoxLink.INTERNET, hasReading = false))
    }

    @Test
    fun signedInPersonUsesTheBoxOverBluetoothOrHotspotOnceItReads() {
        assertTrue(DirectBox.inUse(signedIn = true, link = BoxLink.BLUETOOTH, hasReading = true))
        assertTrue(DirectBox.inUse(signedIn = true, link = BoxLink.HOTSPOT, hasReading = true))
    }

    @Test
    fun signedInPersonStaysOnTheCloudUntilTheBoxIsReading() {
        assertFalse(DirectBox.inUse(signedIn = true, link = BoxLink.BLUETOOTH, hasReading = false))
        assertFalse(DirectBox.inUse(signedIn = true, link = null, hasReading = false))
    }

    @Test
    fun signedInPersonOnTheInternetLinkStaysOnTheCloud() {
        assertFalse(DirectBox.inUse(signedIn = true, link = BoxLink.INTERNET, hasReading = true))
    }

    // ---- offering to pair ----

    @Test
    fun signedInPersonIsOfferedPairingOnlyForAnUnpairedBox() {
        assertTrue(DirectBox.pairingOffered(signedIn = true, hasBoxMac = true, hasSavedBluetooth = false))
        assertFalse(DirectBox.pairingOffered(signedIn = true, hasBoxMac = true, hasSavedBluetooth = true))
        assertFalse(DirectBox.pairingOffered(signedIn = true, hasBoxMac = false, hasSavedBluetooth = false))
    }

    @Test
    fun offlinePersonKeepsTheirOwnPairingPrompt() {
        assertFalse(DirectBox.pairingOffered(signedIn = false, hasBoxMac = true, hasSavedBluetooth = false))
    }
}
