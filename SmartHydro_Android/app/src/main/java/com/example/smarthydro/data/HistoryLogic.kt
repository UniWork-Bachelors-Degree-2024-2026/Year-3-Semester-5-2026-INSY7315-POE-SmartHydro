package com.example.smarthydro.data

import com.example.smarthydro.data.local.DailyAverageRow
import com.example.smarthydro.models.BoxTelemetryDto
import java.time.LocalDate

// The rules for how readings are kept on the phone, kept free of Android code so they can be
// tested on their own
object HistoryLogic {

    const val DAY_MS = 24L * 60 * 60 * 1000

    // How often a screen showing a directly paired box reads it while it is open
    const val BOX_POLL_INTERVAL_MS = 60_000L

    // At most one reading per box is kept in this window; a box that is read more often is skipped
    const val MIN_GAP_MS = 5L * 60 * 1000

    // A reading the cloud already has is only kept this long
    const val SYNCED_KEEP_DAYS = 7L

    // A reading still waiting for the cloud is kept this long, then the oldest are dropped
    const val UNSYNCED_KEEP_DAYS = 90L

    // Turns a MAC as typed by a person or returned by the API into one form, so the same box
    // always lands under the same key. Twelve hex digits become AA:BB:CC:DD:EE:FF
    fun normaliseMac(raw: String): String {
        val digits = raw.filter { it.isLetterOrDigit() }.uppercase()
        val isMac = digits.length == 12 && digits.all { it in '0'..'9' || it in 'A'..'F' }
        return if (isMac) digits.chunked(2).joinToString(":") else raw.trim().uppercase()
    }

    // Whether a new reading is far enough after the last stored one to be kept
    fun shouldStore(lastStoredAtMs: Long?, nowMs: Long): Boolean {
        if (lastStoredAtMs == null) return true

        // A clock that moved backwards must not stop storing until time catches up
        if (nowMs < lastStoredAtMs) return true

        return nowMs - lastStoredAtMs >= MIN_GAP_MS
    }

    // A reading that is not a real number is treated as no reading at all
    fun finiteOrNull(value: Double?): Double? = if (value != null && value.isFinite()) value else null

    // The range each sensor can really read. Over the hotspot a box reports a failed or missing
    // sensor with a stand-in number instead
    private const val PH_MIN = 0.0
    private const val PH_MAX = 14.0
    private const val EC_MAX_MS = 20.0
    private const val TEMPERATURE_MIN_C = -40.0
    private const val TEMPERATURE_MAX_C = 80.0
    private const val HUMIDITY_MAX = 100.0
    private const val LIGHT_MAX_LUX = 200_000.0

    // Keeps a reading only when it's a real number inside the range the sensor can read
    fun inRangeOrNull(value: Double?, min: Double, max: Double): Double? {
        val number = finiteOrNull(value) ?: return null
        return if (number in min..max) number else null
    }

    // The same reading with every faulty sensor value turned into "no reading"
    fun withoutFaults(reading: BoxTelemetryDto): BoxTelemetryDto {
        return reading.copy(
            ph = inRangeOrNull(reading.ph, PH_MIN, PH_MAX)?.takeIf { it > PH_MIN },
            ec = inRangeOrNull(reading.ec, 0.0, EC_MAX_MS),
            temperature = inRangeOrNull(reading.temperature, TEMPERATURE_MIN_C, TEMPERATURE_MAX_C),
            humidity = inRangeOrNull(reading.humidity, 0.0, HUMIDITY_MAX),
            light = inRangeOrNull(reading.light, 0.0, LIGHT_MAX_LUX)
        )
    }

    class DayPoint(val date: LocalDate, val row: DailyAverageRow?)

    // The last few calendar days ending today, oldest first, with no row for a day that has no readings
    fun lastDays(today: LocalDate, days: Int, rows: List<DailyAverageRow>): List<DayPoint> {
        val byDay = rows.associateBy { it.day }

        return (days - 1 downTo 0).map { back ->
            val date = today.minusDays(back.toLong())
            DayPoint(date, byDay[date.toString()])
        }
    }
}