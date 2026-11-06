package com.example.smarthydro.transport

// The connectivity modes a box runs in, with the numbers the firmware reports and accepts for them
// (MODE_LORA_BLE, MODE_WIFI_CLOUD and MODE_EMERGENCY_SOFTAP in the firmware's Config.h)
enum class BoxMode(val code: Int, val label: String) {
    LORA_BLE(0, "LoRa"),
    ONLINE(1, "Online"),
    SOFTAP(2, "Hotspot");

    companion object {

        // The mode behind a reported number, or null for a number this app does not know
        fun fromCode(code: Int?): BoxMode? = entries.firstOrNull { it.code == code }
    }
}

// What a box says about its own connectivity mode: the one it is in, the one it was asked to be in, and whether it
// left that one on its own because the link failed
data class BoxModeInfo(
    val mode: BoxMode?,
    val preferred: BoxMode?,
    val autoFallback: Boolean
)
