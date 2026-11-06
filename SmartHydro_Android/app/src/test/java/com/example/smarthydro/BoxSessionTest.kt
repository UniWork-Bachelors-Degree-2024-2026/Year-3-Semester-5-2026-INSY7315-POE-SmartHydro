package com.example.smarthydro

import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.transport.BoxLink
import com.example.smarthydro.transport.BoxLinkSelector
import com.example.smarthydro.transport.BoxMode
import com.example.smarthydro.transport.BoxModeInfo
import com.example.smarthydro.transport.BoxSession
import com.example.smarthydro.transport.BoxSessionState
import com.example.smarthydro.transport.BoxTransport
import com.example.smarthydro.transport.ConnectionState
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks how a session picks the link to a box from what each link is delivering, and hands over its reading and transport
@OptIn(ExperimentalCoroutinesApi::class)
class BoxSessionTest {

    private val mtu = 247

    private fun reading(ph: Double) = BoxTelemetryDto(
        ph = ph, ec = 1.0, temperature = 24.0, humidity = 60.0, light = 300.0,
        waterLevelRaw = null, waterLevelDetected = true, mlPh = 0, mlEc = 0, mlTemp = 0, mlHumidity = 0
    )

    // A transport that delivers whatever the test feeds it
    private class FakeTransport(override val link: BoxLink) : BoxTransport {
        val feed = MutableSharedFlow<BoxTelemetryDto?>(extraBufferCapacity = 16)
        val modeFeed = MutableSharedFlow<BoxModeInfo>(extraBufferCapacity = 16)

        override fun modes() = modeFeed

        override suspend fun setRelay(actuator: String, on: Boolean) = BoxControlResult.Done()
        override suspend fun dose(type: String, volumeMl: Double) = BoxControlResult.Done()
        override suspend fun readings(onReading: suspend (BoxTelemetryDto?) -> Unit) {
            feed.collect { onReading(it) }
        }
    }

    private class Setup(testScope: TestScope) {
        val bluetooth = FakeTransport(BoxLink.BLUETOOTH)
        val hotspot = FakeTransport(BoxLink.HOTSPOT)
        val bluetoothState = MutableStateFlow<ConnectionState>(ConnectionState.Idle)
        val internet = MutableStateFlow(false)

        val session = BoxSession(
            scope = testScope.backgroundScope,
            transports = mapOf(BoxLink.BLUETOOTH to bluetooth, BoxLink.HOTSPOT to hotspot),
            bluetoothState = bluetoothState,
            internetAvailable = internet,
            selector = BoxLinkSelector(upgradeAfterMs = 5_000L),
            clock = { testScope.testScheduler.currentTime }
        )
    }

    // ---- nothing working ----

    @Test
    fun noLinkAtFirst() = runTest {
        val setup = Setup(this)
        runCurrent()

        assertNull(setup.session.link.value)
        assertNull(setup.session.reading.value)
        assertNull(setup.session.transport.value)
        assertEquals("Not connected to the box", setup.session.statusText.value)
    }

    // ---- the box's mode ----

    private val onlineByFallback = BoxModeInfo(BoxMode.ONLINE, BoxMode.LORA_BLE, autoFallback = true)

    @Test
    fun theModeDetailsOfTheLinkInUseAreShown() = runTest {
        val setup = Setup(this)
        setup.bluetoothState.value = ConnectionState.Ready(mtu)
        runCurrent()

        setup.bluetooth.modeFeed.emit(onlineByFallback)
        setup.bluetooth.feed.emit(reading(6.5))
        runCurrent()

        assertEquals(onlineByFallback, setup.session.state.value.modeInfo)
    }

    @Test
    fun modeDetailsFollowTheBoxWhenItChangesMode() = runTest {
        val setup = Setup(this)
        setup.bluetoothState.value = ConnectionState.Ready(mtu)
        runCurrent()
        setup.bluetooth.modeFeed.emit(onlineByFallback)
        setup.bluetooth.feed.emit(reading(6.5))
        runCurrent()

        val back = BoxModeInfo(BoxMode.LORA_BLE, BoxMode.LORA_BLE, autoFallback = false)
        setup.bluetooth.modeFeed.emit(back)
        runCurrent()

        assertEquals(back, setup.session.state.value.modeInfo)
    }

    @Test
    fun aLinkThatReportsNoModeHasNoModeDetails() = runTest {
        val setup = Setup(this)
        runCurrent()

        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()

        assertEquals(BoxLink.HOTSPOT, setup.session.link.value)
        assertNull(setup.session.state.value.modeInfo)
    }

    @Test
    fun modeDetailsAreNotShownOnceTheLinkHasGoneQuiet() = runTest {
        val setup = Setup(this)
        setup.bluetoothState.value = ConnectionState.Ready(mtu)
        runCurrent()
        setup.bluetooth.modeFeed.emit(onlineByFallback)
        setup.bluetooth.feed.emit(reading(6.5))
        runCurrent()

        setup.bluetooth.feed.emit(null)
        runCurrent()

        assertNull(setup.session.state.value.modeInfo)
    }

    // ---- the first check ----

    @Test
    fun aBoxIsNeverCheckedWhileItsReadingIsStillOnTheWay() = runTest {
        val setup = Setup(this)
        val seen = mutableListOf<BoxSessionState>()
        backgroundScope.launch { setup.session.state.collect { seen += it } }
        runCurrent()

        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()

        assertTrue(seen.none { it.checked && it.reading == null && it.link != null })
        assertEquals(6.1, seen.last().reading!!.ph!!, 0.0001)
        assertEquals(BoxLink.HOTSPOT, seen.last().link)
        assertTrue(seen.last().checked)
    }

