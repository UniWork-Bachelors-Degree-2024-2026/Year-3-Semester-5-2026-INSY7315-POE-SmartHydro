package com.example.smarthydro.transport

import com.example.smarthydro.models.BoxRelaysDto
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.util.MacAddressFormat
import com.google.gson.JsonObject
import com.google.gson.JsonParser

// One telemetry frame from a box over Bluetooth: the reading the screens already understand,
// plus the connectivity details the box reports that the hotspot reply has no place for
data class BleTelemetry(
    val deviceMac: String,
    val reading: BoxTelemetryDto,
    val mode: BoxMode?,
    val preferredMode: BoxMode?,
    // True when the box left the preferred mode on its own because the link failed
    val autoFallback: Boolean,
    val alert: String?
)

// Turns the compact JSON a box notifies on its telemetry characteristic (FF01) into a BleTelemetry.
// The keys are the ones the firmware's buildCompactJson writes (tower and fodder use the same ones)
object BleTelemetryMapper {

    // The bits of the box's "sf" sensor fault field, one per sensor
    private const val FAULT_PH = 1
    private const val FAULT_EC = 2
    private const val FAULT_TEMPERATURE = 4
    private const val FAULT_HUMIDITY = 8
    private const val FAULT_LIGHT = 16

    // Returns null for anything that is not a whole telemetry frame. A frame cut short, which is what a
    // phone gets when the MTU was never raised, fails to parse and is dropped instead of half shown
    fun parse(json: String): BleTelemetry? {
        val root = try {
            JsonParser.parseString(json)
        } catch (e: Exception) {
            return null
        }
        if (!root.isJsonObject) return null
        val frame = root.asJsonObject

        // Every telemetry frame names its box, which is also how it is matched to a structure
        val deviceMac = frame.text("m")?.let { MacAddressFormat.normalise(it) } ?: return null

        // A sensor that has failed is reported as faulty and its value is not to be trusted
        val faults = frame.int("sf") ?: 0
        fun reading(key: String, fault: Int): Double? = if (faults and fault != 0) null else frame.number(key)

        val relays = BoxRelaysDto(
            growLight = frame.flag("gl"),
            circulationFan = frame.flag("fc"),
            // "s" is the water pump on a tower and the spray system on a fodder box, and the app
            // switches both from the same water switch
            mainWaterPump = frame.flag("s"),
            extractorFan = frame.flag("fe"),
            phUpPump = frame.flag("pu"),
            phDownPump = frame.flag("pd"),
            nutrientUpPump = frame.flag("nu"),
            dilutionDownPump = frame.flag("nd")
        )

        val boxReading = BoxTelemetryDto(
            ph = reading("p", FAULT_PH),
            ec = reading("e", FAULT_EC),
            temperature = reading("t", FAULT_TEMPERATURE),
            humidity = reading("h", FAULT_HUMIDITY),
            light = if (faults and FAULT_LIGHT != 0) null else frame.number("lux") ?: frame.number("l"),
            // Bluetooth frames carry no raw water level and no predictions, only whether water is detected
            waterLevelRaw = null,
            waterLevelDetected = frame.flag("w"),
            mlPh = 0,
            mlEc = 0,
            mlTemp = 0,
            mlHumidity = 0,
            // An older box leaves the relays out altogether, which the screens read as "not reported"
            relays = if (relayValues(relays).all { it == null }) null else relays
        )

        return BleTelemetry(
            deviceMac = deviceMac,
            reading = boxReading,
            mode = BoxMode.fromCode(frame.int("md")),
            preferredMode = BoxMode.fromCode(frame.int("pm")),
            autoFallback = frame.int("mr") == 1,
            alert = frame.text("al")
        )
    }

    private fun relayValues(relays: BoxRelaysDto) = listOf(
        relays.growLight, relays.circulationFan, relays.mainWaterPump, relays.extractorFan,
        relays.phUpPump, relays.phDownPump, relays.nutrientUpPump, relays.dilutionDownPump
    )

    // A text value, or null when the key is missing or not text
    private fun JsonObject.text(key: String): String? = try {
        get(key)?.takeIf { it.isJsonPrimitive }?.asString?.takeIf { it.isNotBlank() }
    } catch (e: Exception) {
        null
    }

    // A finite number, or null when the key is missing or holds something else, so one bad
    // value costs only that field and not the whole frame
    private fun JsonObject.number(key: String): Double? = try {
        get(key)?.takeIf { it.isJsonPrimitive }?.asDouble?.takeIf { it.isFinite() }
    } catch (e: Exception) {
        null
    }

    private fun JsonObject.int(key: String): Int? = number(key)?.toInt()

    // The firmware writes switches as 1 and 0, so both are read as on and off
    private fun JsonObject.flag(key: String): Boolean? = int(key)?.let { it != 0 }
}
