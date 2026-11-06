package com.example.smarthydro

import com.example.smarthydro.data.BoxPairing
import com.example.smarthydro.models.StructureType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

// Checks the hotspot name the pairing screen tells farmers to join matches what the firmware broadcasts
class BoxPairingTest {

    // Mock data: a controller MAC as the API stores it
    private val controllerMac = "AA:BB:CC:DD:EE:FF"

    @Test
    fun towerHotspotUsesTheSetupNameAndLastTwoBytes() {
        assertEquals("SmartHydro-Setup-EEFF", BoxPairing.expectedSsid(controllerMac, StructureType.HYDROPONIC))
    }

    @Test
    fun fodderHotspotUsesTheFodderName() {
        assertEquals("SmartHydro-Fodder-EEFF", BoxPairing.expectedSsid(controllerMac, StructureType.FODDER))
    }

    @Test
    fun lowercaseOrDashedMacsGiveTheSameName() {
        assertEquals("SmartHydro-Setup-EEFF", BoxPairing.expectedSsid("aa:bb:cc:dd:ee:ff", StructureType.HYDROPONIC))
        assertEquals("SmartHydro-Setup-EEFF", BoxPairing.expectedSsid("AA-BB-CC-DD-EE-FF", StructureType.HYDROPONIC))
    }

    @Test
    fun aMacThatIsNotSixBytesGivesNoName() {
        assertNull(BoxPairing.expectedSsid("AA:BB:CC", StructureType.HYDROPONIC))
        assertNull(BoxPairing.expectedSsid("", StructureType.FODDER))
    }

    @Test
    fun prefixMatchesEachBoxType() {
        assertEquals("SmartHydro-Setup-", BoxPairing.hotspotPrefix(StructureType.HYDROPONIC))
        assertEquals("SmartHydro-Fodder-", BoxPairing.hotspotPrefix(StructureType.FODDER))
    }
}