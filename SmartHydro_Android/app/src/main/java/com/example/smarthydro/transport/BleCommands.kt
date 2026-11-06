package com.example.smarthydro.transport

import com.google.gson.JsonObject

// Builds the JSON a box accepts on its command characteristic (FF02). Each command carries the box's MAC,
// so a box that is not the one meant ignores it, and an id, so the box's answer can be matched to it.
// The keys are the ones the firmware's CommandProtocol.h reads
object BleCommands {

    // The least a box will run for one dose command, and the most it will run for a pH dose and for any other
    // dose. It changes or refuses anything outside this, so a dose is checked here before it is sent
    const val MIN_DOSE_ML = 0.5
    const val MAX_PH_DOSE_ML = 2.0
    const val MAX_DOSE_ML = 25.0

    // The dose types the app uses and the key the box reads each one under
    private val doseKeys = mapOf(
        "PH_UP" to "pu",
        "PH_DOWN" to "pd",
        "NUTRIENT_UP" to "nu",
        "DILUTION_DOWN" to "nd"
    )

    // The most a box will run for one dose of this type
    fun maxDoseMl(type: String): Double = if (type == "PH_UP" || type == "PH_DOWN") MAX_PH_DOSE_ML else MAX_DOSE_ML

    // Switches one relay on or off, by the actuator names BoxControl.actuatorFor returns
    fun relay(deviceMac: String, commandId: Int, actuator: String, on: Boolean): String =
        command(deviceMac, commandId).apply {
            addProperty("actuator", actuator)
            addProperty("state", on)
        }.toString()

    // Runs one dose pump for one command. Returns null for a type the box does not know or a volume the box
    // would not run as asked
    fun dose(deviceMac: String, commandId: Int, type: String, volumeMl: Double): String? {
        val key = doseKeys[type] ?: return null
        if (!volumeMl.isFinite() || volumeMl < MIN_DOSE_ML || volumeMl > maxDoseMl(type)) return null

        return command(deviceMac, commandId).apply { addProperty(key, volumeMl) }.toString()
    }

    // Asks the box to change its connectivity mode
    fun mode(deviceMac: String, commandId: Int, mode: BoxMode): String =
        command(deviceMac, commandId).apply { addProperty("mode", mode.code) }.toString()

    private fun command(deviceMac: String, commandId: Int) = JsonObject().apply {
        addProperty("m", deviceMac)
        addProperty("cid", commandId)
    }
}
