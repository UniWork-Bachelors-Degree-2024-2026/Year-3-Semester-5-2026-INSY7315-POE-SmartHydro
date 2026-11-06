package com.example.smarthydro.transport

import com.example.smarthydro.data.BoxControlResult
import com.google.gson.JsonParser

// How far a command has got, as the box reports it. A relay switch is finished once it is accepted,
// a dose is accepted when its pump starts and completed when the pump stops
enum class AckPhase(val wire: String) {
    ACCEPTED("accepted"),
    COMPLETED("completed"),
    CANCELLED("cancelled")
}

// The box's answer to one command, as it notifies it on its acknowledgement characteristic (FF03)
data class BleCommandAck(
    val commandId: Int,
    val ok: Boolean,
    val reason: String,
    val phase: AckPhase
) {

    // What the screens are told. A dose the box stopped before it finished counts as refused
    fun toResult(): BoxControlResult = when {
        !ok -> BoxControlResult.Refused(refusalMessage(reason))
        phase == AckPhase.CANCELLED -> BoxControlResult.Refused(CANCELLED_MESSAGE)
        else -> BoxControlResult.Done()
    }

    companion object {

        const val CANCELLED_MESSAGE = "The box stopped that dose before it finished."

        // What the person is told for a reason the box gives, the reasons being the ones in the firmware's
        // CommandProtocol.h, DosingPolicy.h and tower sketch. Any other reason gets the general message
        fun refusalMessage(reason: String): String = when (reason) {
            "sensor_fault" -> "The box can't dose while a sensor isn't reading properly."
            "pump_busy" -> "A dose is already running. Wait for it to finish."
            "mixing_lockout" -> "The box is still mixing the last dose and can't dose again yet."
            "ph_hourly_cap" -> "The box has already made as much pH correction as it allows this hour."
            "dose_count_limit" -> "The box has reached its limit of doses for now."
            "pulse_limit", "invalid_dose" -> "The box won't run a dose of that size."
            "outer_safety_limit" -> "The box's safety checks stopped that dose."
            "safety_storage", "cutoff_timer" -> "The box couldn't start the dose safely. Please try again."
            "invalid_command" -> "The box didn't understand that. Please try again."
            else -> "The box didn't accept that. Please try again."
        }
    }
}

// Reads the JSON a box notifies on its acknowledgement characteristic
object BleCommandAckParser {

    // Returns null for anything that is not a whole answer, such as a cut-off frame, a missing id or a phase
    // this app does not know. A command id of 0 is kept: the box uses it when it could not read the id at all
    fun parse(json: String): BleCommandAck? {
        val frame = try {
            JsonParser.parseString(json).takeIf { it.isJsonObject }?.asJsonObject
        } catch (e: Exception) {
            null
        } ?: return null

        return try {
            val commandId = frame.get("cid")?.asInt ?: return null
            val ok = readFlag(frame.get("ok")) ?: return null
            val phase = AckPhase.entries.firstOrNull { it.wire == frame.get("phase")?.asString } ?: return null

            BleCommandAck(commandId, ok, frame.get("reason")?.asString.orEmpty(), phase)
        } catch (e: Exception) {
            null
        }
    }

    // The box writes true and false, and the same switch can arrive as 1 and 0
    private fun readFlag(value: com.google.gson.JsonElement?): Boolean? {
        val primitive = value?.takeIf { it.isJsonPrimitive }?.asJsonPrimitive ?: return null
        return if (primitive.isBoolean) primitive.asBoolean else primitive.asInt != 0
    }
}
