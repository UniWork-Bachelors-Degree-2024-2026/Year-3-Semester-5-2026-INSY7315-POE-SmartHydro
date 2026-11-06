package com.example.smarthydro.transport

// Which ways of reaching a box work right now
data class LinkStatus(
    val bluetoothReady: Boolean,
    val hotspotReachable: Boolean,
    val internetAvailable: Boolean
) {

    // Whether this link works
    fun has(link: BoxLink): Boolean = when (link) {
        BoxLink.BLUETOOTH -> bluetoothReady
        BoxLink.HOTSPOT -> hotspotReachable
        BoxLink.INTERNET -> internetAvailable
    }

    // The best link that works, going by the order BoxLink lists them in, or null when none does
    fun best(): BoxLink? = BoxLink.entries.firstOrNull { has(it) }

    companion object {

        // Bluetooth only counts once the connection is ready, not while it is still connecting or pairing
        fun from(bluetooth: ConnectionState, hotspotReachable: Boolean, internetAvailable: Boolean) =
            LinkStatus(bluetooth is ConnectionState.Ready, hotspotReachable, internetAvailable)
    }
}

// Picks the link a screen uses to reach a box. A link that is lost is replaced at once by the next best, but a
// better link that turns up is only taken after it has stayed up for a while, so the indicator and the commands
// do not jump about every time a connection blinks
class BoxLinkSelector(private val upgradeAfterMs: Long = UPGRADE_AFTER_MS) {

    companion object {
        const val UPGRADE_AFTER_MS = 5_000L

        // What the indicator says when no link works
        const val NO_LINK_TEXT = "Not connected to the box"

        fun statusText(link: BoxLink?): String = link?.statusText ?: NO_LINK_TEXT

        // The line under a structure's name: the link in use, and what the person has to keep doing for it to work
        fun explanation(link: BoxLink?): String = when (link) {
            BoxLink.HOTSPOT -> "${link.statusText}. Switches work while your phone stays on its Wi-Fi."
            BoxLink.BLUETOOTH -> "${link.statusText}. Switches work while your phone is near the box."
            else -> statusText(link)
        }
    }

    // The link in use, or null when none works
    var current: BoxLink? = null
        private set

    // The best link seen in the latest status, and since when it has been the best
    private var candidate: BoxLink? = null
    private var candidateSince = 0L

    // Takes the latest status and says which link to use. nowMs is any steadily increasing clock
    @Synchronized
    fun select(status: LinkStatus, nowMs: Long): BoxLink? {
        val best = status.best()
        if (best != candidate) {
            candidate = best
            candidateSince = nowMs
        }

        val inUse = current
        current = when {
            best == null -> null
            inUse == null || !status.has(inUse) -> best
            best.ordinal < inUse.ordinal && nowMs - candidateSince >= upgradeAfterMs -> best
            else -> inUse
        }
        return current
    }
}
