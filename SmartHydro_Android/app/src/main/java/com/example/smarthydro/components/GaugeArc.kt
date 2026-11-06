package com.example.smarthydro.components

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.foundation.clickable
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.models.BadgeType
import com.example.smarthydro.models.SensorDetailData
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.theme.*
import kotlin.math.cos
import kotlin.math.sin

@Composable
fun GaugeArc(
    data: SensorDetailData,
    modifier: Modifier = Modifier,
    isLoading: Boolean = false,
    // A dose button is only clickable when its action is given. Without one it is dimmed and says why in doseNote
    onDoseMinus: (() -> Unit)? = null,
    onDosePlus: (() -> Unit)? = null,
    minusLabel: String = "Decrease dosing",
    plusLabel: String = "Increase dosing",
    doseNote: String? = null
) {
    val isDark = LocalThemeIsDark.current

    val tickColor = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f)
    val sublabel = when {
        isLoading -> "Loading…"
        data.hasReading -> data.gaugeSublabel
        else -> "Not connected, or not reporting"
    }

    // One spoken line for the whole reading, since the arc itself is only decoration
    val spokenSummary = if (isLoading) {
        (if (data.title.isNotEmpty()) "${data.title}: " else "") + "loading"
    } else if (!data.hasReading) {
        (if (data.title.isNotEmpty()) "${data.title}: no readings" else "No readings") + ". Not connected, or not reporting"
    } else buildList {
        val reading = data.currentValue + if (data.unit.isNotEmpty()) " ${data.unit}" else ""
        add(if (data.title.isNotEmpty()) "${data.title}: $reading" else reading)
        if (data.gaugeSublabel.isNotEmpty()) add(data.gaugeSublabel)
        data.statusBadge?.let { add(it) }
        data.optimalRangeText?.let { add(it) }
    }.joinToString(". ")

    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 24.dp, vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Box(
            modifier = Modifier
                .size(width = 240.dp, height = 130.dp)
                .clearAndSetSemantics { contentDescription = spokenSummary },
            contentAlignment = Alignment.BottomCenter
        ) {
            Canvas(modifier = Modifier.fillMaxSize()) {
                val strokeWidth = 11.dp.toPx()
                val diameter = size.width - strokeWidth
                val topLeft = Offset(strokeWidth / 2f, strokeWidth / 2f)
                val arcSize = Size(diameter, diameter)

                // Faint full arc, so the unfilled part of the gauge is still visible
                drawArc(
                    color = data.accentColor.copy(alpha = 0.18f),
                    startAngle = 180f,
                    sweepAngle = 180f,
                    useCenter = false,
                    topLeft = topLeft,
                    size = arcSize,
                    style = Stroke(width = strokeWidth, cap = StrokeCap.Round)
                )

                // The reading, filled from the left end. With no reading the arc stays empty
                data.gaugeProgress?.let { progress ->
                    drawArc(
                        color = data.accentColor,
                        startAngle = 180f,
                        sweepAngle = 180f * progress.coerceIn(0f, 1f),
                        useCenter = false,
                        topLeft = topLeft,
                        size = arcSize,
                        style = Stroke(width = strokeWidth, cap = StrokeCap.Round)
                    )
                }

                // Two ticks across the arc mark where the optimal range starts and ends
                val center = Offset(size.width / 2f, size.width / 2f)
                val radius = diameter / 2f
                listOf(data.gaugeBandStart, data.gaugeBandEnd).forEach { fraction ->
                    if (fraction != null && data.hasReading) {
                        val angle = Math.toRadians((180f + 180f * fraction).toDouble())
                        val inner = radius - strokeWidth
                        val outer = radius + strokeWidth
                        drawLine(
                            color = tickColor,
                            start = Offset(center.x + (inner * cos(angle)).toFloat(), center.y + (inner * sin(angle)).toFloat()),
                            end = Offset(center.x + (outer * cos(angle)).toFloat(), center.y + (outer * sin(angle)).toFloat()),
                            strokeWidth = 2.dp.toPx()
                        )
                    }
                }
            }

            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                modifier = Modifier.padding(bottom = 6.dp)
            ) {
                Text(
                    text = buildAnnotatedString {
                        append(if (data.hasReading) data.currentValue else "No readings")
                        if (data.hasReading && data.unit.isNotEmpty()) {
                            withStyle(SpanStyle(fontSize = 24.sp, fontWeight = FontWeight.Normal)) {
                                append(" ${data.unit}")
                            }
                        }
                    },
                    fontSize = if (data.hasReading) 44.sp else 26.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (data.hasReading) MaterialTheme.colorScheme.onSurface else (if (isDark) TextSecondaryDark else TextSecondary),
                    textAlign = TextAlign.Center
                )

                if (sublabel.isNotEmpty()) {
                    Text(
                        text = sublabel,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Normal,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        textAlign = TextAlign.Center
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(16.dp))

        // Center status badge or Dosing - / + controls
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Center,
            modifier = Modifier.fillMaxWidth()
        ) {
            if (data.isDosingControl && data.hasMinusButton) {
                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .clip(CircleShape)
                        .then(
                            if (onDoseMinus != null) Modifier.clickable(role = Role.Button, onClickLabel = minusLabel, onClick = onDoseMinus)
                            else Modifier.alpha(0.4f)
                        )
                        .border(2.dp, MaterialTheme.colorScheme.onSurface, CircleShape)
                        .semantics {
                            contentDescription = if (onDoseMinus != null) minusLabel
                            else "$minusLabel, not available" + (doseNote?.let { ": $it" } ?: "")
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Text(text = "−", modifier = Modifier.decorative(), fontSize = 24.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
                }
                Spacer(modifier = Modifier.width(20.dp))
            }

            data.statusBadge?.takeIf { data.hasReading && !isLoading }?.let { badgeText ->
                val (bg, textColor) = when (data.statusBadgeType) {
                    BadgeType.GOOD -> if (isDark) StatusGoodBgDark to StatusGoodTextDark else StatusGoodBg to StatusGoodText
                    BadgeType.CAUTION, BadgeType.CRITICAL -> if (isDark) StatusCautionBgDark to StatusCautionTextDark else StatusCautionBg to StatusCautionText
                    BadgeType.NEUTRAL -> if (isDark) StatusNeutralBgDark to StatusNeutralTextDark else StatusNeutralBg to StatusNeutralText
                }

                Box(
                    modifier = Modifier
                        .decorative()
                        .clip(RoundedCornerShape(16.dp))
                        .background(bg)
                        .padding(horizontal = 24.dp, vertical = 6.dp)
                ) {
                    Text(text = badgeText, color = textColor, fontSize = 14.sp, fontWeight = FontWeight.Bold)
                }
            }

            if (data.isDosingControl && data.hasPlusButton) {
                Spacer(modifier = Modifier.width(20.dp))
                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .clip(CircleShape)
                        .then(
                            if (onDosePlus != null) Modifier.clickable(role = Role.Button, onClickLabel = plusLabel, onClick = onDosePlus)
                            else Modifier.alpha(0.4f)
                        )
                        .border(2.dp, MaterialTheme.colorScheme.onSurface, CircleShape)
                        .semantics {
                            contentDescription = if (onDosePlus != null) plusLabel
                            else "$plusLabel, not available" + (doseNote?.let { ": $it" } ?: "")
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Text(text = "+", modifier = Modifier.decorative(), fontSize = 24.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
                }
            }
        }

        // The reason the dose buttons are dimmed, so a dead button is never a mystery
        if (data.isDosingControl && onDoseMinus == null && onDosePlus == null && doseNote != null) {
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                text = doseNote,
                modifier = Modifier.decorative(),
                fontSize = 12.sp,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.7f),
                textAlign = TextAlign.Center
            )
        }

        data.optimalRangeText?.takeIf { data.hasReading && !isLoading }?.let { optimalText ->
            Spacer(modifier = Modifier.height(10.dp))
            Text(
                text = optimalText,
                modifier = Modifier.decorative(),
                fontSize = 13.sp,
                fontWeight = FontWeight.Normal,
                color = if (isDark) TextSecondaryDark else TextSecondary
            )
        }
    }
}
