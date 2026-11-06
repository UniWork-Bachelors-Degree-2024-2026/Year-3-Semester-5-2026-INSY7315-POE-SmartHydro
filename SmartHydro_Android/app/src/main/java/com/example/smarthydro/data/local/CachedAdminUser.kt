package com.example.smarthydro.data.local

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.models.AdminUserDto
import com.example.smarthydro.models.PendingOperatorDto
import com.example.smarthydro.models.StatusOptionDto
import com.google.gson.reflect.TypeToken

// Cached admin user record so Admin Console displays instant results (0ms)
@Entity(tableName = "cached_admin_users")
data class CachedAdminUser(
    @PrimaryKey val id: Int,
    val email: String,
    val fullName: String?,
    val farmName: String?,
    val role: String,
    val status: String,
    val isVerified: Boolean,
    val createdAt: String?,
    val lastLoginAt: String?,
    val ownedSiteCount: Int,
    val assignedSitesJson: String,
    val isProtected: Boolean,
    val allowedActionsJson: String,
    val statusOptionsJson: String
) {
    fun toAdminUserDto(): AdminUserDto {
        val stringListType = object : TypeToken<List<String>>() {}.type
        val statusOptionListType = object : TypeToken<List<StatusOptionDto>>() {}.type

        val sites: List<String> = try {
            ApiClient.gson.fromJson(assignedSitesJson, stringListType) ?: emptyList()
        } catch (_: Exception) {
            emptyList()
        }

        val actions: List<String> = try {
            ApiClient.gson.fromJson(allowedActionsJson, stringListType) ?: emptyList()
        } catch (_: Exception) {
            emptyList()
        }

        val options: List<StatusOptionDto> = try {
            ApiClient.gson.fromJson(statusOptionsJson, statusOptionListType) ?: emptyList()
        } catch (_: Exception) {
            emptyList()
        }

        return AdminUserDto(
            id = id,
            email = email,
            fullName = fullName,
            farmName = farmName,
            role = role,
            status = status,
            isVerified = isVerified,
            createdAt = createdAt,
            lastLoginAt = lastLoginAt,
            ownedSiteCount = ownedSiteCount,
            assignedSites = sites,
            isProtected = isProtected,
            allowedActions = actions,
            statusOptions = options
        )
    }

    companion object {
        fun fromDto(dto: AdminUserDto): CachedAdminUser {
            return CachedAdminUser(
                id = dto.id,
                email = dto.email,
                fullName = dto.fullName,
                farmName = dto.farmName,
                role = dto.role,
                status = dto.status,
                isVerified = dto.isVerified,
                createdAt = dto.createdAt,
                lastLoginAt = dto.lastLoginAt,
                ownedSiteCount = dto.ownedSiteCount,
                assignedSitesJson = ApiClient.gson.toJson(dto.assignedSites),
                isProtected = dto.isProtected,
                allowedActionsJson = ApiClient.gson.toJson(dto.allowedActions),
                statusOptionsJson = ApiClient.gson.toJson(dto.statusOptions)
            )
        }
    }
}

// Cached pending operator approvals
@Entity(tableName = "cached_pending_operators")
data class CachedPendingOperator(
    @PrimaryKey val id: Int,
    val email: String,
    val farmName: String?,
    val role: String,
    val status: String,
    val createdAt: String?
) {
    fun toPendingOperatorDto(): PendingOperatorDto = PendingOperatorDto(
        id = id,
        email = email,
        farmName = farmName,
        role = role,
        status = status,
        createdAt = createdAt
    )

    companion object {
        fun fromDto(dto: PendingOperatorDto): CachedPendingOperator = CachedPendingOperator(
            id = dto.id,
            email = dto.email,
            farmName = dto.farmName,
            role = dto.role,
            status = dto.status,
            createdAt = dto.createdAt
        )
    }
}

// Cached admin farm sites for AdminSitesScreen & AdminDashboard
@Entity(tableName = "cached_admin_sites")
data class CachedAdminSite(
    @PrimaryKey val id: String,
    val name: String,
    val location: String?,
    val ownerEmail: String?,
    val createdAt: String?,
    val structureCount: Int,
    val workerCount: Int
) {
    fun toAdminSiteDto(): com.example.smarthydro.models.AdminSiteDto = com.example.smarthydro.models.AdminSiteDto(
        id = id,
        name = name,
        location = location,
        ownerEmail = ownerEmail,
        createdAt = createdAt,
        structureCount = structureCount,
        workerCount = workerCount
    )

    companion object {
        fun fromDto(dto: com.example.smarthydro.models.AdminSiteDto): CachedAdminSite = CachedAdminSite(
            id = dto.id,
            name = dto.name,
            location = dto.location,
            ownerEmail = dto.ownerEmail,
            createdAt = dto.createdAt,
            structureCount = dto.structureCount,
            workerCount = dto.workerCount
        )
    }
}

