package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PhotoLibrary
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.data.GalleryCapture
import com.example.smarthydro.data.GalleryStore
import com.example.smarthydro.ui.theme.*
import java.time.Duration
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlinx.coroutines.launch

// Shows the newest photo a structure's camera has sent, with how long ago it was taken
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LatestPhotoSheet(
    structureName: String,
    cameraMac: String,
    onDismiss: () -> Unit,
    onOpenGallery: () -> Unit
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    var isLoading by remember { mutableStateOf(true) }
    var capture by remember { mutableStateOf<GalleryCapture?>(null) }
    var error by remember { mutableStateOf<String?>(null) }

    suspend fun load() {
        isLoading = true
        val result = GalleryStore.latestFromCamera(cameraMac)
        capture = result.capture
        error = result.error
        isLoading = false
    }

    LaunchedEffect(cameraMac) { load() }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = sheetState,
        containerColor = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp)
                .padding(bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            Column {
                Text(
                    text = "Latest photo",
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                )
                Text(
                    text = "From $structureName's camera",
                    fontSize = 13.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary
                )
            }

            // The photo or why there isn't one
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .aspectRatio(4f / 3f)
                    .clip(RoundedCornerShape(14.dp))
                    .background(if (isDark) SurfaceDarkVariant else MintBannerBg),
                contentAlignment = Alignment.Center
            ) {
                val shown = capture
                when {
                    isLoading -> CircularProgressIndicator(color = ForestGreen)
                    shown != null -> CaptureImage(
                        capture = shown,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize()
                    )
                    else -> Text(
                        text = error ?: "This camera hasn't sent a photo yet.",
                        fontSize = 14.sp,
                        textAlign = TextAlign.Center,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        modifier = Modifier.padding(24.dp)
                    )
                }
            }

            // When it was taken
            capture?.takenAt?.let { takenAt ->
                Text(
                    text = "Taken ${timeAgo(takenAt)} · ${takenAt.format(DateTimeFormatter.ofPattern("d MMM, HH:mm", Locale.ENGLISH))}",
                    fontSize = 13.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = if (isDark) TextPrimaryDark else TextPrimary
                )
            }

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                OutlinedButton(
                    onClick = { scope.launch { load() } },
                    enabled = !isLoading,
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp),
                    shape = RoundedCornerShape(12.dp)
                ) {
                    Icon(imageVector = Icons.Filled.Refresh, contentDescription = null, tint = ForestGreen, modifier = Modifier.size(18.dp))
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(text = "Refresh", color = ForestGreen, fontWeight = FontWeight.SemiBold)
                }

                Button(
                    onClick = onOpenGallery,
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = ForestGreen, contentColor = Color.White)
                ) {
                    Icon(imageVector = Icons.Filled.PhotoLibrary, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(text = "Open gallery", fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

private fun timeAgo(takenAt: ZonedDateTime): String {
    val minutes = Duration.between(takenAt, ZonedDateTime.now(takenAt.zone)).toMinutes().coerceAtLeast(0)
    return when {
        minutes < 1 -> "just now"
        minutes < 60 -> "$minutes minute${if (minutes == 1L) "" else "s"} ago"
        minutes < 60 * 24 -> (minutes / 60).let { "$it hour${if (it == 1L) "" else "s"} ago" }
        else -> (minutes / (60 * 24)).let { "$it day${if (it == 1L) "" else "s"} ago" }
    }
}