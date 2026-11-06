package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.models.BadgeType
import com.example.smarthydro.models.SensorNode
import com.example.smarthydro.ui.switchLabel
import com.example.smarthydro.ui.theme.*

@Composable
fun SensorCard(
    sensor: SensorNode,
    onClick: () -> Unit,
    onToggle: (Boolean) -> Unit = {},
    controlEnabled: Boolean = true,
    // Goes up when a switch change failed, so the switch snaps back to what the box really has
    resetKey: Int = 0,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current

    // Re-seeded whenever the real state changes (once a command round-trips), but still
    // gives an immediate visual response where there's no real state behind it yet
    var isOn by remember(sensor.controlOn, resetKey) { mutableStateOf(sensor.controlOn) }

    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .border(1.5.dp, sensor.accentColor, RoundedCornerShape(16.dp))
            .background(MaterialTheme.colorScheme.surface)
            .clickable(onClickLabel = "Open details") { onClick() }
            .padding(14.dp)
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.Top
            ) {
                // Left Icon Squircle
                Box(
                    modifier = Modifier
                        .size(56.dp)
                        .clip(RoundedCornerShape(14.dp))
                        .background(sensor.accentColor),
                    contentAlignment = Alignment.Center
                ) {
                    SensorIcon(
                        iconType = sensor.iconType,
                        tint = Color.White,
                        size = 28.dp
                    )
                }

                Spacer(modifier = Modifier.width(14.dp))

                // Middle Info
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = sensor.title,
                        color = MaterialTheme.colorScheme.onSurface,
                        fontSize = 19.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(modifier = Modifier.height(3.dp))
                    Text(
                        text = sensor.valueDisplay,
                        color = MaterialTheme.colorScheme.onSurface,
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold,
                        lineHeight = 28.sp
                    )

                    sensor.statusBadgeText?.let { badgeText ->
                        Spacer(modifier = Modifier.height(4.dp))
                        val (bg, textColor) = when (sensor.statusBadgeType) {
                            BadgeType.GOOD -> if (isDark) StatusGoodBgDark to StatusGoodTextDark else StatusGoodBg to StatusGoodText
                            BadgeType.CAUTION, BadgeType.CRITICAL -> if (isDark) StatusCautionBgDark to StatusCautionTextDark else StatusCautionBg to StatusCautionText
                            BadgeType.NEUTRAL -> if (isDark) StatusNeutralBgDark to StatusNeutralTextDark else StatusNeutralBg to StatusNeutralText
                        }

                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(12.dp))
                                .background(bg)
                                .padding(horizontal = 10.dp, vertical = 2.dp)
                        ) {
                            Text(
                                text = badgeText,
                                color = textColor,
                                fontSize = 12.sp,
                                fontWeight = FontWeight.SemiBold
                            )
                        }
                    }
                }

                // Details > arrow
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.padding(top = 2.dp)
                ) {
                    Text(
                        text = "Details",
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium
                    )
                    Spacer(modifier = Modifier.width(2.dp))
                    Icon(
                        painter = painterResource(id = R.drawable.ic_chevron_left),
                        contentDescription = null,
                        tint = if (isDark) TextSecondaryDark else TextSecondary,
                        modifier = Modifier
                            .size(16.dp)
                            .rotate(180f)
                            .padding(bottom = 1.dp)
                    )
                }
            }

            Spacer(modifier = Modifier.height(12.dp))
            HorizontalDivider(color = if (isDark) CardDividerDark else CardDivider, thickness = 1.dp)
            Spacer(modifier = Modifier.height(8.dp))

            // Control footer: the device this sensor drives, its state, and a switch to turn it on or off
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = "${sensor.controlLabel}: ${if (isOn) "ON" else "OFF"}",
                        color = MaterialTheme.colorScheme.onSurface,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium
                    )
                    Text(
                        text = sensor.infoText,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Normal
                    )
                }

                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = sensor.controlTag,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Switch(
                        checked = isOn && controlEnabled,
                        enabled = controlEnabled,
                        modifier = Modifier.switchLabel(sensor.controlLabel),
                        onCheckedChange = {
                            isOn = it
                            onToggle(it)
                        },
                        colors = SwitchDefaults.colors(
                            checkedThumbColor = SwitchThumb,
                            checkedTrackColor = ForestGreen,
                            uncheckedThumbColor = SwitchThumb,
                            uncheckedTrackColor = SwitchTrackInactive,
                            uncheckedBorderColor = Color.Transparent
                        )
                    )
                }
            }
        }
    }
}
