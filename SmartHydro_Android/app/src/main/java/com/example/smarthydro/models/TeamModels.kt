package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// One worker or pending invitation on a site the signed-in Operator manages
data class TeamMemberDto(
    @SerializedName("siteId") val siteId: String,
    @SerializedName("userId") val userId: Int?,
    @SerializedName("siteName") val siteName: String,
    @SerializedName("email") val email: String,
    @SerializedName("fullName") val fullName: String?,
    @SerializedName("status") val status: String,
    @SerializedName("since") val since: String,
    @SerializedName("expiresAt") val expiresAt: String?
)

// Request payload for inviting a worker to a site
data class CreateSiteInviteDto(
    @SerializedName("email") val email: String
)

data class SiteInviteResponseDto(
    @SerializedName("message") val message: String,
    @SerializedName("expiresAt") val expiresAt: String
)
