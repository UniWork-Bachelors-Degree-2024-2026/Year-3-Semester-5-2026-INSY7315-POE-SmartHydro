package com.example.smarthydro

import android.bluetooth.le.ScanCallback
import com.example.smarthydro.transport.ScanProblem
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks what stops a search for boxes and what the person is told about it
class ScanProblemTest {

    // ---- before the search starts ----

    @Test
    fun searchGoesAheadWhenEverythingIsReady() {
        assertNull(ScanProblem.beforeScanning(permissionsMissing = false, hasBluetooth = true, bluetoothOn = true))
    }

    @Test
    fun missingPermissionComesFirst() {
        assertEquals(
            ScanProblem.PERMISSION_MISSING,
            ScanProblem.beforeScanning(permissionsMissing = true, hasBluetooth = false, bluetoothOn = false)
        )
    }

    @Test
    fun phoneWithoutBluetoothIsToldSo() {
        assertEquals(
            ScanProblem.NO_BLUETOOTH,
            ScanProblem.beforeScanning(permissionsMissing = false, hasBluetooth = false, bluetoothOn = false)
        )
    }

    @Test
    fun bluetoothSwitchedOffIsToldSo() {
        assertEquals(
            ScanProblem.BLUETOOTH_OFF,
            ScanProblem.beforeScanning(permissionsMissing = false, hasBluetooth = true, bluetoothOn = false)
        )
    }

    // ---- when the search fails to start ----

    @Test
    fun busyErrorsAreGroupedTogether() {
        assertEquals(ScanProblem.BUSY, ScanProblem.fromErrorCode(ScanCallback.SCAN_FAILED_ALREADY_STARTED))
        assertEquals(ScanProblem.BUSY, ScanProblem.fromErrorCode(ScanCallback.SCAN_FAILED_OUT_OF_HARDWARE_RESOURCES))
        assertEquals(ScanProblem.BUSY, ScanProblem.fromErrorCode(ScanCallback.SCAN_FAILED_SCANNING_TOO_FREQUENTLY))
    }

    @Test
    fun otherErrorsAreAGeneralFailure() {
        assertEquals(ScanProblem.FAILED, ScanProblem.fromErrorCode(ScanCallback.SCAN_FAILED_INTERNAL_ERROR))
        assertEquals(ScanProblem.FAILED, ScanProblem.fromErrorCode(ScanCallback.SCAN_FAILED_APPLICATION_REGISTRATION_FAILED))
        assertEquals(ScanProblem.FAILED, ScanProblem.fromErrorCode(ScanCallback.SCAN_FAILED_FEATURE_UNSUPPORTED))
        assertEquals(ScanProblem.FAILED, ScanProblem.fromErrorCode(999))
    }

    // ---- the words ----

    @Test
    fun everyProblemHasItsOwnWords() {
        val messages = ScanProblem.entries.map { it.message }

        assertTrue(messages.all { it.isNotBlank() })
        assertEquals(messages.size, messages.toSet().size)
    }
}