    @Test
    fun notCheckedUntilALinkHasReported() = runTest {
        val setup = Setup(this)
        runCurrent()

        assertEquals(false, setup.session.checked.value)
    }

    @Test
    fun checkedOnceALinkReportsAReading() = runTest {
        val setup = Setup(this)
        runCurrent()

        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()

        assertEquals(true, setup.session.checked.value)
    }

    @Test
    fun checkedOnceALinkReportsNothingToo() = runTest {
        val setup = Setup(this)
        runCurrent()

        setup.hotspot.feed.emit(null)
        runCurrent()

        assertEquals(true, setup.session.checked.value)
        assertNull(setup.session.link.value)
    }

    // ---- one link ----

    @Test
    fun hotspotThatDeliversAReadingBecomesTheLink() = runTest {
        val setup = Setup(this)
        runCurrent()

        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()

        assertEquals(BoxLink.HOTSPOT, setup.session.link.value)
        assertEquals(6.1, setup.session.reading.value!!.ph!!, 0.0001)
        assertSame(setup.hotspot, setup.session.transport.value)
        assertEquals("Connected via Hotspot", setup.session.statusText.value)
    }

    @Test
    fun bluetoothThatIsConnectedButSilentIsNotALink() = runTest {
        val setup = Setup(this)
        setup.bluetoothState.value = ConnectionState.Ready(mtu)
        runCurrent()

        assertNull(setup.session.link.value)
    }

    @Test
    fun bluetoothReadingWithoutAReadyConnectionIsNotALink() = runTest {
        val setup = Setup(this)
        runCurrent()

        setup.bluetooth.feed.emit(reading(6.5))
        runCurrent()

        assertNull(setup.session.link.value)
    }

    @Test
    fun bluetoothThatIsReadyAndDeliveringBecomesTheLink() = runTest {
        val setup = Setup(this)
        setup.bluetoothState.value = ConnectionState.Ready(mtu)
        runCurrent()

        setup.bluetooth.feed.emit(reading(6.5))
        runCurrent()

        assertEquals(BoxLink.BLUETOOTH, setup.session.link.value)
        assertEquals(6.5, setup.session.reading.value!!.ph!!, 0.0001)
        assertSame(setup.bluetooth, setup.session.transport.value)
    }

    @Test
    fun internetAloneIsALinkWithNoReadingAndNoTransport() = runTest {
        val setup = Setup(this)
        runCurrent()

        setup.internet.value = true
        runCurrent()

        assertEquals(BoxLink.INTERNET, setup.session.link.value)
        assertNull(setup.session.reading.value)
        assertNull(setup.session.transport.value)
        assertEquals("Connected via Internet", setup.session.statusText.value)
    }

    // ---- two links ----

    @Test
    fun betterLinkIsTakenOnceItHasStayedUp() = runTest {
        val setup = Setup(this)
        runCurrent()
        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()

        setup.bluetoothState.value = ConnectionState.Ready(mtu)
        setup.bluetooth.feed.emit(reading(6.5))
        runCurrent()
        assertEquals(BoxLink.HOTSPOT, setup.session.link.value)

        advanceTimeBy(6_000L)
        runCurrent()

        assertEquals(BoxLink.BLUETOOTH, setup.session.link.value)
        assertEquals(6.5, setup.session.reading.value!!.ph!!, 0.0001)
    }

    @Test
    fun lostBluetoothFallsBackToTheHotspotAtOnce() = runTest {
        val setup = Setup(this)
        setup.bluetoothState.value = ConnectionState.Ready(mtu)
        runCurrent()
        setup.bluetooth.feed.emit(reading(6.5))
        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()
        assertEquals(BoxLink.BLUETOOTH, setup.session.link.value)

        setup.bluetooth.feed.emit(null)
        runCurrent()

        assertEquals(BoxLink.HOTSPOT, setup.session.link.value)
        assertEquals(6.1, setup.session.reading.value!!.ph!!, 0.0001)
        assertSame(setup.hotspot, setup.session.transport.value)
    }

    @Test
    fun lostConnectionFallsBackEvenWhileReadingsAreStillFresh() = runTest {
        val setup = Setup(this)
        setup.bluetoothState.value = ConnectionState.Ready(mtu)
        runCurrent()
        setup.bluetooth.feed.emit(reading(6.5))
        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()

        setup.bluetoothState.value = ConnectionState.Idle
        runCurrent()

        assertEquals(BoxLink.HOTSPOT, setup.session.link.value)
    }

    @Test
    fun readingFollowsTheLinkInUse() = runTest {
        val setup = Setup(this)
        runCurrent()
        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()

        setup.hotspot.feed.emit(reading(6.9))
        runCurrent()

        assertEquals(6.9, setup.session.reading.value!!.ph!!, 0.0001)
    }

    @Test
    fun hotspotGoingQuietLeavesTheInternetAsTheLink() = runTest {
        val setup = Setup(this)
        setup.internet.value = true
        runCurrent()
        setup.hotspot.feed.emit(reading(6.1))
        runCurrent()
        advanceTimeBy(6_000L)
        runCurrent()
        assertEquals(BoxLink.HOTSPOT, setup.session.link.value)

        setup.hotspot.feed.emit(null)
        runCurrent()

        assertEquals(BoxLink.INTERNET, setup.session.link.value)
        assertNull(setup.session.reading.value)
    }
}
