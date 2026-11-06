package com.example.smarthydro

import com.example.smarthydro.models.StructureType
import com.example.smarthydro.transport.BoxAdvertisement
import com.example.smarthydro.transport.BoxKind
import com.example.smarthydro.transport.NearbyBox
import com.example.smarthydro.transport.NearbyBoxList
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks how boxes are recognised from a Bluetooth scan, matched to a structure and listed
class NearbyBoxTest {

    private val towerName = "SmartHydro-Tower-9B3E01"
    private val fodderName = "SmartHydro-Fodder-44A702"

    private fun box(address: String, name: String = towerName, rssi: Int = -60, seenAtMs: Long = 0L) =
        BoxAdvertisement.parse(name, address, rssi, seenAtMs)!!

    // ---- recognising a box ----

    @Test
    fun towerNameIsRecognised() {
        val found = BoxAdvertisement.parse(towerName, "11:22:33:44:55:66", -55, 1000L)!!

        assertEquals(BoxKind.TOWER, found.kind)
        assertEquals("9B3E01", found.macSuffix)
        assertEquals("11:22:33:44:55:66", found.address)
        assertEquals(-55, found.rssi)
        assertEquals(1000L, found.seenAtMs)
    }

    @Test
    fun fodderNameIsRecognised() {
        val found = BoxAdvertisement.parse(fodderName, "11:22:33:44:55:66", -70, 0L)!!

        assertEquals(BoxKind.FODDER, found.kind)
        assertEquals("44A702", found.macSuffix)
    }

    @Test
    fun suffixInLowerCaseIsWrittenUpperCase() {
        assertEquals("9B3E01", BoxAdvertisement.parse("SmartHydro-Tower-9b3e01", "addr", -60, 0L)!!.macSuffix)
    }

    @Test
    fun namesThatAreNotABoxAreIgnored() {
        listOf(
            null, "", "Bose NC 700 Headphones", "SmartHydro-Tower", "SmartHydro-Fodder-",
            "SmartHydro-Tower-9B3E0", "SmartHydro-Tower-9B3E012", "SmartHydro-Tower-ZZZZZZ",
            "SmartHydro-Camera-9B3E01", "smarthydro-tower-9B3E01", "SmartHydro-Tower-9B3E01 "
        ).forEach { assertNull("$it", BoxAdvertisement.parse(it, "addr", -60, 0L)) }
    }

    // ---- matching a structure ----

    @Test
    fun suffixIsTheLastThreeBytesOfTheMac() {
        assertEquals("9B3E01", BoxAdvertisement.suffixOf("A4:CF:12:9B:3E:01"))
        assertEquals("9B3E01", BoxAdvertisement.suffixOf("a4-cf-12-9b-3e-01"))
        assertEquals("9B3E01", BoxAdvertisement.suffixOf("a4cf129b3e01"))
    }

    @Test
    fun suffixOfSomethingThatIsNotAMacIsNull() {
        assertNull(BoxAdvertisement.suffixOf(null))
        assertNull(BoxAdvertisement.suffixOf(""))
        assertNull(BoxAdvertisement.suffixOf("not a mac"))
    }

    @Test
    fun boxMatchesItsOwnStructure() {
        assertTrue(BoxAdvertisement.matches(box("a1"), "A4:CF:12:9B:3E:01", StructureType.HYDROPONIC))
        assertTrue(BoxAdvertisement.matches(box("a2", fodderName), "B8:D6:1A:44:A7:02", StructureType.FODDER))
    }

    @Test
    fun boxDoesNotMatchAnotherStructure() {
        assertFalse(BoxAdvertisement.matches(box("a1"), "A4:CF:12:9B:3E:02", StructureType.HYDROPONIC))
        assertFalse(BoxAdvertisement.matches(box("a1"), "A4:CF:12:9B:3E:01", StructureType.FODDER))
        assertFalse(BoxAdvertisement.matches(box("a1"), null, StructureType.HYDROPONIC))
        assertFalse(BoxAdvertisement.matches(box("a1"), "not a mac", StructureType.HYDROPONIC))
    }

    // ---- listing the boxes heard ----

    @Test
    fun strongestSignalIsListedFirst() {
        val list = NearbyBoxList()
        list.onResult(box("far", rssi = -85))
        list.onResult(box("near", rssi = -45))
        list.onResult(box("middle", rssi = -65))

        assertEquals(listOf("near", "middle", "far"), list.snapshot(0L).map { it.address })
    }

    @Test
    fun equalSignalsAreOrderedByName() {
        val list = NearbyBoxList()
        list.onResult(box("b", name = fodderName, rssi = -60))
        list.onResult(box("a", name = towerName, rssi = -60))

        assertEquals(listOf(fodderName, towerName), list.snapshot(0L).map { it.name })
    }

    @Test
    fun aBoxHeardAgainIsListedOnceWithItsNewestSighting() {
        val list = NearbyBoxList()
        list.onResult(box("a", rssi = -80, seenAtMs = 1000L))
        list.onResult(box("a", rssi = -50, seenAtMs = 2000L))

        val heard = list.snapshot(2000L)

        assertEquals(1, heard.size)
        assertEquals(-50, heard[0].rssi)
    }

    @Test
    fun anOlderSightingDoesNotReplaceANewerOne() {
        val list = NearbyBoxList()
        list.onResult(box("a", rssi = -50, seenAtMs = 2000L))
        list.onResult(box("a", rssi = -80, seenAtMs = 1000L))

        assertEquals(-50, list.snapshot(2000L)[0].rssi)
    }

    @Test
    fun aBoxNotHeardLatelyIsDropped() {
        val list = NearbyBoxList(staleAfterMs = 10_000L)
        list.onResult(box("old", seenAtMs = 0L))
        list.onResult(box("recent", seenAtMs = 9_000L))

        val heard = list.snapshot(12_000L)

        assertEquals(listOf("recent"), heard.map { it.address })
    }

    @Test
    fun aBoxExactlyAtTheLimitIsKept() {
        val list = NearbyBoxList(staleAfterMs = 10_000L)
        list.onResult(box("edge", seenAtMs = 0L))

        assertNotNull(list.snapshot(10_000L).firstOrNull())
        assertTrue(list.snapshot(10_001L).isEmpty())
    }

    @Test
    fun emptyScanGivesAnEmptyList() {
        assertTrue(NearbyBoxList().snapshot(0L).isEmpty())
    }

    @Test
    fun serviceUuidMatchesTheFirmware() {
        assertEquals("0000ff00-0000-1000-8000-00805f9b34fb", BoxAdvertisement.SERVICE_UUID)
    }
}
