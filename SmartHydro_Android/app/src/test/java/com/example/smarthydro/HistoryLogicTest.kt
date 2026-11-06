package com.example.smarthydro

import com.example.smarthydro.data.HistoryLogic
import com.example.smarthydro.data.local.DailyAverageRow
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class HistoryLogicTest {

    @Test
    fun normaliseMac_formatsTwelveHexDigitsWithColons() {
        assertEquals("A4:CF:12:9B:3E:01", HistoryLogic.normaliseMac("a4cf129b3e01"))
    }

    @Test
    fun normaliseMac_keepsAnAlreadyFormattedMacTheSame() {
        assertEquals("A4:CF:12:9B:3E:01", HistoryLogic.normaliseMac("A4:CF:12:9B:3E:01"))
        assertEquals("A4:CF:12:9B:3E:01", HistoryLogic.normaliseMac("  a4-cf-12-9b-3e-01 "))
    }

    @Test
    fun normaliseMac_leavesSomethingThatIsNotAMacTidyButUnchanged() {
        assertEquals("NOT A MAC", HistoryLogic.normaliseMac(" not a mac "))
        assertEquals("", HistoryLogic.normaliseMac("   "))
    }

    @Test
    fun shouldStore_alwaysStoresTheFirstReading() {
        assertTrue(HistoryLogic.shouldStore(null, 1_000L))
    }

    @Test
    fun shouldStore_skipsAReadingUnderFiveMinutesAfterTheLast() {
        val last = 1_000_000L
        assertFalse(HistoryLogic.shouldStore(last, last + HistoryLogic.MIN_GAP_MS - 1))
    }

    @Test
    fun shouldStore_storesAtExactlyFiveMinutes() {
        val last = 1_000_000L
        assertTrue(HistoryLogic.shouldStore(last, last + HistoryLogic.MIN_GAP_MS))
    }

    @Test
    fun shouldStore_storesWhenTheClockMovedBackwards() {
        assertTrue(HistoryLogic.shouldStore(5_000_000L, 4_000_000L))
    }

    @Test
    fun finiteOrNull_dropsNotANumberAndInfinity() {
        assertNull(HistoryLogic.finiteOrNull(Double.NaN))
        assertNull(HistoryLogic.finiteOrNull(Double.POSITIVE_INFINITY))
        assertEquals(6.2, HistoryLogic.finiteOrNull(6.2)!!, 0.0)
        assertEquals(0.0, HistoryLogic.finiteOrNull(0.0)!!, 0.0)
    }

    @Test
    fun lastDays_returnsSevenDaysEndingTodayOldestFirst() {
        val today = LocalDate.of(2026, 10, 7)
        val days = HistoryLogic.lastDays(today, 7, emptyList())

        assertEquals(7, days.size)
        assertEquals(LocalDate.of(2026, 10, 1), days.first().date)
        assertEquals(today, days.last().date)
    }

    @Test
    fun lastDays_leavesAGapForADayWithNoReadings() {
        val today = LocalDate.of(2026, 10, 7)
        val rows = listOf(
            row("2026-10-05", 6.1),
            row("2026-10-07", 6.3)
        )
        val days = HistoryLogic.lastDays(today, 7, rows)

        assertEquals(6.1, days[4].row!!.avgPh!!, 0.0)
        assertNull(days[5].row)
        assertEquals(6.3, days[6].row!!.avgPh!!, 0.0)
        assertEquals(5, days.count { it.row == null })
    }

    @Test
    fun lastDays_ignoresRowsOutsideTheWindow() {
        val today = LocalDate.of(2026, 10, 7)
        val days = HistoryLogic.lastDays(today, 7, listOf(row("2026-09-01", 7.0)))

        assertTrue(days.all { it.row == null })
    }

    @Test
    fun retention_isAWeekForSyncedAndThreeMonthsForUnsynced() {
        assertEquals(7L, HistoryLogic.SYNCED_KEEP_DAYS)
        assertEquals(90L, HistoryLogic.UNSYNCED_KEEP_DAYS)
        assertTrue(HistoryLogic.UNSYNCED_KEEP_DAYS > HistoryLogic.SYNCED_KEEP_DAYS)
    }

    private fun row(day: String, ph: Double) = DailyAverageRow(
        day = day, avgPh = ph, avgEc = null, avgLight = null,
        avgTemperature = null, avgHumidity = null, samples = 12
    )
}
