package com.example.smarthydro.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface AdminUserDao {

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(user: CachedAdminUser)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(users: List<CachedAdminUser>)

    @Query("SELECT * FROM cached_admin_users ORDER BY id ASC")
    suspend fun getAll(): List<CachedAdminUser>

    @Query("DELETE FROM cached_admin_users WHERE id = :id")
    suspend fun deleteById(id: Int)

    @Query("DELETE FROM cached_admin_users")
    suspend fun deleteAll()

    // Pending Operators
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertPendingAll(operators: List<CachedPendingOperator>)

    @Query("SELECT * FROM cached_pending_operators ORDER BY id DESC")
    suspend fun getPendingAll(): List<CachedPendingOperator>

    @Query("DELETE FROM cached_pending_operators WHERE id = :id")
    suspend fun deletePendingById(id: Int)

    @Query("DELETE FROM cached_pending_operators")
    suspend fun deleteAllPending()

    // Admin Sites
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertSitesAll(sites: List<CachedAdminSite>)

    @Query("SELECT * FROM cached_admin_sites ORDER BY name ASC")
    suspend fun getSitesAll(): List<CachedAdminSite>

    @Query("DELETE FROM cached_admin_sites WHERE id = :id")
    suspend fun deleteSiteById(id: String)

    @Query("DELETE FROM cached_admin_sites")
    suspend fun deleteAllSites()
}
