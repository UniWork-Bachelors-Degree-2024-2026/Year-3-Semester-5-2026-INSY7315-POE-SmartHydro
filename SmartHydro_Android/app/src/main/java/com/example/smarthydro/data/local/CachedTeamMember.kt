package com.example.smarthydro.data.local

import androidx.room.Entity
import com.example.smarthydro.models.TeamMemberDto

// Cached team member / invitation record so TeamScreen loads instantly (0ms)
@Entity(
    tableName = "cached_team_members",
    primaryKeys = ["siteId", "email"]
)
data class CachedTeamMember(
    val siteId: String,
    val userId: Int?,
    val siteName: String,
    val email: String,
    val fullName: String?,
    val status: String,
    val since: String,
    val expiresAt: String?
) {
    fun toDto(): TeamMemberDto = TeamMemberDto(
        siteId = siteId,
        userId = userId,
        siteName = siteName,
        email = email,
        fullName = fullName,
        status = status,
        since = since,
        expiresAt = expiresAt
    )

    companion object {
        fun fromDto(dto: TeamMemberDto): CachedTeamMember = CachedTeamMember(
            siteId = dto.siteId,
            userId = dto.userId,
            siteName = dto.siteName,
            email = dto.email,
            fullName = dto.fullName,
            status = dto.status,
            since = dto.since,
            expiresAt = dto.expiresAt
        )
    }
}
