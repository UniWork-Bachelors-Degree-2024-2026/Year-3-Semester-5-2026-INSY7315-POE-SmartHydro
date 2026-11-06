package com.example.smarthydro.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface StructureDao {

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(structure: CachedStructure)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(structures: List<CachedStructure>)

    @Query("SELECT * FROM cached_structures")
    suspend fun getAll(): List<CachedStructure>

    @Query("DELETE FROM cached_structures WHERE id = :id")
    suspend fun deleteById(id: String)

    @Query("DELETE FROM cached_structures")
    suspend fun deleteAll()
}
