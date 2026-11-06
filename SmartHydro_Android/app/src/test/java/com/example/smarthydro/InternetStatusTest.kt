package com.example.smarthydro

import com.example.smarthydro.transport.InternetStatus
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks when a network counts as having internet
class InternetStatusTest {

    @Test
    fun networkThatClaimsInternetAndWasCheckedCounts() {
        assertTrue(InternetStatus.isOnline(claimsInternet = true, validated = true))
    }

    @Test
    fun networkThatOnlyClaimsInternetDoesNotCount() {
        // The box's own hotspot is like this: it says it can reach the internet but it cannot
        assertFalse(InternetStatus.isOnline(claimsInternet = true, validated = false))
    }

    @Test
    fun networkWithoutInternetNeverCounts() {
        assertFalse(InternetStatus.isOnline(claimsInternet = false, validated = true))
        assertFalse(InternetStatus.isOnline(claimsInternet = false, validated = false))
    }
}
