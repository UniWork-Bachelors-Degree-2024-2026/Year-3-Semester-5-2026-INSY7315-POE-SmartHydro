package com.example.smarthydro.components

import android.content.Context
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.ImageLoader
import coil.compose.SubcomposeAsyncImage
import coil.request.ImageRequest
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.GalleryCapture
import com.example.smarthydro.ui.theme.*

// Shows a gallery photo from the API or offline
@Composable
fun CaptureImage(
    capture: GalleryCapture,
    contentScale: ContentScale,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val source = if (capture.localFile != null) "local" else "api"
    val cacheKey = "capture-$source-${capture.id}-${capture.version}"

    val request = remember(cacheKey) {
        ImageRequest.Builder(context)
            .data(capture.localFile ?: capture.imageUrl)
            .memoryCacheKey(cacheKey)
            .diskCacheKey(cacheKey)
            .crossfade(true)
            .build()
    }

    SubcomposeAsyncImage(
        model = request,
        imageLoader = remember { GalleryImageLoader.get(context) },
        contentDescription = capture.title,
        contentScale = contentScale,
        modifier = modifier,
        loading = {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(
                    modifier = Modifier.size(22.dp),
                    color = ForestGreen,
                    strokeWidth = 2.dp
                )
            }
        },
        error = {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text(
                    text = "Image unavailable",
                    fontSize = 11.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary
                )
            }
        }
    )
}

// One image loader for the whole app
private object GalleryImageLoader {
    @Volatile
    private var instance: ImageLoader? = null

    fun get(context: Context): ImageLoader =
        instance ?: synchronized(this) {
            instance ?: ImageLoader.Builder(context.applicationContext)
                .okHttpClient(ApiClient.okHttpClient)
                // The API sends no cache headers
                .respectCacheHeaders(false)
                .build()
                .also { instance = it }
        }
}