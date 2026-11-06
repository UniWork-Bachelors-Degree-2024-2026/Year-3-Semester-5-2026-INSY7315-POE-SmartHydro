package com.example.smarthydro.data.local

import androidx.room.Entity
import androidx.room.PrimaryKey

// One photo a farmer took or picked while using the app offline
@Entity(tableName = "cached_photos")
data class CachedPhoto(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    // A locally paired structure's id
    val structureId: String,
    val plant: String,
    val label: String,
    val cameraLabel: String,
    val isFavourite: Boolean,
    val takenAtEpochMs: Long,
    // The JPEG's name inside LocalDatabase.photosDir
    val fileName: String,
    val updatedAtEpochMs: Long,
    // Set when the photo is deleted
    val deletedAtEpochMs: Long? = null
)