package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// Models matching SmartHydroAPI.Models.Dtos for Admin management

data class AdminUserDto(
    @SerializedName("id") val id: Int,
    @SerializedName("email") val email: String,
    @SerializedName("fullName") val fullName: String? = null,
    @SerializedName("farmName") val farmName: String? = null,
    @SerializedName("role") val role: String,
    @SerializedName("status") val status: String,
    @SerializedName("isVerified") val isVerified: Boolean = false,
    @SerializedName("createdAt") val createdAt: String? = null,
    @SerializedName("lastLoginAt") val lastLoginAt: String? = null,
    @SerializedName("ownedSiteCount") val ownedSiteCount: Int = 0,
    @SerializedName("assignedSites") val assignedSites: List<String> = emptyList(),
    @SerializedName("isProtected") val isProtected: Boolean = false,
    @SerializedName("isCurrentUser") val isCurrentUser: Boolean = false,
    @SerializedName("allowedActions") val allowedActions: List<String> = emptyList(),
    @SerializedName("statusOptions") val statusOptions: List<StatusOptionDto> = emptyList()
)

data class StatusOptionDto(
    @SerializedName("status") val status: String,
    @SerializedName("action") val action: String
)

data class PendingOperatorDto(
    @SerializedName("id") val id: Int,
    @SerializedName("email") val email: String,
    @SerializedName("farmName") val farmName: String? = null,
    @SerializedName("role") val role: String,
    @SerializedName("status") val status: String,
    @SerializedName("createdAt") val createdAt: String? = null
)

data class OperatorReviewResponseDto(
    @SerializedName("message") val message: String,
    @SerializedName("id") val id: Int,
    @SerializedName("email") val email: String,
    @SerializedName("status") val status: String,
    @SerializedName("notificationSent") val notificationSent: Boolean
)

data class AdminSiteDto(
    @SerializedName("id") val id: String,
    @SerializedName("name") val name: String,
    @SerializedName("location") val location: String? = null,
    @SerializedName("ownerEmail") val ownerEmail: String? = null,
    @SerializedName("createdAt") val createdAt: String? = null,
    @SerializedName("structureCount") val structureCount: Int = 0,
    @SerializedName("workerCount") val workerCount: Int = 0
)

data class UpdateUserDto(
    @SerializedName("fullName") val fullName: String?,
    @SerializedName("farmName") val farmName: String?,
    @SerializedName("role") val role: String
)

data class AssignSiteOwnerDto(
    @SerializedName("ownerEmail") val ownerEmail: String
)

data class AccountActionResponseDto(
    @SerializedName("message") val message: String,
    @SerializedName("id") val id: Int? = null,
    @SerializedName("status") val status: String? = null
)
