package com.example.smarthydro.data.local

import android.content.Context
import java.io.File

// Single entry point to the on-device database, initialised once at app start
// (see MainActivity), the same way ApiClient is set up.
object LocalDatabase {

    private var database: AppDatabase? = null

    private var photoFolder: File? = null

    fun init(context: Context) {
        database = AppDatabase.getInstance(context)
        photoFolder = File(context.filesDir, "photos").apply { mkdirs() }
    }

    val telemetryDao: TelemetryDao
        get() = requireNotNull(database) { "LocalDatabase.init() must be called before use" }.telemetryDao()

    val structureDao: StructureDao
        get() = requireNotNull(database) { "LocalDatabase.init() must be called before use" }.structureDao()

    val siteDao: SiteDao
        get() = requireNotNull(database) { "LocalDatabase.init() must be called before use" }.siteDao()

    val adminUserDao: AdminUserDao
        get() = requireNotNull(database) { "LocalDatabase.init() must be called before use" }.adminUserDao()

    val teamDao: TeamDao
        get() = requireNotNull(database) { "LocalDatabase.init() must be called before use" }.teamDao()

    val photoDao: PhotoDao
        get() = requireNotNull(database) { "LocalDatabase.init() must be called before use" }.photoDao()

    val photosDir: File
        get() = requireNotNull(photoFolder) { "LocalDatabase.init() must be called before use" }
}