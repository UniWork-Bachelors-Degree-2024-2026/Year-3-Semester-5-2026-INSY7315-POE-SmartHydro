package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 11. SITES MODELS (/api/Sites)

// Details of a physical farm installation site with aggregated structure count
data class SiteDto(
    @SerializedName("id") val id: String,
    @SerializedName("name") val name: String,
    @SerializedName("location") val location: String?,
    @SerializedName("createdAt") val createdAt: String,
    @SerializedName("structureCount") val structureCount: Int
)

// Request payload for creating a new installation site
data class CreateSiteDto(
    @SerializedName("id") val id: String? = null,
    @SerializedName("name") val name: String,
    @SerializedName("location") val location: String? = null
)

// Request payload for updating an existing site
data class UpdateSiteDto(
    @SerializedName("name") val name: String,
    @SerializedName("location") val location: String? = null
)

