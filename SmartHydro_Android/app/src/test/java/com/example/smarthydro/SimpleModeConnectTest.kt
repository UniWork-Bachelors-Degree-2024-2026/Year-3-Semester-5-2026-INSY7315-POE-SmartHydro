package com.example.smarthydro

import com.example.smarthydro.ui.SimpleModeConnect
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks when Simple Mode offers to connect the box
class SimpleModeConnectTest {

    @Test
    fun offeredWhenAFarmersBoxHasBeenCheckedAndIsNotAnswering() {
        assertTrue(SimpleModeConnect.shouldOffer(isFarmerBox = true, boxChecked = true, hasReading = false))
    }

    @Test
    fun notOfferedWhileTheBoxIsStillBeingChecked() {
        assertFalse(SimpleModeConnect.shouldOffer(isFarmerBox = true, boxChecked = false, hasReading = false))
    }

    @Test
    fun notOfferedWhenTheBoxIsAnswering() {
        assertFalse(SimpleModeConnect.shouldOffer(isFarmerBox = true, boxChecked = true, hasReading = true))
    }

    @Test
    fun notOfferedToSomeoneUsingTheCloud() {
        assertFalse(SimpleModeConnect.shouldOffer(isFarmerBox = false, boxChecked = true, hasReading = false))
        assertFalse(SimpleModeConnect.shouldOffer(isFarmerBox = false, boxChecked = false, hasReading = false))
    }

    @Test
    fun wordsAreShortAndPlain() {
        assertEquals("Your box isn't connected.", SimpleModeConnect.MESSAGE)
        assertEquals("Connect your box", SimpleModeConnect.BUTTON)
    }
}
