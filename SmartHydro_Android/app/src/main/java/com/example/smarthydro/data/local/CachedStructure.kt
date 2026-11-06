package com.example.smarthydro.data.local

import androidx.room.Entity

// One structure a farmer paired while using the app without an account, so it survives the
// app being closed. Only ever written to or read from when nobody is signed in.
@Entity(tableName = "cached_structures", primaryKeys = ["id"])
data class CachedStructure(
    val id: String,
    val title: String,
    val iconType: String,
    val themeColorArgb: Int,
    val structureType: String,
    val macAddress: String?,
    val createdOn: String?,
    val siteId: String,
    val boxIpAddress: String = "192.168.4.1",
    val boxBluetoothAddress: String? = null
)
