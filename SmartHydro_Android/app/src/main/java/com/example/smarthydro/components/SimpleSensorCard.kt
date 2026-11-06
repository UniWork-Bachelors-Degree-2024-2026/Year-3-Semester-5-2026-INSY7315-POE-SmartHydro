package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDownward
import androidx.compose.material.icons.filled.ArrowUpward
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.PowerSettingsNew
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.models.BadgeType
import com.example.smarthydro.models.SensorNode
import com.example.smarthydro.ui.theme.*

/**
 * Emphasizes high visibility with:
 * - Extra large sensor icons (68dp)
 * - Clear, large sensor readings
 * - Simplified status indicators with minimal clutter
 * - Large, prominent on/off actuator toggle with clear ON/OFF states
 * - pH and EC cards have prominent Up and Down dosing buttons (auto-dosing is always on)
 * - Purely glanceable and non-navigable to keep everything strictly on one screen
 */
@Composable
fun SimpleSensorCard(
    sensor: SensorNode,
    onToggle: (Boolean) -> Unit = {},
    onDoseDown: (() -> Unit)? = null,
    onDoseUp: (() -> Unit)? = null,
    controlEnabled: Boolean = true,
    resetKey: Int = 0,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    var isOn by remember(sensor.controlOn, resetKey) { mutableStateOf(sensor.controlOn) }

    val borderColor = if (HighContrast.enabled) {
        if (isDark) Color.White else Color.Black
    } else {
        sensor.accentColor
    }

    val isPh = sensor.id.equals("clean_water", ignoreCase = true) || sensor.id.equals("ph", ignoreCase = true)
    val isEc = sensor.id.equals("nutrients", ignoreCase = true) || sensor.id.equals("ec", ignoreCase = true)
    val isDosingCard = isPh || isEc

    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(20.dp))
            .border(
                width = if (HighContrast.enabled) 2.5.dp else 1.8.dp,
                color = borderColor,
                shape = RoundedCornerShape(20.dp)
            )
            .background(MaterialTheme.colorScheme.surface)
            .padding(18.dp)
    ) {
        Column(
            modifier = Modifier.fillMaxWidth(),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            // Upper Section: Large Icon + Big Reading + Simplified Status
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Large Icon Squircle (68dp)
                Box(
                    modifier = Modifier
                        .size(68.dp)
                        .clip(RoundedCornerShape(18.dp))
                        .background(sensor.accentColor),
                    contentAlignment = Alignment.Center
                ) {
                    SensorIcon(
                        iconType = sensor.iconType,
                        tint = Color.White,
                        size = 38.dp
                    )
                }

                Spacer(modifier = Modifier.width(16.dp))

                // Sensor Name & Big Reading
                Column(
                    modifier = Modifier.weight(1f),
                    verticalArrangement = Arrangement.spacedBy(4.dp)
                ) {
                    Text(
                        text = sensor.title,
                        color = MaterialTheme.colorScheme.onSurface,
                        fontSize = 20.sp,
                        fontWeight = FontWeight.Bold,
                        lineHeight = 24.sp
                    )

                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Text(
                            text = sensor.valueDisplay,
                            color = MaterialTheme.colorScheme.onSurface,
                            fontSize = 28.sp,
                            fontWeight = FontWeight.ExtraBold,
                            lineHeight = 32.sp
                        )

                        // Simplified Status Badge
                        sensor.statusBadgeText?.let { badgeText ->
                            val (bg, textColor) = when (sensor.statusBadgeType) {
                                BadgeType.GOOD -> if (isDark) StatusGoodBgDark to StatusGoodTextDark else StatusGoodBg to StatusGoodText
                                BadgeType.CAUTION, BadgeType.CRITICAL -> if (isDark) StatusCautionBgDark to StatusCautionTextDark else StatusCautionBg to StatusCautionText
                                BadgeType.NEUTRAL -> if (isDark) StatusNeutralBgDark to StatusNeutralTextDark else StatusNeutralBg to StatusNeutralText
                            }

                            Box(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(10.dp))
                                    .background(bg)
                                    .padding(horizontal = 10.dp, vertical = 4.dp)
                            ) {
                                Text(
                                    text = badgeText,
                                    color = textColor,
                                    fontSize = 13.sp,
                                    fontWeight = FontWeight.Bold
                                )
                            }
                        }
                    }
                }
            }

            // Divider
            HorizontalDivider(
                color = if (isDark) CardDividerDark else CardDivider,
                thickness = 1.dp
            )

            if (isDosingCard) {
                // Lower Section for pH and EC: Up and Down buttons (Auto dosing toggle is hidden)
                val buttonBorder = if (HighContrast.enabled) {
                    if (isDark) Color.White else Color.Black
                } else {
                    if (isDark) CardBorderDark else CardBorder
                }
                val buttonBorderWidth = if (HighContrast.enabled) 2.dp else 1.dp

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // Down Button
                    val downCaption = if (controlEnabled) (if (isPh) "Lower pH" else "Dilute") else "Unavailable"
                    val downDescription = "${if (isPh) "Lower pH" else "Dilute nutrients"} down button"
                    Row(
                        modifier = Modifier
                            .weight(1f)
                            .heightIn(min = 60.dp)
                            .clip(RoundedCornerShape(14.dp))
                            .background(
                                if (isDark) SurfaceDarkVariant else MintBannerBg.copy(alpha = 0.6f)
                            )
                            .border(
                                width = buttonBorderWidth,
                                color = buttonBorder,
                                shape = RoundedCornerShape(14.dp)
                            )
                            .clickable(
                                enabled = controlEnabled,
                                role = Role.Button,
                                onClick = { onDoseDown?.invoke() }
                            )
                            .semantics {
                                role = Role.Button
                                contentDescription = downDescription
                            }
                            .padding(horizontal = 12.dp, vertical = 10.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(36.dp)
                                .clip(CircleShape)
                                .background(if (isDark) SurfaceDark else Color.White),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = Icons.Filled.ArrowDownward,
                                contentDescription = null,
                                tint = if (controlEnabled) (if (isDark) TextPrimaryDark else ForestGreen)
                                else (if (isDark) TextSecondaryDark else Color.Gray),
                                modifier = Modifier.size(20.dp)
                            )
                        }

                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = "Down",
                                fontSize = 17.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (controlEnabled) MaterialTheme.colorScheme.onSurface
                                else (if (isDark) TextSecondaryDark else TextSecondary),
                                maxLines = 1
                            )
                            Text(
                                text = downCaption,
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Medium,
                                color = if (isDark) TextSecondaryDark else TextSecondary,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                    }

                    // Up Button
                    val upCaption = if (controlEnabled) (if (isPh) "Raise pH" else "Add Nutrients") else "Unavailable"
                    val upDescription = "${if (isPh) "Raise pH" else "Add nutrients"} up button"
                    Row(
                        modifier = Modifier
                            .weight(1f)
                            .heightIn(min = 60.dp)
                            .clip(RoundedCornerShape(14.dp))
                            .background(
                                if (isDark) SurfaceDarkVariant else MintBannerBg.copy(alpha = 0.6f)
                            )
                            .border(
                                width = buttonBorderWidth,
                                color = buttonBorder,
                                shape = RoundedCornerShape(14.dp)
                            )
                            .clickable(
                                enabled = controlEnabled,
                                role = Role.Button,
                                onClick = { onDoseUp?.invoke() }
                            )
                            .semantics {
                                role = Role.Button
                                contentDescription = upDescription
                            }
                            .padding(horizontal = 12.dp, vertical = 10.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(36.dp)
                                .clip(CircleShape)
                                .background(if (isDark) SurfaceDark else Color.White),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = Icons.Filled.ArrowUpward,
                                contentDescription = null,
                                tint = if (controlEnabled) (if (isDark) TextPrimaryDark else ForestGreen)
                                else (if (isDark) TextSecondaryDark else Color.Gray),
                                modifier = Modifier.size(20.dp)
                            )
                        }

                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = "Up",
                                fontSize = 17.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (controlEnabled) MaterialTheme.colorScheme.onSurface
                                else (if (isDark) TextSecondaryDark else TextSecondary),
                                maxLines = 1
                            )
                            Text(
                                text = upCaption,
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Medium,
                                color = if (isDark) TextSecondaryDark else TextSecondary,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                    }
                }
            } else {
                // Lower Section: Large Accessible ON/OFF Toggle
                val stateText = if (isOn) "ON" else "OFF"
                val toggleDescription = "${sensor.controlLabel} is currently $stateText. Tap to turn ${if (isOn) "OFF" else "ON"}."

                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(min = 60.dp)
                        .clip(RoundedCornerShape(14.dp))
                        .background(
                            if (isOn) {
                                if (isDark) PrimaryButtonBgDark else ForestGreen
                            } else {
                                if (isDark) SurfaceDarkVariant else MintBannerBg.copy(alpha = 0.6f)
                            }
                        )
                        .border(
                            width = 1.dp,
                            color = if (isOn) {
                                Color.Transparent
                            } else {
                                if (isDark) CardBorderDark else CardBorder
                            },
                            shape = RoundedCornerShape(14.dp)
                        )
                        .clickable(
                            enabled = controlEnabled,
                            role = Role.Switch,
                            onClick = {
                                val nextState = !isOn
                                isOn = nextState
                                onToggle(nextState)
                            }
                        )
                        .semantics {
                            role = Role.Switch
                            stateDescription = stateText
                            contentDescription = toggleDescription
                        }
                        .padding(horizontal = 16.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(36.dp)
                                .clip(CircleShape)
                                .background(
                                    if (isOn) Color.White.copy(alpha = 0.2f) else (if (isDark) SurfaceDark else Color.White)
                                ),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = if (isOn) Icons.Filled.Check else Icons.Filled.PowerSettingsNew,
                                contentDescription = null,
                                tint = if (isOn) Color.White else (if (isDark) TextSecondaryDark else ForestGreen),
                                modifier = Modifier.size(20.dp)
                            )
                        }

                        Column {
                            Text(
                                text = sensor.controlLabel,
                                fontSize = 17.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isOn) Color.White else MaterialTheme.colorScheme.onSurface
                            )
                            Text(
                                text = if (controlEnabled) "Tap to switch" else "Control unavailable",
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Medium,
                                color = if (isOn) Color.White.copy(alpha = 0.85f) else (if (isDark) TextSecondaryDark else TextSecondary)
                            )
                        }
                    }

                    // Large State Badge Pill
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(20.dp))
                            .background(
                                if (isOn) Color.White else (if (isDark) SurfaceDark else ForestGreenLight.copy(alpha = 0.2f))
                            )
                            .padding(horizontal = 16.dp, vertical = 8.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = stateText,
                            fontSize = 16.sp,
                            fontWeight = FontWeight.ExtraBold,
                            color = if (isOn) ForestGreenDark else (if (isDark) TextPrimaryDark else ForestGreenDark)
                        )
                    }
                }
            }
        }
    }
}
