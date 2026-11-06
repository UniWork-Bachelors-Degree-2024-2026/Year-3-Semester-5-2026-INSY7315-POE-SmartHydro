package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.data.TrendPoint
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*

// The 7-day trend. A day with no readings is a gap, and with no readings at all the chart says
// so instead of drawing anything
@Composable
fun BarChartView(
    points: List<TrendPoint>,
    barColor: Color,
    modifier: Modifier = Modifier,
    title: String = "7-Day Trend",
    subject: String = "",
    unit: String = "",
    isLoading: Boolean = false
) {
    val isDark = LocalThemeIsDark.current
    val readings = points.mapNotNull { it.value }
    val hasData = readings.isNotEmpty()
    val maxVal = readings.maxOrNull() ?: 1f
    val minVal = readings.minOrNull() ?: 0f
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    // One spoken description of the whole chart, so it is not read as a long list of bare numbers
    val unitPart = if (unit.isNotEmpty()) " $unit" else ""
    val heading = if (subject.isNotEmpty()) "$subject, $title" else title
    val chartSummary = if (!hasData) {
        if (isLoading) "$heading: loading" else "$heading: no readings recorded yet"
    } else {
        val withValues = points.filter { it.value != null }
        val highest = withValues.maxByOrNull { it.value!! }!!.label
        val lowest = withValues.minByOrNull { it.value!! }!!.label
        val spoken = points.joinToString(", ") {
            if (it.value == null) "${it.label} no data" else "${it.label} ${it.valueText}$unitPart"
        }
        "$heading: $spoken" + if (withValues.size > 1) ". Highest $highest, lowest $lowest" else ""
    }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp, vertical = 6.dp)
    ) {
        Text(
            text = title,
            fontSize = 16.sp,
            fontWeight = FontWeight.Bold,
            color = if (isDark) TextPrimaryDark else ForestGreenDark,
            modifier = Modifier.screenHeading().padding(bottom = 8.dp)
        )

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .clearAndSetSemantics { contentDescription = chartSummary }
                .clip(RoundedCornerShape(12.dp))
                .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(12.dp))
                .background(MaterialTheme.colorScheme.surface)
                .padding(horizontal = 12.dp, vertical = 16.dp)
        ) {
            if (!hasData) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(120.dp),
                    verticalArrangement = Arrangement.Center,
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Text(
                        text = if (isLoading) "Loading history…" else "No history yet",
                        fontSize = 15.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurface
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = if (isLoading) "Fetching the last 7 days." else "Daily readings appear here once this sensor has reported.",
                        fontSize = 12.sp,
                        color = secondaryText,
                        textAlign = TextAlign.Center
                    )
                }
            } else {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(170.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.Bottom
                ) {
                    points.forEach { point ->
                        val value = point.value

                        Column(
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.Bottom,
                            modifier = Modifier
                                .weight(1f)
                                .fillMaxHeight()
                        ) {
                            Text(
                                text = point.valueText,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (value == null) secondaryText else MaterialTheme.colorScheme.onSurface,
                                textAlign = TextAlign.Center,
                                modifier = Modifier.padding(bottom = 4.dp)
                            )

                            if (value == null) {
                                // No readings that day: a thin stub marks the gap instead of a bar
                                Box(
                                    modifier = Modifier
                                        .width(28.dp)
                                        .height(3.dp)
                                        .clip(RoundedCornerShape(2.dp))
                                        .background(secondaryText.copy(alpha = 0.35f))
                                )
                            } else {
                                val ratio = if (maxVal == minVal) 0.8f else ((value - (minVal * 0.7f)) / (maxVal - (minVal * 0.7f))).coerceIn(0.25f, 1f)

                                Box(
                                    modifier = Modifier
                                        .width(28.dp)
                                        .height((120 * ratio).dp)
                                        .clip(RoundedCornerShape(topStart = 4.dp, topEnd = 4.dp, bottomStart = 2.dp, bottomEnd = 2.dp))
                                        .background(barColor)
                                )
                            }

                            Spacer(modifier = Modifier.height(10.dp))

                            Text(
                                text = point.label,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Medium,
                                color = if (isDark) TextSecondaryDark else ForestGreenDark,
                                textAlign = TextAlign.Center
                            )
                        }
                    }
                }
            }
        }
    }
}
