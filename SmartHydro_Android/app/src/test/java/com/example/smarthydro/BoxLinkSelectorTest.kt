package com.example.smarthydro

import com.example.smarthydro.transport.BoxLink
import com.example.smarthydro.transport.BoxLinkSelector
import com.example.smarthydro.transport.ConnectionState
import com.example.smarthydro.transport.DisconnectReason
import com.example.smarthydro.transport.LinkStatus
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks which way of reaching a box a screen uses, and that it does not jump about
class BoxLinkSelectorTest {

    private val none = LinkStatus(bluetoothReady = false, hotspotReachable = false, internetAvailable = false)
    private val all = LinkStatus(bluetoothReady = true, hotspotReachable = true, internetAvailable = true)
    private val hotspotOnly = none.copy(hotspotReachable = true)
    private val internetOnly = none.copy(internetAvailable = true)
    private val bluetoothOnly = none.copy(bluetoothReady = true)
    private val hotspotAndInternet = LinkStatus(bluetoothReady = false, hotspotReachable = true, internetAvailable = true)

    // ---- the best link ----

    @Test
    fun bestLinkFollowsTheOrderBluetoothHotspotInternet() {
        assertEquals(BoxLink.BLUETOOTH, all.best())
        assertEquals(BoxLink.HOTSPOT, hotspotAndInternet.best())
        assertEquals(BoxLink.INTERNET, internetOnly.best())
        assertNull(none.best())
    }

    @Test
    fun linkWorksOnlyWhenItsOwnFlagIsOn() {
        assertTrue(bluetoothOnly.has(BoxLink.BLUETOOTH))
        assertFalse(bluetoothOnly.has(BoxLink.HOTSPOT))
        assertFalse(bluetoothOnly.has(BoxLink.INTERNET))
    }

    @Test
    fun bluetoothOnlyCountsOnceTheConnectionIsReady() {
        val mtu = 247

        assertTrue(LinkStatus.from(ConnectionState.Ready(mtu), false, false).bluetoothReady)
        assertFalse(LinkStatus.from(ConnectionState.Connecting, false, false).bluetoothReady)
        assertFalse(LinkStatus.from(ConnectionState.Pairing(mtu), false, false).bluetoothReady)
        assertFalse(LinkStatus.from(ConnectionState.Idle, false, false).bluetoothReady)
        assertFalse(LinkStatus.from(ConnectionState.Disconnected(DisconnectReason.LOST_SIGNAL), false, false).bluetoothReady)
    }

    // ---- choosing ----

    @Test
    fun nothingWorkingMeansNoLink() {
        assertNull(BoxLinkSelector().select(none, 0L))
    }

    @Test
    fun firstChoiceIsTheBestLinkAtOnce() {
        assertEquals(BoxLink.BLUETOOTH, BoxLinkSelector().select(all, 0L))
        assertEquals(BoxLink.HOTSPOT, BoxLinkSelector().select(hotspotAndInternet, 0L))
        assertEquals(BoxLink.INTERNET, BoxLinkSelector().select(internetOnly, 0L))
    }

    // ---- losing a link ----

    @Test
    fun lostLinkIsReplacedAtOnceByTheNextBest() {
        val selector = BoxLinkSelector()
        selector.select(all, 0L)

        assertEquals(BoxLink.HOTSPOT, selector.select(all.copy(bluetoothReady = false), 1L))
        assertEquals(BoxLink.INTERNET, selector.select(internetOnly, 2L))
    }

    @Test
    fun losingEveryLinkMeansNoLink() {
        val selector = BoxLinkSelector()
        selector.select(hotspotOnly, 0L)

        assertNull(selector.select(none, 1L))
        assertNull(selector.current)
    }

    @Test
    fun linkComesBackAfterNothingWorkedAtOnce() {
        val selector = BoxLinkSelector()
        selector.select(none, 0L)

        assertEquals(BoxLink.BLUETOOTH, selector.select(bluetoothOnly, 1L))
    }

    // ---- a better link turning up ----

    @Test
    fun betterLinkIsOnlyTakenOnceItHasStayedUp() {
        val selector = BoxLinkSelector(upgradeAfterMs = 5_000L)
        selector.select(hotspotOnly, 0L)

        assertEquals(BoxLink.HOTSPOT, selector.select(all, 1_000L))
        assertEquals(BoxLink.HOTSPOT, selector.select(all, 5_999L))
        assertEquals(BoxLink.BLUETOOTH, selector.select(all, 6_000L))
    }

    @Test
    fun betterLinkThatBlinksOffStartsTheWaitAgain() {
        val selector = BoxLinkSelector(upgradeAfterMs = 5_000L)
        selector.select(hotspotOnly, 0L)
        selector.select(all, 1_000L)

        selector.select(hotspotOnly, 4_000L)
        assertEquals(BoxLink.HOTSPOT, selector.select(all, 5_000L))
        assertEquals(BoxLink.HOTSPOT, selector.select(all, 9_999L))
        assertEquals(BoxLink.BLUETOOTH, selector.select(all, 10_000L))
    }

    @Test
    fun linkInUseIsKeptWhileAWorseOneAppears() {
        val selector = BoxLinkSelector()
        selector.select(bluetoothOnly, 0L)

        assertEquals(BoxLink.BLUETOOTH, selector.select(all, 100_000L))
    }

    @Test
    fun betterLinkIsTakenOnlyAfterTheWaitEvenWhenTheOneInUseIsStillFine() {
        val selector = BoxLinkSelector(upgradeAfterMs = 5_000L)
        selector.select(internetOnly, 0L)

        assertEquals(BoxLink.INTERNET, selector.select(hotspotAndInternet, 1_000L))
        assertEquals(BoxLink.HOTSPOT, selector.select(hotspotAndInternet, 6_000L))
    }

    @Test
    fun linkLostWhileWaitingToUpgradeFallsBackWithoutWaiting() {
        val selector = BoxLinkSelector(upgradeAfterMs = 5_000L)
        selector.select(hotspotOnly, 0L)
        selector.select(all, 1_000L)

        assertEquals(BoxLink.INTERNET, selector.select(internetOnly, 2_000L))
    }

    // ---- the words ----

    @Test
    fun lineUnderTheNameSaysWhatToKeepDoingForTheLink() {
        assertEquals(
            "Connected via Hotspot. Switches work while your phone stays on its Wi-Fi.",
            BoxLinkSelector.explanation(BoxLink.HOTSPOT)
        )
        assertEquals(
            "Connected via Bluetooth. Switches work while your phone is near the box.",
            BoxLinkSelector.explanation(BoxLink.BLUETOOTH)
        )
        assertEquals("Connected via Internet", BoxLinkSelector.explanation(BoxLink.INTERNET))
        assertEquals("Not connected to the box", BoxLinkSelector.explanation(null))
    }

    @Test
    fun indicatorNamesTheLinkOrSaysNotConnected() {
        assertEquals("Connected via Bluetooth", BoxLinkSelector.statusText(BoxLink.BLUETOOTH))
        assertEquals("Connected via Hotspot", BoxLinkSelector.statusText(BoxLink.HOTSPOT))
        assertEquals("Connected via Internet", BoxLinkSelector.statusText(BoxLink.INTERNET))
        assertEquals("Not connected to the box", BoxLinkSelector.statusText(null))
    }
}
