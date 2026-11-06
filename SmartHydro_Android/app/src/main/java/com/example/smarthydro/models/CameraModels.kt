package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 8. CAMERA MODELS (/api/Camera)

// Request payload for triggering an on-demand camera capture on an ESP32-CAM node
data class CameraTriggerRequestDto(
    @SerializedName("deviceMac") val deviceMac: String
)

// Response received by an ESP32-CAM when polling for pending capture commands
data class CameraPendingResponseDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("capturePending") val capturePending: Boolean,
    @SerializedName("requestedAt") val requestedAt: String
)

// Response returned after successfully uploading a photo frame
data class CameraUploadResponseDto(
    @SerializedName("success") val success: Boolean,
    @SerializedName("captureId") val captureId: Long,
    @SerializedName("size") val size: Long
)

// Photo metadata information for gallery display
data class CameraPhotoMetadataDto(
    @SerializedName("id") val id: Long,
    @SerializedName("deviceMac") val deviceMac: String?,
    @SerializedName("structureId") val structureId: Int?,
    @SerializedName("plant") val plant: String?,
    @SerializedName("label") val label: String?,
    @SerializedName("cameraLabel") val cameraLabel: String?,
    @SerializedName("isFavourite") val isFavourite: Boolean,
    @SerializedName("capturedAt") val capturedAt: String,
    @SerializedName("imageSizeBytes") val imageSizeBytes: Int = 0,
    @SerializedName("contentType") val contentType: String? = null
)

// Response returned after toggling favourite status of a photo
data class ToggleFavouriteResponseDto(
    @SerializedName("id") val id: Long,
    @SerializedName("isFavourite") val isFavourite: Boolean
)

// Request payload for initiating or stopping an ephemeral live camera stream
data class CameraStreamRequestDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("timeoutSeconds") val timeoutSeconds: Int = 90
)

// Response detailing active camera stream configuration and connection parameters
data class CameraStreamStatusDto(
    @SerializedName("deviceMac") val deviceMac: String,
    @SerializedName("streamPort") val streamPort: Int = 81,
    @SerializedName("streamPath") val streamPath: String = "/stream",
    @SerializedName("defaultTimeoutSeconds") val defaultTimeoutSeconds: Int = 90
)
