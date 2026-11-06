package com.example.smarthydro

import com.example.smarthydro.transport.BoxLink
import com.example.smarthydro.transport.HotspotTransport
import org.junit.Assert.assertEquals
import org.junit.Test

// Checks the ways of reaching a box: the order they are tried in and what the indicator says
class BoxLinkTest {

    @Test
    fun linksAreListedBestFirst() {
        assertEquals(
            listOf(BoxLink.BLUETOOTH, BoxLink.HOTSPOT, BoxLink.INTERNET),
            BoxLink.entries.toList()
        )
    }

    @Test
    fun indicatorNamesTheLinkInUse() {
        assertEquals("Connected via Bluetooth", BoxLink.BLUETOOTH.statusText)
        assertEquals("Connected via Hotspot", BoxLink.HOTSPOT.statusText)
        assertEquals("Connected via Internet", BoxLink.INTERNET.statusText)
    }

    @Test
    fun hotspotTransportReportsTheHotspotLink() {
        assertEquals(BoxLink.HOTSPOT, HotspotTransport("192.168.4.1", "AA:BB:CC:DD:EE:FF").link)
    }
}
