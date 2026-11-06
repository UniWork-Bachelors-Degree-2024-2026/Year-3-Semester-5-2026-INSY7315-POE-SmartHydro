package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// Generic paginated response envelope
data class PagedResultDto<T>(
    @SerializedName("items") val items: List<T>,
    @SerializedName("page") val page: Int,
    @SerializedName("pageSize") val pageSize: Int,
    @SerializedName("totalCount") val totalCount: Int,
    @SerializedName("totalPages") val totalPages: Int
)

// Generic status and message response payload
data class SimpleMessageResponseDto(
    @SerializedName("message") val message: String? = null,
    @SerializedName("success") val success: Boolean? = null
)
