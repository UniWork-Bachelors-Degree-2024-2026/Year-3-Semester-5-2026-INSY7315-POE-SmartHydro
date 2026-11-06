package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Place
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.models.AlertItem
import com.example.smarthydro.models.AlertSeverity
import com.example.smarthydro.ui.theme.*
import java.time.LocalDate
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.Locale

// One alert in the list
@Composable
fun AlertCard(
    alert: AlertItem,
    onClick: () -> Unit,
    onAcknowledge: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val status = alertStatusStyle(alert, isDark)

    Row(
        modifier = modifier
            .fillMaxWidth()
            .height(IntrinsicSize.Min)
            .clip(RoundedCornerShape(16.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp))
            .background(MaterialTheme.colorScheme.surface)
            .clickable(onClick = onClick)
    ) {
        // Coloured strip down the left so the status shows at a glance
        Box(
            modifier = Modifier
                .width(5.dp)
                .fillMaxHeight()
                .background(status.text)
        )

        Column(modifier = Modifier.padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(44.dp)
                        .clip(RoundedCornerShape(12.dp))
                        .background(alert.accentColor),
                    contentAlignment = Alignment.Center
                ) {
                    SensorIcon(iconType = alert.iconType, tint = Color.White, size = 24.dp)
                }

                Spacer(modifier = Modifier.width(12.dp))

                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = alert.title,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface
                    )
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(
                            imageVector = Icons.Filled.Place,
                            contentDescription = null,
                            tint = secondaryText,
                            modifier = Modifier.size(13.dp)
                        )
                        Spacer(modifier = Modifier.width(3.dp))
                        Text(text = alert.location, fontSize = 12.sp, color = secondaryText, maxLines = 1)
                    }
                }

                Spacer(modifier = Modifier.width(8.dp))

                StatusPill(label = status.label, icon = status.icon, background = status.background, text = status.text)
            }

            Spacer(modifier = Modifier.height(12.dp))
            Row(verticalAlignment = Alignment.Top) {
                Column(modifier = Modifier.weight(1f)) {
                    if (alert.value.isNotBlank()) {
                        Text(text = alert.value, fontSize = 16.sp, fontWeight = FontWeight.Bold, color = status.text)
                    }
                    if (alert.detail.isNotBlank()) {
                        Text(text = alert.detail, fontSize = 12.sp, lineHeight = 16.sp, color = secondaryText)
                    }
                }

                alert.occurredAt?.let { happenedAt ->
                    Spacer(modifier = Modifier.width(10.dp))
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(
                            imageVector = Icons.Filled.Schedule,
                            contentDescription = null,
                            tint = secondaryText,
                            modifier = Modifier.size(16.dp)
                        )
                        Spacer(modifier = Modifier.width(6.dp))
                        Column {
                            Text(
                                text = timeText(happenedAt),
                                fontSize = 13.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.onSurface
                            )
                            Text(text = dayText(happenedAt), fontSize = 11.sp, color = secondaryText)
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(10.dp))
            HorizontalDivider(color = if (isDark) CardDividerDark else CardDivider)
            Spacer(modifier = Modifier.height(8.dp))

            if (alert.isResolved) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        imageVector = Icons.Filled.CheckCircle,
                        contentDescription = null,
                        tint = secondaryText,
                        modifier = Modifier.size(15.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(text = "Cleared by ${alert.clearedBy ?: "Operator"}", fontSize = 13.sp, color = secondaryText)
                }
            } else {
                OutlinedButton(
                    onClick = onAcknowledge,
                    shape = RoundedCornerShape(18.dp),
                    border = BorderStroke(1.dp, if (isDark) OutlinedButtonBorderDark else ForestGreen),
                    contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp),
                    modifier = Modifier.height(34.dp)
                ) {
                    Icon(
                        imageVector = Icons.Filled.Check,
                        contentDescription = null,
                        tint = if (isDark) AccentGreenDark else ForestGreen,
                        modifier = Modifier.size(15.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = "Acknowledge",
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold,
                        color = if (isDark) TextPrimaryDark else ForestGreenDark
                    )
                }
            }
        }
    }
}

// Label, icon and colours for an alert's state
private class AlertStatusStyle(val label: String, val icon: ImageVector, val background: Color, val text: Color)

private fun alertStatusStyle(alert: AlertItem, isDark: Boolean): AlertStatusStyle = when {
    alert.isResolved -> AlertStatusStyle(
        "Resolved", Icons.Filled.CheckCircle,
        if (isDark) StatusGoodBgDark else StatusGoodBg,
        if (isDark) StatusGoodTextDark else StatusGoodText
    )
    alert.severity == AlertSeverity.ALERT -> AlertStatusStyle(
        "Alert", Icons.Filled.Warning,
        if (isDark) StatusCautionBgDark else StatusCautionBg,
        if (isDark) StatusCautionTextDark else StatusCautionText
    )
    else -> AlertStatusStyle(
        "Caution", Icons.Filled.Info,
        if (isDark) StatusWarnBgDark else StatusWarnBg,
        if (isDark) StatusWarnTextDark else StatusWarnText
    )
}

@Composable
private fun StatusPill(label: String, icon: ImageVector, background: Color, text: Color) {
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(12.dp))
            .background(background)
            .padding(horizontal = 8.dp, vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(imageVector = icon, contentDescription = null, tint = text, modifier = Modifier.size(13.dp))
        Spacer(modifier = Modifier.width(4.dp))
        Text(text = label, color = text, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
    }
}

// "3:45 PM"
private fun timeText(happenedAt: ZonedDateTime): String =
    happenedAt.format(DateTimeFormatter.ofPattern("h:mm a", Locale.ENGLISH))

// "Today"
private fun dayText(happenedAt: ZonedDateTime): String {
    val days = ChronoUnit.DAYS.between(happenedAt.toLocalDate(), LocalDate.now())
    return when {
        days <= 0 -> "Today"
        days == 1L -> "Yesterday"
        else -> happenedAt.format(DateTimeFormatter.ofPattern("d MMM", Locale.ENGLISH))
    }
}