package com.example.smarthydro.data

import com.example.smarthydro.data.local.CachedStructure

// Rules for which saved structures belong to offline mode and which belong to a signed-in account
object StructureCacheRules {

    // Structures added without an account get an id starting with "local-", cloud ones use the API's number
    private const val LocalPrefix = "local-"

    fun isLocal(id: String): Boolean = id.startsWith(LocalPrefix)

    // Offline mode only shows the farmer's own local structures,
    // signed in only shows the account's cloud structures
    fun visibleRows(rows: List<CachedStructure>, signedIn: Boolean): List<CachedStructure> =
        rows.filter { isLocal(it.id) != signedIn }

    // The cloud doesn't know the box's IP or Bluetooth address, so they're carried over
    // from the phone's saved copy of the same structure when a fresh list comes in
    fun keepPairing(fresh: List<CachedStructure>, saved: List<CachedStructure>): List<CachedStructure> {
        val savedById = saved.associateBy { it.id }
        return fresh.map { row ->
            val old = savedById[row.id] ?: return@map row
            // A new MAC means a different box, so the old Bluetooth address no longer applies
            val sameBox = sameMac(old.macAddress, row.macAddress)
            row.copy(
                boxIpAddress = old.boxIpAddress,
                boxBluetoothAddress = if (sameBox) old.boxBluetoothAddress else null
            )
        }
    }

    // Keeps the Bluetooth address only while the structure still points at the same box
    fun bluetoothAfterEdit(oldMac: String?, newMac: String?, oldBluetooth: String?): String? =
        if (sameMac(oldMac, newMac)) oldBluetooth else null

    private fun sameMac(a: String?, b: String?): Boolean =
        a?.trim()?.uppercase()?.replace('-', ':') == b?.trim()?.uppercase()?.replace('-', ':')
}