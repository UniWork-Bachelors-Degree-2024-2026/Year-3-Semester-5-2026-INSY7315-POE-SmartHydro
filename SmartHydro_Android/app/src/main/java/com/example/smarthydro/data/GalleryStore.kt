package com.example.smarthydro.data

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.models.CameraPhotoMetadataDto
import com.example.smarthydro.screens.extractAuthErrorMessage
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.File
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.ZonedDateTime

// One photo in the gallery, with its time already converted to the phone's time zone
data class GalleryCapture(
    val id: Long,
    val structureId: String?,
    val plant: String,
    val label: String,
    val camera: String,
    val isFavourite: Boolean,
    val takenAt: ZonedDateTime,
    val version: String,
    val localFile: File? = null
) {
    val title: String
        get() = plant.ifBlank { label }.ifBlank { "Photo" }

    val structureName: String
        get() = structureId?.let { StructureStore.find(it)?.title } ?: "Unassigned"

    val imageUrl: String
        get() = "${ApiClient.BASE_URL}Camera/photos/$id"
}

// Newest photo sent by camera
data class LatestPhotoResult(val capture: GalleryCapture?, val error: String? = null)

// Photos from every structure the user can see
object GalleryStore {

    private const val PAGE_SIZE = 100
    private const val MAX_PAGES = 50
    private const val MAX_PLANT_LENGTH = 60

    val captures = mutableStateListOf<GalleryCapture>()

    var isLoading by mutableStateOf(false)
        private set

    var loadError by mutableStateOf<String?>(null)
        private set

    // True when nobody is signed in
    val isOffline: Boolean
        get() = !ApiClient.isSignedIn()

    fun find(id: Long): GalleryCapture? = captures.find { it.id == id }

    // Loads every page of photos if online or the photos saved on this phone if offline
    suspend fun refresh() {
        isLoading = true
        loadError = null

        if (isOffline) {
            try {
                val saved = OfflineGallery.load()
                captures.clear()
                captures.addAll(saved)
            } catch (e: Exception) {
                loadError = "Couldn't load the photos saved on this phone."
            } finally {
                isLoading = false
            }
            return
        }

        try {
            val loaded = mutableListOf<GalleryCapture>()
            for (page in 1..MAX_PAGES) {
                val response = ApiClient.service.getGallery(page = page, pageSize = PAGE_SIZE)
                val body = response.body()
                if (!response.isSuccessful || body == null) {
                    if (page == 1) loadError = "Couldn't load photos. Please try again."
                    break
                }
                loaded.addAll(body.items.map { it.toGalleryCapture() })
                if (page >= body.totalPages) break
            }
            if (loadError == null) {
                captures.clear()
                captures.addAll(loaded.sortedByDescending { it.takenAt })
            }
        } catch (e: Exception) {
            loadError = "Couldn't reach the server. Check your connection and try again."
        } finally {
            isLoading = false
        }
    }

    // The newest photo taken by the camera
    suspend fun latestFromCamera(cameraMac: String): LatestPhotoResult {
        return try {
            val response = ApiClient.service.getGallery(deviceMac = cameraMac, page = 1, pageSize = 20)
            val body = response.body()
            if (response.isSuccessful && body != null) {
                LatestPhotoResult(body.items.map { it.toGalleryCapture() }.maxByOrNull { it.takenAt })
            } else {
                LatestPhotoResult(null, "Couldn't load the latest photo. Please try again.")
            }
        } catch (e: Exception) {
            LatestPhotoResult(null, "Couldn't reach the server. Check your connection and try again.")
        }
    }

    // Saves a new photo on a structure
    suspend fun upload(
        jpeg: ByteArray,
        structureId: String,
        plant: String,
        takenOn: LocalDate,
        favourite: Boolean,
        fromCamera: Boolean
    ): String? {
        val label = if (fromCamera) "Device camera photo" else "Uploaded photo"
        val cleanPlant = plant.trim().take(MAX_PLANT_LENGTH)

        if (isOffline) {
            return try {
                OfflineGallery.add(jpeg, structureId, cleanPlant, label, cameraName(fromCamera), capturedAtFor(takenOn), favourite)
                refresh()
                null
            } catch (e: Exception) {
                "The photo couldn't be saved on this phone. Check there's enough storage and try again."
            }
        }

        val apiStructureId = structureId.toIntOrNull() ?: return "Choose which structure this photo belongs to."

        return try {
            val response = ApiClient.service.uploadCameraPhoto(
                file = jpegPart(jpeg),
                deviceMac = "".asPart(),
                structureId = apiStructureId.toString().asPart(),
                plant = cleanPlant.asPart(),
                label = label.asPart(),
                cameraLabel = cameraName(fromCamera).asPart(),
                capturedAt = capturedAtFor(takenOn).toString().asPart(),
                isFavourite = favourite.toString().asPart()
            )
            if (response.isSuccessful) {
                refresh()
                null
            } else {
                saveErrorMessage(response.code(), response.errorBody()?.string())
            }
        } catch (e: Exception) {
            "Couldn't reach the server. Check your connection and try again."
        }
    }

