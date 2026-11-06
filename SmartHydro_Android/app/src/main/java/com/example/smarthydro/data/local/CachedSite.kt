package com.example.smarthydro.data.local

import androidx.room.Entity
import androidx.room.PrimaryKey

// Cached site record so sites load instantly (0ms) alongside cached structures
@Entity(tableName = "cached_sites")
data class CachedSite(
    @PrimaryKey val id: String,
    val name: String
)
