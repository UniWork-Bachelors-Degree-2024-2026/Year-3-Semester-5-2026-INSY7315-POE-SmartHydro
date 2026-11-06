package com.example.smarthydro.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface TeamDao {

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(member: CachedTeamMember)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(members: List<CachedTeamMember>)

    @Query("SELECT * FROM cached_team_members ORDER BY siteName ASC, email ASC")
    suspend fun getAll(): List<CachedTeamMember>

    @Query("DELETE FROM cached_team_members WHERE siteId = :siteId AND email = :email")
    suspend fun deleteBySiteAndEmail(siteId: String, email: String)

    @Query("DELETE FROM cached_team_members WHERE siteId = :siteId AND userId = :userId")
    suspend fun deleteBySiteAndUserId(siteId: String, userId: Int)

    @Query("DELETE FROM cached_team_members")
    suspend fun deleteAll()
}