    // Changes a photo's details and its image when a new one is given
    suspend fun update(
        id: Long,
        newJpeg: ByteArray?,
        newFromCamera: Boolean,
        structureId: String,
        plant: String,
        takenOn: LocalDate,
        favourite: Boolean
    ): String? {
        // Same day as before keeps the photo's original time of day
        val sameDay = find(id)?.takenAt?.toLocalDate() == takenOn
        val cleanPlant = plant.trim().take(MAX_PLANT_LENGTH)

        if (isOffline) {
            return try {
                val updated = OfflineGallery.update(
                    id = id,
                    newJpeg = newJpeg,
                    newCameraLabel = newJpeg?.let { cameraName(newFromCamera) },
                    structureId = structureId,
                    plant = cleanPlant,
                    takenAt = if (sameDay) null else capturedAtFor(takenOn),
                    favourite = favourite
                )
                if (updated) {
                    refresh()
                    null
                } else {
                    "That photo could not be found. It may have been deleted."
                }
            } catch (e: Exception) {
                "The photo couldn't be saved on this phone. Check there's enough storage and try again."
            }
        }

        val apiStructureId = structureId.toIntOrNull() ?: return "Choose which structure this photo belongs to."

        return try {
            val response = ApiClient.service.updatePhoto(
                id = id,
                file = newJpeg?.let { jpegPart(it) },
                structureId = apiStructureId.toString().asPart(),
                plant = cleanPlant.asPart(),
                cameraLabel = newJpeg?.let { cameraName(newFromCamera).asPart() },
                capturedAt = if (sameDay) null else capturedAtFor(takenOn).toString().asPart(),
                isFavourite = favourite.toString().asPart()
            )
            if (response.isSuccessful) {
                refresh()
                null
            } else if (response.code() == 404) {
                "That photo could not be found. It may have been deleted."
            } else {
                saveErrorMessage(response.code(), response.errorBody()?.string())
            }
        } catch (e: Exception) {
            "Couldn't reach the server. Check your connection and try again."
        }
    }

    // Flips a photo's favourite flag and returns false when unchanged
    suspend fun toggleFavourite(id: Long): Boolean {
        if (isOffline) {
            val current = find(id) ?: return false
            return try {
                val changed = OfflineGallery.setFavourite(id, !current.isFavourite)
                if (changed) setFavouriteInList(id, !current.isFavourite)
                changed
            } catch (e: Exception) {
                false
            }
        }

        return try {
            val response = ApiClient.service.togglePhotoFavourite(id)
            val body = response.body()
            if (response.isSuccessful && body != null) {
                setFavouriteInList(id, body.isFavourite)
                true
            } else {
                false
            }
        } catch (e: Exception) {
            false
        }
    }

    // Moves a photo to deleted items
    suspend fun delete(id: Long): Boolean {
        return try {
            val deleted = if (isOffline) {
                OfflineGallery.delete(id)
            } else {
                ApiClient.service.deletePhoto(id).isSuccessful
            }
            if (deleted) captures.removeAll { it.id == id }
            deleted
        } catch (e: Exception) {
            false
        }
    }

    // Brings a photo back from deleted items
    suspend fun restore(id: Long): Boolean {
        return try {
            val restored = if (isOffline) {
                OfflineGallery.restore(id)
            } else {
                ApiClient.service.restorePhoto(id).isSuccessful
            }
            if (restored) refresh()
            restored
        } catch (e: Exception) {
            false
        }
    }

    private fun setFavouriteInList(id: Long, favourite: Boolean) {
        val index = captures.indexOfFirst { it.id == id }
        if (index >= 0) captures[index] = captures[index].copy(isFavourite = favourite)
    }

    private fun String.asPart() = toRequestBody("text/plain".toMediaType())

    private fun jpegPart(jpeg: ByteArray) =
        MultipartBody.Part.createFormData("file", "photo.jpg", jpeg.toRequestBody("image/jpeg".toMediaType()))

    private fun cameraName(fromCamera: Boolean) = if (fromCamera) "Device camera" else "Uploaded"

    private fun saveErrorMessage(code: Int, errorBody: String?): String =
        extractAuthErrorMessage(errorBody) ?: when (code) {
            403 -> "You don't have access to that structure."
            413 -> "That photo is too large. The limit is 10 MB."
            else -> "The photo could not be saved. Try again."
        }

    private fun CameraPhotoMetadataDto.toGalleryCapture(): GalleryCapture = GalleryCapture(
        id = id,
        structureId = structureId?.toString(),
        plant = plant.orEmpty().trim(),
        label = label.orEmpty().trim(),
        camera = cameraLabel.orEmpty().trim(),
        isFavourite = isFavourite,
        takenAt = parseCapturedAt(capturedAt),
        version = "$imageSizeBytes-$capturedAt"
    )

    // The API sends UTC times, so both shapes are accepted
    private fun parseCapturedAt(value: String?): ZonedDateTime {
        val zone = ZoneId.systemDefault()
        if (value.isNullOrBlank()) return ZonedDateTime.now(zone)
        return try {
            OffsetDateTime.parse(value).atZoneSameInstant(zone)
        } catch (e: Exception) {
            try {
                LocalDateTime.parse(value).atZone(ZoneOffset.UTC).withZoneSameInstant(zone)
            } catch (e: Exception) {
                ZonedDateTime.now(zone)
            }
        }
    }

    // The chosen day with the current time of day
    private fun capturedAtFor(day: LocalDate): Instant {
        val zone = ZoneId.systemDefault()
        val now = ZonedDateTime.now(zone)
        val chosen = ZonedDateTime.of(day, LocalTime.now(zone), zone)
        return (if (chosen.isAfter(now)) now else chosen).toInstant()
    }
}