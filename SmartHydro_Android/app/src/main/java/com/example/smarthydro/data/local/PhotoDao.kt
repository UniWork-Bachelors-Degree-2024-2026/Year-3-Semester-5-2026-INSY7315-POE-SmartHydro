package com.example.smarthydro.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import androidx.room.Update

// Reads and writes the photos a farmer keeps on the phone
@Dao
interface PhotoDao {

    // Every photo still in the gallery
    @Query("SELECT * FROM cached_photos WHERE deletedAtEpochMs IS NULL ORDER BY takenAtEpochMs DESC")
    suspend fun getAll(): List<CachedPhoto>

    @Query("SELECT * FROM cached_photos WHERE id = :id")
    suspend fun getById(id: Long): CachedPhoto?

    // Returns the new photo's id
    @Insert
    suspend fun insert(photo: CachedPhoto): Long

    @Update
    suspend fun update(photo: CachedPhoto)

    // Each of these returns how many rows changed
    @Query("UPDATE cached_photos SET isFavourite = :favourite, updatedAtEpochMs = :now WHERE id = :id AND deletedAtEpochMs IS NULL")
    suspend fun setFavourite(id: Long, favourite: Boolean, now: Long): Int

    @Query("UPDATE cached_photos SET deletedAtEpochMs = :now WHERE id = :id AND deletedAtEpochMs IS NULL")
    suspend fun markDeleted(id: Long, now: Long): Int

    @Query("UPDATE cached_photos SET deletedAtEpochMs = NULL WHERE id = :id AND deletedAtEpochMs IS NOT NULL")
    suspend fun restore(id: Long): Int

    // Photos deleted long enough ago that Undo is no longer offered
    @Query("SELECT * FROM cached_photos WHERE deletedAtEpochMs IS NOT NULL AND deletedAtEpochMs < :before")
    suspend fun getDeletedBefore(before: Long): List<CachedPhoto>

    @Query("DELETE FROM cached_photos WHERE id IN (:ids)")
    suspend fun deleteByIds(ids: List<Long>)
}