package com.example.smarthydro.data

import com.example.smarthydro.data.local.CachedPhoto
import com.example.smarthydro.data.local.LocalDatabase
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.util.UUID

// The gallery for someone using the app without an account, photos stay on this phone only
internal object OfflineGallery {

    // Undo can bring a deleted photo back, it's removed once it has been deleted for longer than this
    private const val KEEP_DELETED_MS = 60 * 60 * 1000L

    private val dao get() = LocalDatabase.photoDao

    // Every photo still in the gallery
    suspend fun load(): List<GalleryCapture> {
        removeOldDeleted()
        return dao.getAll().map { it.toGalleryCapture() }
    }

    suspend fun add(
        jpeg: ByteArray,
        structureId: String,
        plant: String,
        label: String,
        cameraLabel: String,
        takenAt: Instant,
        favourite: Boolean
    ) {
        val now = System.currentTimeMillis()
        dao.insert(
            CachedPhoto(
                structureId = structureId,
                plant = plant,
                label = label,
                cameraLabel = cameraLabel,
                isFavourite = favourite,
                takenAtEpochMs = takenAt.toEpochMilli(),
                fileName = saveFile(jpeg),
                updatedAtEpochMs = now
            )
        )
    }

    // Changes a photo's details and its image when a new one is given
    suspend fun update(
        id: Long,
        newJpeg: ByteArray?,
        newCameraLabel: String?,
        structureId: String,
        plant: String,
        takenAt: Instant?,
        favourite: Boolean
    ): Boolean {
        val existing = dao.getById(id) ?: return false
        if (existing.deletedAtEpochMs != null) return false

        // The new image is saved before the old one is removed
        val fileName = if (newJpeg != null) {
            saveFile(newJpeg).also { deleteFile(existing.fileName) }
        } else {
            existing.fileName
        }

        dao.update(
            existing.copy(
                structureId = structureId,
                plant = plant,
                cameraLabel = newCameraLabel ?: existing.cameraLabel,
                isFavourite = favourite,
                takenAtEpochMs = takenAt?.toEpochMilli() ?: existing.takenAtEpochMs,
                fileName = fileName,
                updatedAtEpochMs = System.currentTimeMillis()
            )
        )
        return true
    }

    suspend fun setFavourite(id: Long, favourite: Boolean): Boolean =
        dao.setFavourite(id, favourite, System.currentTimeMillis()) > 0

    // Hides the photo but keeps its file, so Undo can still bring it back
    suspend fun delete(id: Long): Boolean =
        dao.markDeleted(id, System.currentTimeMillis()) > 0

    suspend fun restore(id: Long): Boolean =
        dao.restore(id) > 0

    // Removes photos deleted more than an hour ago
    private suspend fun removeOldDeleted() {
        val old = dao.getDeletedBefore(System.currentTimeMillis() - KEEP_DELETED_MS)
        if (old.isEmpty()) return
        old.forEach { deleteFile(it.fileName) }
        dao.deleteByIds(old.map { it.id })
    }

    // A random name
    private suspend fun saveFile(jpeg: ByteArray): String = withContext(Dispatchers.IO) {
        val name = "${UUID.randomUUID()}.jpg"
        File(LocalDatabase.photosDir, name).writeBytes(jpeg)
        name
    }

    private suspend fun deleteFile(fileName: String) = withContext(Dispatchers.IO) {
        File(LocalDatabase.photosDir, fileName).delete()
    }

    private fun CachedPhoto.toGalleryCapture(): GalleryCapture = GalleryCapture(
        id = id,
        structureId = structureId,
        plant = plant,
        label = label,
        camera = cameraLabel,
        isFavourite = isFavourite,
        takenAt = Instant.ofEpochMilli(takenAtEpochMs).atZone(ZoneId.systemDefault()),
        version = updatedAtEpochMs.toString(),
        localFile = File(LocalDatabase.photosDir, fileName)
    )
}