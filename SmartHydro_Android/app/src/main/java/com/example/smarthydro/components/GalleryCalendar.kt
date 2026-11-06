package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.theme.*
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

// Month view for browsing photos by date
@Composable
fun GalleryCalendar(
    month: YearMonth,
    daysWithPhotos: Map<LocalDate, Int>,
    selectedDay: LocalDate?,
    onSelectDay: (LocalDate) -> Unit,
    onMonthChange: (YearMonth) -> Unit
) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val today = LocalDate.now()
    val monthCount = daysWithPhotos.filterKeys { YearMonth.from(it) == month }.values.sum()

    Row(verticalAlignment = Alignment.CenterVertically) {
        CalendarArrow(symbol = "‹", enabled = true) { onMonthChange(month.minusMonths(1)) }
        Column(
            modifier = Modifier.weight(1f),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                text = month.format(DateTimeFormatter.ofPattern("MMMM yyyy", Locale.ENGLISH)),
                fontSize = 15.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )
            Text(
                text = if (monthCount == 0) "No photos this month"
                else "$monthCount photo${if (monthCount == 1) "" else "s"} this month",
                fontSize = 12.sp,
                color = secondaryText
            )
        }
        // No going past the current month
        CalendarArrow(symbol = "›", enabled = month.isBefore(YearMonth.now())) { onMonthChange(month.plusMonths(1)) }
    }

    Spacer(modifier = Modifier.height(10.dp))

    Row(modifier = Modifier.fillMaxWidth()) {
        listOf("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun").forEach { day ->
            Text(
                text = day,
                modifier = Modifier.weight(1f),
                textAlign = TextAlign.Center,
                fontSize = 11.sp,
                fontWeight = FontWeight.SemiBold,
                color = secondaryText
            )
        }
    }

    Spacer(modifier = Modifier.height(4.dp))

    val leadingBlanks = month.atDay(1).dayOfWeek.value - 1
    val daysInMonth = month.lengthOfMonth()
    val weeks = (leadingBlanks + daysInMonth + 6) / 7

    for (week in 0 until weeks) {
        Row(modifier = Modifier.fillMaxWidth()) {
            for (column in 0 until 7) {
                val dayNumber = week * 7 + column - leadingBlanks + 1
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .aspectRatio(1f)
                        .padding(3.dp)
                ) {
                    if (dayNumber in 1..daysInMonth) {
                        val date = month.atDay(dayNumber)
                        CalendarDay(
                            date = date,
                            hasPhotos = (daysWithPhotos[date] ?: 0) > 0,
                            isSelected = date == selectedDay,
                            isToday = date == today,
                            isFuture = date.isAfter(today),
                            onClick = { onSelectDay(date) }
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun CalendarArrow(symbol: String, enabled: Boolean, onClick: () -> Unit) {
    val isDark = LocalThemeIsDark.current
    Box(
        modifier = Modifier
            .size(48.dp)
            .clip(CircleShape)
            .background(if (enabled) ForestGreen.copy(alpha = if (isDark) 0.22f else 0.10f) else Color.Transparent)
            .clickable(enabled = enabled, onClick = onClick)
            .semantics { contentDescription = if (symbol == "‹") "Previous month" else "Next month" },
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = symbol,
            modifier = Modifier.decorative(),
            fontSize = 20.sp,
            fontWeight = FontWeight.Bold,
            color = if (enabled) ForestGreen else ForestGreen.copy(alpha = 0.3f)
        )
    }
}

@Composable
private fun CalendarDay(
    date: LocalDate,
    hasPhotos: Boolean,
    isSelected: Boolean,
    isToday: Boolean,
    isFuture: Boolean,
    onClick: () -> Unit
) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    val background = when {
        isSelected -> ForestGreen
        hasPhotos -> ForestGreen.copy(alpha = if (isDark) 0.28f else 0.14f)
        else -> Color.Transparent
    }
    val textColor = when {
        isSelected -> Color.White
        hasPhotos -> if (isDark) TextPrimaryDark else ForestGreenDark
        isFuture -> secondaryText.copy(alpha = 0.4f)
        else -> secondaryText
    }

    var dayModifier = Modifier
        .fillMaxSize()
        .clip(CircleShape)
        .background(background)
    if (isToday && !isSelected) {
        dayModifier = dayModifier.border(1.5.dp, ForestGreen, CircleShape)
    }

    Box(
        modifier = dayModifier.clickable(enabled = hasPhotos, onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = date.dayOfMonth.toString(),
            fontSize = 13.sp,
            fontWeight = if (hasPhotos || isSelected) FontWeight.Bold else FontWeight.Normal,
            color = textColor
        )
    }
}