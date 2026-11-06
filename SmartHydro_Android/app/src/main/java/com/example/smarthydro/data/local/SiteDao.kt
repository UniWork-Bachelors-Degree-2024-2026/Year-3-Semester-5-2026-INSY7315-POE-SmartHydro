package com.example.smarthydro.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface SiteDao {

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(site: CachedSite)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(sites: List<CachedSite>)

    @Query("SELECT * FROM cached_sites ORDER BY name ASC")
    suspend fun getAll(): List<CachedSite>

    @Query("DELETE FROM cached_sites WHERE id = :id")
    suspend fun deleteById(id: String)

    @Query("DELETE FROM cached_sites")
    suspend fun deleteAll()
}
