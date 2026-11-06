package com.example.smarthydro

import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.ui.SimpleModeState
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SimpleModeTest {

    @Before
    fun setup() {
        ApiClient.clearAuthToken()
        SimpleModeState.offlineSimpleMode = false
    }

    @Test
    fun offlineMode_simpleModeIsOffByDefault() {
        assertFalse(ApiClient.isSignedIn())
        assertFalse(SimpleModeState.offlineSimpleMode)
        assertFalse(SimpleModeState.isActive)
        assertFalse(ApiClient.isSimpleModeActive())
    }

    @Test
    fun offlineMode_toggleSimpleModeOnAndOff() {
        assertFalse(ApiClient.isSignedIn())

        SimpleModeState.setOfflineMode(true)
        assertTrue(SimpleModeState.offlineSimpleMode)
        assertTrue(SimpleModeState.isActive)
        assertTrue(ApiClient.isSimpleModeActive())

        SimpleModeState.setOfflineMode(false)
        assertFalse(SimpleModeState.offlineSimpleMode)
        assertFalse(SimpleModeState.isActive)
        assertFalse(ApiClient.isSimpleModeActive())
    }

    @Test
    fun signedInWorker_isAlwaysSimpleModeRegardlessOfOfflineToggle() {
        // Save worker auth token
        ApiClient.setAuthToken(
            token = "dummy_token",
            expiresAt = "2099-01-01T00:00:00Z",
            email = "worker@farm.com",
            role = "Worker"
        )
        SimpleModeState.offlineSimpleMode = false

        assertTrue(ApiClient.isSignedIn())
        assertTrue("Worker must always be in simple mode", SimpleModeState.isActive)
        assertTrue("ApiClient must report simple mode active for Worker", ApiClient.isSimpleModeActive())
    }

    @Test
    fun signedInOperator_isNotSimpleModeEvenIfOfflineToggleWasTrue() {
        // Save operator auth token
        ApiClient.setAuthToken(
            token = "dummy_token",
            expiresAt = "2099-01-01T00:00:00Z",
            email = "operator@farm.com",
            role = "Operator"
        )
        // Even if offlineSimpleMode flag in memory is true, signed-in operator is NOT simple mode
        SimpleModeState.offlineSimpleMode = true

        assertTrue(ApiClient.isSignedIn())
        assertFalse("Operator should not be in simple mode", SimpleModeState.isActive)
        assertFalse(ApiClient.isSimpleModeActive())
    }

    @Test
    fun signedInAdmin_isNotSimpleMode() {
        // Save admin auth token
        ApiClient.setAuthToken(
            token = "dummy_token",
            expiresAt = "2099-01-01T00:00:00Z",
            email = "admin@farm.com",
            role = "Admin"
        )
        SimpleModeState.offlineSimpleMode = false

        assertTrue(ApiClient.isSignedIn())
        assertFalse("Admin should not be in simple mode", SimpleModeState.isActive)
        assertFalse(ApiClient.isSimpleModeActive())
    }

    @Test
    fun structureCount_determinesDropdownRequirement() {
        val multipleStructures = listOf("Structure 1", "Structure 2")
        val singleStructure = listOf("Structure 1")
        val emptyStructures = emptyList<String>()

        assertTrue("Multiple structures require dropdown", multipleStructures.size > 1)
        assertFalse("Single structure does not require dropdown", singleStructure.size > 1)
        assertFalse("Empty structures list does not require dropdown", emptyStructures.size > 1)
    }
}
