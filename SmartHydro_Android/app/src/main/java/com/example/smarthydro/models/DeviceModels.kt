package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 3. DEVICES MODELS (/api/Devices)

// DTO representing registered IoT hardware device information
data class DeviceDto(
    @SerializedName("macAddress") val macAddress: String,
    @SerializedName("structureId") val structureId: Int?,
    @SerializedName("deviceName") val deviceName: String,
    @SerializedName("deviceType") val deviceType: String,
    @SerializedName("structureType") val structureType: String?,
    @SerializedName("localIp") val localIp: String?,
    @SerializedName("isOnline") val isOnline: Boolean,
    @SerializedName("isOfflineMode") val isOfflineMode: Boolean,
    @SerializedName("pendingOfflineTransition") val pendingOfflineTransition: Boolean,
    @SerializedName("lastSeenAt") val lastSeenAt: String,
    @SerializedName("createdAt") val createdAt: String
)

// Request payload for registering a new IoT device node
data class CreateDeviceDto(
    @SerializedName("macAddress") val macAddress: String,
    @SerializedName("deviceName") val deviceName: String,
    @SerializedName("deviceType") val deviceType: String = "SENSOR-NODE",
    @SerializedName("structureType") val structureType: String? = null,
    @SerializedName("structureId") val structureId: Int? = null,
    @SerializedName("localIp") val localIp: String? = null
)

// Request payload for updating existing device metadata
data class UpdateDeviceDto(
    @SerializedName("deviceName") val deviceName: String? = null,
    @SerializedName("deviceType") val deviceType: String? = null,
    @SerializedName("structureType") val structureType: String? = null,
    @SerializedName("structureId") val structureId: Int? = null,
    @SerializedName("localIp") val localIp: String? = null
)

// Request payload for renaming a device display name
data class RenameDeviceDto(
    @SerializedName("name") val name: String
)

// Request payload for assigning or unassigning a device to a structure
data class AssignStructureDto(
    @SerializedName("structureId") val structureId: Int?
)

// Request payload for changing structure profile between VERTICAL-TOWER and FODDER-SYSTEM
data class SetStructureTypeDto(
    @SerializedName("structureType") val structureType: String
)
