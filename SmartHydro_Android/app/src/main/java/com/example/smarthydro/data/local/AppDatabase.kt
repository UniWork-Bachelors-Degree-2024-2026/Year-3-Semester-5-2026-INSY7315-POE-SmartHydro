package com.example.smarthydro.data.local

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase

@Database(
    entities = [
        CachedTelemetryReading::class,
        CachedStructure::class,
        CachedSite::class,
        CachedAdminUser::class,
        CachedPendingOperator::class,
        CachedAdminSite::class,
        CachedTeamMember::class,
        CachedPhoto::class
    ],
    version = 10,
    exportSchema = true
)
abstract class AppDatabase : RoomDatabase() {

    abstract fun telemetryDao(): TelemetryDao
    abstract fun structureDao(): StructureDao
    abstract fun siteDao(): SiteDao
    abstract fun adminUserDao(): AdminUserDao
    abstract fun teamDao(): TeamDao
    abstract fun photoDao(): PhotoDao

    companion object {
        // 7 -> 8: a sensor that gave no reading can now be stored as null. SQLite cannot loosen a
        // column's NOT NULL in place, so the readings table is rebuilt and any rows already in it
        // are carried across. Nothing else in the database is touched, so structures a farmer
        // created offline are kept.
        private val MIGRATION_7_8 = object : Migration(7, 8) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS `cached_telemetry_readings_new` (`deviceMac` TEXT NOT NULL, `recordedAtEpochMs` INTEGER NOT NULL, `ph` REAL, `ec` REAL, `temperature` REAL, `humidity` REAL, `light` REAL, `waterLevelRaw` INTEGER NOT NULL, `waterLevelDetected` INTEGER NOT NULL, `mlPh` INTEGER NOT NULL, `mlEc` INTEGER NOT NULL, `mlTemp` INTEGER NOT NULL, `mlHumidity` INTEGER NOT NULL, `isSynced` INTEGER NOT NULL, PRIMARY KEY(`deviceMac`, `recordedAtEpochMs`))")
                db.execSQL("INSERT INTO cached_telemetry_readings_new (deviceMac, recordedAtEpochMs, ph, ec, temperature, humidity, light, waterLevelRaw, waterLevelDetected, mlPh, mlEc, mlTemp, mlHumidity, isSynced) SELECT deviceMac, recordedAtEpochMs, ph, ec, temperature, humidity, light, waterLevelRaw, waterLevelDetected, mlPh, mlEc, mlTemp, mlHumidity, isSynced FROM cached_telemetry_readings")
                db.execSQL("DROP TABLE cached_telemetry_readings")
                db.execSQL("ALTER TABLE cached_telemetry_readings_new RENAME TO cached_telemetry_readings")
            }
        }

        // 8 -> 9: adds the table for photos a farmer keeps on the phone while offline
        // It's a new table, so every existing table and row is left exactly as it was
        private val MIGRATION_8_9 = object : Migration(8, 9) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS `cached_photos` (`id` INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, `structureId` TEXT NOT NULL, `plant` TEXT NOT NULL, `label` TEXT NOT NULL, `cameraLabel` TEXT NOT NULL, `isFavourite` INTEGER NOT NULL, `takenAtEpochMs` INTEGER NOT NULL, `fileName` TEXT NOT NULL, `updatedAtEpochMs` INTEGER NOT NULL, `deletedAtEpochMs` INTEGER)")
            }
        }

        // 9 -> 10: adds the box's Bluetooth address to a structure a farmer paired offline. It's one new column that
        // can be empty, so every existing row keeps its values and simply has no Bluetooth address yet
        private val MIGRATION_9_10 = object : Migration(9, 10) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE `cached_structures` ADD COLUMN `boxBluetoothAddress` TEXT")
            }
        }

        @Volatile
        private var instance: AppDatabase? = null

        fun getInstance(context: Context): AppDatabase =
            instance ?: synchronized(this) {
                instance ?: Room.databaseBuilder(
                    context.applicationContext,
                    AppDatabase::class.java,
                    "smarthydro.db"
                )
                    // Upgrades from version 7 onward keep their data. Anything older has no
                    // migration path, so it is wiped rather than crashing on the mismatch
                    .addMigrations(MIGRATION_7_8, MIGRATION_8_9, MIGRATION_9_10)
                    .fallbackToDestructiveMigration()
                    .build().also { instance = it }
            }
    }
}