package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
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
import androidx.compose.ui.window.Dialog
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.theme.*
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

// Calendar dialog
@Composable
fun CustomCalendarDialog(
    initialDate: LocalDate,
    onDateSelected: (LocalDate) -> Unit,
    onDismiss: () -> Unit,
    maxDate: LocalDate? = LocalDate.now(),
    minDate: LocalDate? = null,
    title: String = "Select Date"
) {
    val isDark = LocalThemeIsDark.current
    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val dialogBg = if (isDark) SurfaceDark else SurfaceWhite
    val cardBorder = if (isDark) CardBorderDark else CardBorder

    var selectedDate by remember { mutableStateOf(initialDate) }
    var currentMonth by remember { mutableStateOf(YearMonth.from(initialDate)) }
    val today = LocalDate.now()

    Dialog(onDismissRequest = onDismiss) {
        Surface(
            shape = RoundedCornerShape(24.dp),
            color = dialogBg,
            border = androidx.compose.foundation.BorderStroke(1.dp, cardBorder),
            shadowElevation = 8.dp,
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 8.dp)
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp)
            ) {
                // Title and selected date
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column {
                        Text(
                            text = title,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Medium,
                            color = secondaryText
                        )
                        Spacer(modifier = Modifier.height(2.dp))
                        Text(
                            text = selectedDate.format(DateTimeFormatter.ofPattern("EEE, d MMM yyyy", Locale.ENGLISH)),
                            fontSize = 18.sp,
                            fontWeight = FontWeight.Bold,
                            color = primaryText
                        )
                    }

                    // Today shortcut button
                    Surface(
                        shape = RoundedCornerShape(12.dp),
                        color = ForestGreen.copy(alpha = if (isDark) 0.22f else 0.12f),
                        modifier = Modifier.clickable {
                            val target = if (maxDate != null && today.isAfter(maxDate)) maxDate else today
                            selectedDate = target
                            currentMonth = YearMonth.from(target)
                        }
                    ) {
                        Text(
                            text = "Today",
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            color = ForestGreen,
                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp)
                        )
                    }
                }

                Spacer(modifier = Modifier.height(18.dp))

                // Month navigation
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    val canGoBack = minDate == null || currentMonth.minusMonths(1).atEndOfMonth().isAfter(minDate.minusDays(1))
                    val canGoForward = maxDate == null || !currentMonth.plusMonths(1).atDay(1).isAfter(maxDate)

                    Box(
                        modifier = Modifier
                            .size(38.dp)
                            .clip(CircleShape)
                            .background(if (canGoBack) ForestGreen.copy(alpha = if (isDark) 0.18f else 0.08f) else Color.Transparent)
                            .clickable(enabled = canGoBack) { currentMonth = currentMonth.minusMonths(1) }
                            .semantics { contentDescription = "Previous Month" },
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "‹",
                            modifier = Modifier.decorative(),
                            fontSize = 22.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (canGoBack) ForestGreen else secondaryText.copy(alpha = 0.3f)
                        )
                    }

                    Text(
                        text = currentMonth.format(DateTimeFormatter.ofPattern("MMMM yyyy", Locale.ENGLISH)),
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = primaryText
                    )

                    Box(
                        modifier = Modifier
                            .size(38.dp)
                            .clip(CircleShape)
                            .background(if (canGoForward) ForestGreen.copy(alpha = if (isDark) 0.18f else 0.08f) else Color.Transparent)
                            .clickable(enabled = canGoForward) { currentMonth = currentMonth.plusMonths(1) }
                            .semantics { contentDescription = "Next Month" },
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "›",
                            modifier = Modifier.decorative(),
                            fontSize = 22.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (canGoForward) ForestGreen else secondaryText.copy(alpha = 0.3f)
                        )
                    }
                }

                Spacer(modifier = Modifier.height(12.dp))

                // Day of week headers
                Row(modifier = Modifier.fillMaxWidth()) {
                    listOf("Mo", "Tu", "We", "Th", "Fr", "Sa", "Su").forEach { dayLabel ->
                        Text(
                            text = dayLabel,
                            modifier = Modifier.weight(1f),
                            textAlign = TextAlign.Center,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = secondaryText
                        )
                    }
                }

                Spacer(modifier = Modifier.height(6.dp))

                // Days grid
                val leadingBlanks = currentMonth.atDay(1).dayOfWeek.value - 1
                val daysInMonth = currentMonth.lengthOfMonth()
                val weeks = (leadingBlanks + daysInMonth + 6) / 7

                for (week in 0 until weeks) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 2.dp)
                    ) {
                        for (column in 0 until 7) {
                            val dayNumber = week * 7 + column - leadingBlanks + 1
                            Box(
                                modifier = Modifier
                                    .weight(1f)
                                    .aspectRatio(1f)
                                    .padding(2.dp),
                                contentAlignment = Alignment.Center
                            ) {
                                if (dayNumber in 1..daysInMonth) {
                                    val date = currentMonth.atDay(dayNumber)
                                    val isSelected = date == selectedDate
                                    val isToday = date == today
                                    val isDisabled = (maxDate != null && date.isAfter(maxDate)) ||
                                            (minDate != null && date.isBefore(minDate))

                                    val cellBg = when {
                                        isSelected -> ForestGreen
                                        isToday -> ForestGreen.copy(alpha = if (isDark) 0.22f else 0.12f)
                                        else -> Color.Transparent
                                    }

                                    val cellTextColor = when {
                                        isSelected -> Color.White
                                        isDisabled -> secondaryText.copy(alpha = 0.35f)
                                        isToday -> ForestGreen
                                        else -> if (isDark) TextPrimaryDark else TextPrimary
                                    }

                                    Box(
                                        modifier = Modifier
                                            .fillMaxSize()
                                            .clip(CircleShape)
                                            .background(cellBg)
                                            .then(
                                                if (isToday && !isSelected) {
                                                    Modifier.border(1.5.dp, ForestGreen, CircleShape)
                                                } else Modifier
                                            )
                                            .clickable(enabled = !isDisabled) {
                                                selectedDate = date
                                            },
                                        contentAlignment = Alignment.Center
                                    ) {
                                        Text(
                                            text = dayNumber.toString(),
                                            fontSize = 13.sp,
                                            fontWeight = if (isSelected || isToday) FontWeight.Bold else FontWeight.Normal,
                                            color = cellTextColor
                                        )
                                    }
                                }
                            }
                        }
                    }
                }

                Spacer(modifier = Modifier.height(18.dp))

                // Action buttons
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    OutlinedButton(
                        onClick = onDismiss,
                        modifier = Modifier
                            .weight(1f)
                            .height(44.dp),
                        shape = RoundedCornerShape(12.dp),
                        border = androidx.compose.foundation.BorderStroke(1.dp, if (isDark) CardBorderDark else CardBorder)
                    ) {
                        Text(
                            text = "Cancel",
                            color = secondaryText,
                            fontSize = 14.sp,
                            fontWeight = FontWeight.SemiBold
                        )
                    }

                    Button(
                        onClick = {
                            onDateSelected(selectedDate)
                            onDismiss()
                        },
                        modifier = Modifier
                            .weight(1f)
                            .height(44.dp),
                        shape = RoundedCornerShape(12.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                            contentColor = Color.White
                        )
                    ) {
                        Text(
                            text = "Set Date",
                            fontSize = 14.sp,
                            fontWeight = FontWeight.Bold
                        )
                    }
                }
            }
        }
    }
}
