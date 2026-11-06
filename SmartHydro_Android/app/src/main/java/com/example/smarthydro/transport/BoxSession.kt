package com.example.smarthydro.transport

import com.example.smarthydro.models.BoxTelemetryDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

// What a screen needs to know about a box at one moment. The three belong together, so a screen reads them in one
// go and never sees a box called checked while its reading is still on the way
data class BoxSessionState(
    // The link in use, or null when none works. The internet is only available to the cloud screens, so this can
    // name it with no reading behind it
    val link: BoxLink?,

    // The latest reading from the link in use. It is null for the internet, whose readings come from the cloud
    val reading: BoxTelemetryDto?,

    // True once any link has reported, with a reading or with nothing, so a screen does not call the box silent
    // before it has had a chance to answer
    val checked: Boolean,

    // What the box says about its connectivity mode, when the link in use carries it and has a reading
    val modeInfo: BoxModeInfo? = null
)

// Everything the screens need to know about one box: which link reaches it right now, its latest reading and
// the transport to send commands through. It listens on every transport it is given, and a link only counts as
// working while it is actually delivering readings, so no link is assumed to work
class BoxSession(
    private val scope: CoroutineScope,
    private val transports: Map<BoxLink, BoxTransport>,
    bluetoothState: Flow<ConnectionState>,
    internetAvailable: Flow<Boolean>,
    private val selector: BoxLinkSelector = BoxLinkSelector(),
    private val clock: () -> Long = { System.nanoTime() / 1_000_000L }
) {

    companion object {

        // How often the choice is looked at again with no change, so a better link that has stayed up gets taken
        const val RECHECK_MS = 1_000L
    }

    // The newest reading each link has delivered, null once that link has gone quiet
    private val latest = MutableStateFlow<Map<BoxLink, BoxTelemetryDto?>>(emptyMap())

    // The newest connectivity mode details each link has reported
    private val modes = MutableStateFlow<Map<BoxLink, BoxModeInfo>>(emptyMap())

    init {
        transports.forEach { (link, transport) ->
            scope.launch { transport.readings { reading -> latest.update { it + (link to reading) } } }
            scope.launch { transport.modes().collect { info -> modes.update { it + (link to info) } } }
        }
    }

    private val recheck: Flow<Unit> = flow {
        while (true) {
            emit(Unit)
            delay(RECHECK_MS)
        }
    }

    val state: StateFlow<BoxSessionState> = combine(bluetoothState, latest, internetAvailable, recheck, modes) { bluetooth, readings, internet, _, modeDetails ->
        val status = LinkStatus(
            // A connection that is ready but silent is not a working link yet
            bluetoothReady = bluetooth is ConnectionState.Ready && readings[BoxLink.BLUETOOTH] != null,
            hotspotReachable = readings[BoxLink.HOTSPOT] != null,
            internetAvailable = internet
        )
        val link = selector.select(status, clock())
        val reading = link?.let { readings[it] }

        // The details come with a reading, so they are only trusted while the link in use is delivering one
        BoxSessionState(
            link = link,
            reading = reading,
            checked = readings.isNotEmpty(),
            modeInfo = if (reading != null) link?.let { modeDetails[it] } else null
        )
    }.distinctUntilChanged().stateIn(scope, SharingStarted.Eagerly, BoxSessionState(link = null, reading = null, checked = false))

    // The link in use, or null when none works
    val link: StateFlow<BoxLink?> = state.map { it.link }.distinctUntilChanged()
        .stateIn(scope, SharingStarted.Eagerly, null)

    // The latest reading from the link in use
    val reading: StateFlow<BoxTelemetryDto?> = state.map { it.reading }.distinctUntilChanged()
        .stateIn(scope, SharingStarted.Eagerly, null)

    // True once any link has reported
    val checked: StateFlow<Boolean> = state.map { it.checked }.distinctUntilChanged()
        .stateIn(scope, SharingStarted.Eagerly, false)

    // The transport commands go through, or null when the link in use has none here, which is the case for the internet
    val transport: StateFlow<BoxTransport?> = link.map { inUse -> inUse?.let { transports[it] } }
        .stateIn(scope, SharingStarted.Eagerly, null)

    // What the connection indicator says
    val statusText: StateFlow<String> = link.map { BoxLinkSelector.statusText(it) }
        .stateIn(scope, SharingStarted.Eagerly, BoxLinkSelector.NO_LINK_TEXT)

    // Which link reaches the box is written to the log each time it changes, to follow a switch between links
    init {
        scope.launch { link.collect { BoxLog.i("link", "now ${it?.label ?: "none"}") } }
    }
}
