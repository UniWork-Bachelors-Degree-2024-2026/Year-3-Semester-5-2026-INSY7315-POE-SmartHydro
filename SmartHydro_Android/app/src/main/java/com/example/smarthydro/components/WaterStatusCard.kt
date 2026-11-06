package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.models.BadgeType
import com.example.smarthydro.models.SensorDetailData
import com.example.smarthydro.ui.theme.*

// The Water page's status. The tank sensor only says whether water is there, so this is a plain
// status and not a gauge
@Composable
fun WaterStatusCard(
    data: SensorDetailData,
    modifier: Modifier = Modifier,
    isLoading: Boolean = false
) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    val headline = when {
        isLoading -> "Loading…"
        !data.hasReading -> "No readings"
        else -> data.currentValue
    }
    val detail = when {
        isLoading -> ""
        !data.hasReading -> "Not connected, or not reporting"
        else -> data.gaugeSublabel
    }
    val headlineColor = when {
        isLoading || !data.hasReading -> secondaryText
        data.statusBadgeType == BadgeType.GOOD -> MaterialTheme.colorScheme.onSurface
        else -> if (isDark) StatusCautionTextDark else StatusCautionText
    }

    // One spoken line for the whole status
    val spoken = if (detail.isEmpty()) "${data.title}: $headline" else "${data.title}: $headline. $detail"

    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 24.dp, vertical = 12.dp)
            .clearAndSetSemantics { contentDescription = spoken },
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Box(
            modifier = Modifier
                .size(84.dp)
                .clip(CircleShape)
                .background(data.accentColor.copy(alpha = if (isDark) 0.22f else 0.14f)),
            contentAlignment = Alignment.Center
        ) {
            SensorIcon(iconType = "water_drop", tint = data.accentColor, size = 44.dp)
        }

        Spacer(modifier = Modifier.height(14.dp))

        Text(
            text = headline,
            fontSize = 28.sp,
            fontWeight = FontWeight.Bold,
            color = headlineColor,
            textAlign = TextAlign.Center
        )

        if (detail.isNotEmpty()) {
            Spacer(modifier = Modifier.height(4.dp))
            Text(text = detail, fontSize = 14.sp, color = secondaryText, textAlign = TextAlign.Center)
        }
    }
}
