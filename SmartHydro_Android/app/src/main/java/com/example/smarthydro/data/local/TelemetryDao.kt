package com.example.smarthydro.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface TelemetryDao {

    // Replaces any existing reading for the same device at the same moment, so re-caching
    // something already stored (e.g. after a re-sync) never creates a duplicate row
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(reading: CachedTelemetryReading)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(readings: List<CachedTelemetryReading>)

    @Query("SELECT * FROM cached_telemetry_readings WHERE deviceMac = :deviceMac ORDER BY recordedAtEpochMs DESC LIMIT 1")
    suspend fun latestFor(deviceMac: String): CachedTelemetryReading?

    @Query("SELECT * FROM cached_telemetry_readings WHERE deviceMac = :deviceMac ORDER BY recordedAtEpochMs DESC")
    suspend fun historyFor(deviceMac: String): List<CachedTelemetryReading>

    // When this device's newest stored reading was taken, so a new one can be skipped if it is too soon
    @Query("SELECT MAX(recordedAtEpochMs) FROM cached_telemetry_readings WHERE deviceMac = :deviceMac")
    suspend fun latestRecordedAt(deviceMac: String): Long?

    // One row per calendar day since the given moment, averaged in the phone's time zone
    @Query(
        "SELECT date(recordedAtEpochMs / 1000, 'unixepoch', 'localtime') AS day, " +
            "AVG(ph) AS avgPh, AVG(ec) AS avgEc, AVG(light) AS avgLight, " +
            "AVG(temperature) AS avgTemperature, AVG(humidity) AS avgHumidity, COUNT(*) AS samples " +
            "FROM cached_telemetry_readings " +
            "WHERE deviceMac = :deviceMac AND recordedAtEpochMs >= :sinceEpochMs " +
            "GROUP BY day ORDER BY day ASC"
    )
    suspend fun dailyAverages(deviceMac: String, sinceEpochMs: Long): List<DailyAverageRow>

    // Everything still waiting to reach the cloud, oldest first, ready for the sync worker
    @Query("SELECT * FROM cached_telemetry_readings WHERE isSynced = 0 ORDER BY recordedAtEpochMs ASC")
    suspend fun unsynced(): List<CachedTelemetryReading>

    @Query("UPDATE cached_telemetry_readings SET isSynced = 1 WHERE deviceMac = :deviceMac AND recordedAtEpochMs = :recordedAtEpochMs")
    suspend fun markSynced(deviceMac: String, recordedAtEpochMs: Long)

    // Only ever removes readings that have already reached the cloud, so an unsynced
    // reading is never lost just because it has been sitting on the phone for a while
    @Query("DELETE FROM cached_telemetry_readings WHERE isSynced = 1 AND recordedAtEpochMs < :beforeEpochMs")
    suspend fun pruneSyncedBefore(beforeEpochMs: Long)

    // The last resort when a phone has been offline for a very long time: the oldest readings
    // still waiting to reach the cloud are dropped so the database cannot grow without limit
    @Query("DELETE FROM cached_telemetry_readings WHERE isSynced = 0 AND recordedAtEpochMs < :beforeEpochMs")
    suspend fun pruneUnsyncedBefore(beforeEpochMs: Long)

    // Clears only what the cloud already has, so readings still waiting to be uploaded survive
    @Query("DELETE FROM cached_telemetry_readings WHERE isSynced = 1")
    suspend fun deleteSynced()

    @Query("DELETE FROM cached_telemetry_readings")
    suspend fun deleteAll()
}
