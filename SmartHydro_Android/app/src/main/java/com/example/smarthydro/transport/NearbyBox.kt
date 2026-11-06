package com.example.smarthydro.transport

import com.example.smarthydro.models.StructureType
import com.example.smarthydro.util.MacAddressFormat

// The two kinds of box, as a box names itself over Bluetooth
enum class BoxKind { TOWER, FODDER }

// A box heard during a scan. The address is the one the phone uses to connect, and macSuffix is the last
// three bytes of the box's own MAC, which its Bluetooth name ends with
data class NearbyBox(
    val address: String,
    val name: String,
    val kind: BoxKind,
    val macSuffix: String,
    val rssi: Int,
    val seenAtMs: Long
) {

    // How close the box seems, in a word, going by its signal strength
    val signalLabel: String
        get() = when {
            rssi >= -60 -> "Strong signal"
            rssi >= -75 -> "Good signal"
            else -> "Weak signal"
        }
}

// Recognises a box from what it advertises and matches it to a structure
object BoxAdvertisement {

    // The service every box advertises (BLE_SERVICE_UUID in the firmware's Config.h), which is what a scan filters on
    const val SERVICE_UUID = "0000ff00-0000-1000-8000-00805f9b34fb"

    // "SmartHydro-Tower-" or "SmartHydro-Fodder-" followed by the last six hex digits of the box's MAC
    private val namePattern = Regex("^SmartHydro-(Tower|Fodder)-([0-9A-Fa-f]{6})$")

    // The box behind a scan result, or null when the name is not one a box gives itself
    fun parse(name: String?, address: String, rssi: Int, seenAtMs: Long): NearbyBox? {
        val match = name?.let { namePattern.matchEntire(it) } ?: return null
        val kind = if (match.groupValues[1] == "Fodder") BoxKind.FODDER else BoxKind.TOWER

        return NearbyBox(address, name, kind, match.groupValues[2].uppercase(), rssi, seenAtMs)
    }

    // The kind of box a structure uses. A hydroponic structure is a tower
    fun kindFor(type: StructureType): BoxKind =
        if (type == StructureType.FODDER) BoxKind.FODDER else BoxKind.TOWER

    // The last three bytes of a MAC in any separator style, upper case, or null when it is not a MAC
    fun suffixOf(mac: String?): String? =
        mac?.let { MacAddressFormat.normalise(it) }?.replace(":", "")?.takeLast(6)

    // Whether a box heard in a scan is the one a structure uses
    fun matches(box: NearbyBox, structureMac: String?, type: StructureType): Boolean =
        box.kind == kindFor(type) && box.macSuffix == suffixOf(structureMac)
}

// The boxes heard so far during a scan, newest sighting of each, with the ones not heard lately dropped
class NearbyBoxList(private val staleAfterMs: Long = 10_000L) {

    private val boxes = LinkedHashMap<String, NearbyBox>()

    // Keeps the newest sighting of a box
    @Synchronized
    fun onResult(box: NearbyBox) {
        val known = boxes[box.address]
        if (known == null || box.seenAtMs >= known.seenAtMs) boxes[box.address] = box
    }

    // The boxes heard within the stale window, strongest signal first, with equal signals ordered by name
    @Synchronized
    fun snapshot(nowMs: Long): List<NearbyBox> {
        boxes.values.removeAll { nowMs - it.seenAtMs > staleAfterMs }
        return boxes.values.sortedWith(compareByDescending<NearbyBox> { it.rssi }.thenBy { it.name })
    }
}
