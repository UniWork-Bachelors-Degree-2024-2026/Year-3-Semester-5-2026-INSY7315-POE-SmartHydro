package com.example.smarthydro.models

import androidx.compose.ui.graphics.Color

enum class StructureType {
    FODDER,
    HYDROPONIC
}

data class StructureItem(
    val id: String,
    val title: String,
    val subtitle: String? = null,
    val subtitleColor: Color? = null,
    val iconType: String,
    val themeColor: Color,
    val structureType: StructureType,
    val macAddress: String? = null,
    val createdOn: String? = null,
    val siteId: String = "",
    // The box's local IP once a farmer has paired with it over its own hotspot.
    // Defaults to the ESP32's standard SoftAP gateway address.
    val boxIpAddress: String = "192.168.4.1",
    val cameraMac: String? = null,
    // The box's Bluetooth address once a farmer has paired with it over Bluetooth, so it can be
    // connected again without searching for it
    val boxBluetoothAddress: String? = null
)
