package com.example.smarthydro.data

import com.example.smarthydro.models.StructureType

// Works out the name of a box's own Wi-Fi hotspot from its controller MAC, so the app can tell a
// farmer exactly which network to join
object BoxPairing {

    // The start of every hotspot name for this kind of box
    fun hotspotPrefix(structureType: StructureType): String {
        return if (structureType == StructureType.FODDER) "SmartHydro-Fodder-" else "SmartHydro-Setup-"
    }

    // The full hotspot name, for example "SmartHydro-Setup-EEFF" or null when the MAC isn't six bytes
    fun expectedSsid(macAddress: String, structureType: StructureType): String? {
        val parts = macAddress.trim().split(':', '-')
        if (parts.size != 6 || parts.any { it.length != 2 }) return null

        return hotspotPrefix(structureType) + (parts[4] + parts[5]).uppercase()
    }
}