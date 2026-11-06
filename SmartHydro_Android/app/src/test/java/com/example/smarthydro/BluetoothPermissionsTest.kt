package com.example.smarthydro

import android.Manifest
import com.example.smarthydro.transport.BluetoothPermissions
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks which Bluetooth permissions are asked for on each version of Android
class BluetoothPermissionsTest {

    private val android11 = 30
    private val android12 = 31
    private val android15 = 35

    @Test
    fun android12AndNewerAskForNearbyDevices() {
        val expected = listOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)

        assertEquals(expected, BluetoothPermissions.required(android12))
        assertEquals(expected, BluetoothPermissions.required(android15))
    }

    @Test
    fun android11AndOlderAskForLocation() {
        val expected = listOf(Manifest.permission.ACCESS_FINE_LOCATION)

        assertEquals(expected, BluetoothPermissions.required(android11))
        assertEquals(expected, BluetoothPermissions.required(26))
    }

    @Test
    fun android12DoesNotAskForLocation() {
        assertFalse(BluetoothPermissions.required(android12).contains(Manifest.permission.ACCESS_FINE_LOCATION))
    }

    @Test
    fun nothingIsMissingOnceEverythingIsGranted() {
        assertTrue(BluetoothPermissions.missing(android12) { true }.isEmpty())
        assertTrue(BluetoothPermissions.missing(android11) { true }.isEmpty())
    }

    @Test
    fun everythingIsMissingWhenNothingIsGranted() {
        assertEquals(BluetoothPermissions.required(android12), BluetoothPermissions.missing(android12) { false })
        assertEquals(BluetoothPermissions.required(android11), BluetoothPermissions.missing(android11) { false })
    }

    @Test
    fun onlyThePermissionNotYetGrantedIsMissing() {
        val missing = BluetoothPermissions.missing(android12) { it == Manifest.permission.BLUETOOTH_SCAN }

        assertEquals(listOf(Manifest.permission.BLUETOOTH_CONNECT), missing)
    }

    @Test
    fun theReasonMatchesWhatThePhoneWillAskFor() {
        assertTrue(BluetoothPermissions.reason(android12).contains("Nearby devices"))
        assertTrue(BluetoothPermissions.reason(android11).contains("location"))
        assertTrue(BluetoothPermissions.reason(android11).contains("doesn't track"))
    }
}
