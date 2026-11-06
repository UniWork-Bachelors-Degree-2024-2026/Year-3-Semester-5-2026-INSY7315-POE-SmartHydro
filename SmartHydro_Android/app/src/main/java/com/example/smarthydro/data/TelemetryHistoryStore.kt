package com.example.smarthydro.data

import com.example.smarthydro.data.local.CachedTelemetryReading
import com.example.smarthydro.data.local.LocalDatabase
import com.example.smarthydro.data.local.TelemetryDao
import com.example.smarthydro.models.BoxTelemetryDto
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.time.LocalDate
import java.time.ZoneId
import kotlin.coroutines.cancellation.CancellationException

// Keeps the readings a phone sees from a box on the phone itself, so charts have a history
// even with no internet. Nothing here is ever uploaded yet: every reading is saved as not
// synced, ready for the cloud upload to pick up later.
object TelemetryHistoryStore {

    private const val PRUNE_EVERY_MS = 60L * 60 * 1000

    // The newest stored time per box, so most readings are skipped without touching the database
    private val lastStoredAt = mutableMapOf<String, Long>()
    private val lock = Mutex()
    private var lastPruneAtMs = 0L

    // Saves a live reading from a box unless one was saved in the last few minutes. Returns
    // whether it was saved. A failure here never breaks the screen that is showing the reading
    suspend fun recordBoxReading(
        deviceMac: String,
        reading: BoxTelemetryDto,
        nowMs: Long = System.currentTimeMillis()
    ): Boolean = withContext(Dispatchers.IO) {
        val mac = HistoryLogic.normaliseMac(deviceMac)
        if (mac.isBlank()) return@withContext false

        // Checked again here so no route, hotspot or Bluetooth can store a fault value in the chart history
        val clean = HistoryLogic.withoutFaults(reading)

        try {
            lock.withLock {
                val dao = LocalDatabase.telemetryDao
                val last = lastStoredAt[mac] ?: dao.latestRecordedAt(mac)

                if (!HistoryLogic.shouldStore(last, nowMs)) {
                    if (last != null) lastStoredAt[mac] = last
                    return@withLock false
                }

                dao.insert(
                    CachedTelemetryReading(
                        deviceMac = mac,
                        recordedAtEpochMs = nowMs,
                        ph = clean.ph,
                        ec = clean.ec,
                        temperature = clean.temperature,
                        humidity = clean.humidity,
                        light = clean.light,
                        waterLevelRaw = reading.waterLevelRaw ?: 0,
                        waterLevelDetected = reading.waterLevelDetected ?: false,
                        mlPh = reading.mlPh,
                        mlEc = reading.mlEc,
                        mlTemp = reading.mlTemp,
                        mlHumidity = reading.mlHumidity,
                        isSynced = false
                    )
                )
                lastStoredAt[mac] = nowMs
                pruneIfDue(dao, nowMs)
                true
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            false
        }
    }

    // The last few calendar days of this box's readings, averaged per day in the phone's time
    // zone, with no row for a day nothing was recorded
    suspend fun dailyHistory(
        deviceMac: String,
        days: Int = 7,
        today: LocalDate = LocalDate.now()
    ): List<HistoryLogic.DayPoint> = withContext(Dispatchers.IO) {
        val mac = HistoryLogic.normaliseMac(deviceMac)
        val since = today.minusDays((days - 1).toLong())
            .atStartOfDay(ZoneId.systemDefault())
            .toInstant()
            .toEpochMilli()

        val rows = try {
            LocalDatabase.telemetryDao.dailyAverages(mac, since)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            emptyList()
        }

        HistoryLogic.lastDays(today, days, rows)
    }

    // Removes readings the cloud already has once they are a week old, and as a last resort
    // the oldest readings still waiting for the cloud once they are three months old
    private suspend fun pruneIfDue(dao: TelemetryDao, nowMs: Long) {
        if (nowMs - lastPruneAtMs < PRUNE_EVERY_MS) return
        lastPruneAtMs = nowMs

        dao.pruneSyncedBefore(nowMs - HistoryLogic.SYNCED_KEEP_DAYS * HistoryLogic.DAY_MS)
        dao.pruneUnsyncedBefore(nowMs - HistoryLogic.UNSYNCED_KEEP_DAYS * HistoryLogic.DAY_MS)
    }
}