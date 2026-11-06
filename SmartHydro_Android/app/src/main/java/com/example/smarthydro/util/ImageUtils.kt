package com.example.smarthydro.util

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.media.ExifInterface
import android.net.Uri
import java.io.ByteArrayOutputStream
import java.io.InputStream

// A photo taken or picked on the phone
class PreparedPhoto(
    val jpeg: ByteArray,
    val preview: Bitmap,
    val fromCamera: Boolean
)

// Gets photos from the phone ready to upload, Shrinking them keeps uploads under the 10 MB limit
object ImageUtils {

    private const val MAX_SIDE = 1920
    private const val JPEG_QUALITY = 90

    // A photo from the camera
    fun fromCamera(bitmap: Bitmap): PreparedPhoto {
        val scaled = scaleToFit(bitmap)
        return PreparedPhoto(toJpeg(scaled), scaled, fromCamera = true)
    }

    // A photo picked from the phone's library
    fun fromUri(context: Context, uri: Uri): PreparedPhoto? {
        return try {
            val resolver = context.contentResolver

            // Read the size first so a huge photo is decoded at a smaller size straight away
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null

            val options = BitmapFactory.Options().apply {
                inSampleSize = sampleSizeFor(bounds.outWidth, bounds.outHeight)
            }
            val decoded = resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, options) }
                ?: return null

            // Phones often store photos sideway
            val rotation = resolver.openInputStream(uri)?.use { exifRotation(it) } ?: 0
            val upright = if (rotation != 0) {
                val matrix = Matrix().apply { postRotate(rotation.toFloat()) }
                Bitmap.createBitmap(decoded, 0, 0, decoded.width, decoded.height, matrix, true)
            } else {
                decoded
            }

            val scaled = scaleToFit(upright)
            PreparedPhoto(toJpeg(scaled), scaled, fromCamera = false)
        } catch (e: Exception) {
            null
        }
    }

    private fun sampleSizeFor(width: Int, height: Int): Int {
        var sample = 1
        while (maxOf(width, height) / (sample * 2) >= MAX_SIDE) sample *= 2
        return sample
    }

    private fun scaleToFit(bitmap: Bitmap): Bitmap {
        val longest = maxOf(bitmap.width, bitmap.height)
        if (longest <= MAX_SIDE) return bitmap
        val scale = MAX_SIDE / longest.toFloat()
        return Bitmap.createScaledBitmap(
            bitmap,
            (bitmap.width * scale).toInt().coerceAtLeast(1),
            (bitmap.height * scale).toInt().coerceAtLeast(1),
            true
        )
    }

    private fun toJpeg(bitmap: Bitmap): ByteArray {
        val output = ByteArrayOutputStream()
        bitmap.compress(Bitmap.CompressFormat.JPEG, JPEG_QUALITY, output)
        return output.toByteArray()
    }

    private fun exifRotation(input: InputStream): Int {
        return try {
            when (ExifInterface(input).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)) {
                ExifInterface.ORIENTATION_ROTATE_90 -> 90
                ExifInterface.ORIENTATION_ROTATE_180 -> 180
                ExifInterface.ORIENTATION_ROTATE_270 -> 270
                else -> 0
            }
        } catch (e: Exception) {
            0
        }
    }
}