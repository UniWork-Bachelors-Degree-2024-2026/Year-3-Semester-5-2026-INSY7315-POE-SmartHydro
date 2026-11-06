package com.example.smarthydro

import com.example.smarthydro.data.StructureCacheRules
import com.example.smarthydro.data.local.CachedStructure
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class StructureCacheRulesTest {

    private fun row(id: String, mac: String? = "AA:BB:CC:DD:EE:FF", ip: String = "192.168.4.1", ble: String? = null) =
        CachedStructure(
            id = id, title = "Tower $id", iconType = "flower", themeColorArgb = 0,
            structureType = "HYDROPONIC", macAddress = mac, createdOn = null, siteId = "",
            boxIpAddress = ip, boxBluetoothAddress = ble
        )

    @Test
    fun localIdsAreRecognised() {
        assertTrue(StructureCacheRules.isLocal("local-123"))
        assertFalse(StructureCacheRules.isLocal("42"))
    }

    @Test
    fun offlineOnlySeesLocalStructures() {
        val rows = listOf(row("local-1"), row("7"))
        assertEquals(listOf("local-1"), StructureCacheRules.visibleRows(rows, signedIn = false).map { it.id })
    }

    @Test
    fun signedInOnlySeesCloudStructures() {
        val rows = listOf(row("local-1"), row("7"))
        assertEquals(listOf("7"), StructureCacheRules.visibleRows(rows, signedIn = true).map { it.id })
    }

    @Test
    fun refreshKeepsSavedIpAndBluetooth() {
        val saved = listOf(row("7", ip = "192.168.4.20", ble = "11:22:33:44:55:66"))
        val fresh = listOf(row("7"))
        val merged = StructureCacheRules.keepPairing(fresh, saved).single()
        assertEquals("192.168.4.20", merged.boxIpAddress)
        assertEquals("11:22:33:44:55:66", merged.boxBluetoothAddress)
    }

    @Test
    fun refreshDropsBluetoothWhenMacChanged() {
        val saved = listOf(row("7", mac = "AA:BB:CC:DD:EE:FF", ble = "11:22:33:44:55:66"))
        val fresh = listOf(row("7", mac = "AA:BB:CC:DD:EE:00"))
        assertNull(StructureCacheRules.keepPairing(fresh, saved).single().boxBluetoothAddress)
    }

    @Test
    fun newCloudStructureKeepsDefaults() {
        val merged = StructureCacheRules.keepPairing(listOf(row("8")), emptyList()).single()
        assertEquals("192.168.4.1", merged.boxIpAddress)
        assertNull(merged.boxBluetoothAddress)
    }

    @Test
    fun editKeepsBluetoothOnlyForSameMac() {
        assertEquals("BT", StructureCacheRules.bluetoothAfterEdit("aa:bb:cc:dd:ee:ff", "AA-BB-CC-DD-EE-FF", "BT"))
        assertNull(StructureCacheRules.bluetoothAfterEdit("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:00", "BT"))
    }
}