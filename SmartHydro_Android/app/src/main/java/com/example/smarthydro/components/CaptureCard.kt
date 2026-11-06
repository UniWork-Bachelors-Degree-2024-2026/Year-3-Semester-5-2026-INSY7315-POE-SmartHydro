package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.data.GalleryCapture
import com.example.smarthydro.ui.theme.*
import java.time.LocalDate
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

// Star colour shared by the gallery's favourite controls
internal val FavouriteGold = Color(0xFFE0A800)

// A square photo tile in the gallery grid, with its name and when it was taken
@Composable
fun CaptureCard(
    capture: GalleryCapture,
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current

    Column(
        modifier = modifier
            .clip(RoundedCornerShape(16.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp))
            .background(MaterialTheme.colorScheme.surface)
            .clickable(onClick = onClick)
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .aspectRatio(1f)
                .background(ForestGreen.copy(alpha = if (isDark) 0.12f else 0.06f))
        ) {
            CaptureImage(
                capture = capture,
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxSize()
            )

            if (capture.isFavourite) {
                Box(
                    modifier = Modifier
                        .align(Alignment.TopEnd)
                        .padding(8.dp)
                        .size(28.dp)
                        .clip(CircleShape)
                        .background(Color.Black.copy(alpha = 0.55f)),
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        imageVector = Icons.Filled.Star,
                        contentDescription = "Favourite",
                        tint = Color(0xFFFFC94D),
                        modifier = Modifier.size(16.dp)
                    )
                }
            }
        }

        Column(modifier = Modifier.padding(horizontal = 10.dp, vertical = 8.dp)) {
            Text(
                text = capture.title,
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Text(
                text = whenLabel(capture.takenAt),
                fontSize = 11.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                maxLines = 1
            )
        }
    }
}

// "Today, 06:15"
private fun whenLabel(takenAt: ZonedDateTime): String {
    val today = LocalDate.now()
    val day = takenAt.toLocalDate()
    val time = takenAt.format(DateTimeFormatter.ofPattern("HH:mm", Locale.ENGLISH))
    return when {
        day == today -> "Today, $time"
        day == today.minusDays(1) -> "Yesterday, $time"
        day.year == today.year -> takenAt.format(DateTimeFormatter.ofPattern("d MMM", Locale.ENGLISH)) + ", $time"
        else -> takenAt.format(DateTimeFormatter.ofPattern("d MMM yyyy", Locale.ENGLISH))
    }
}