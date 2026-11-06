package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ChevronRight
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
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.models.SensorNode
import com.example.smarthydro.ui.readAsOne
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.switchLabel
import com.example.smarthydro.ui.theme.*

// The heading above the sensors that are not giving a reading, which can be folded away
@Composable
fun NoReadingsHeader(
    count: Int,
    expanded: Boolean,
    onToggle: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current

    Row(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .clickable(role = Role.Button, onClick = onToggle)
            .semantics { stateDescription = if (expanded) "Expanded" else "Collapsed" }
            .padding(horizontal = 4.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = "No readings ($count)",
            fontSize = 16.sp,
            fontWeight = FontWeight.Bold,
            color = if (isDark) TextPrimaryDark else ForestGreenDark,
            modifier = Modifier
                .weight(1f)
                .screenHeading()
        )
        Text(
            text = if (expanded) "Hide" else "Show",
            fontSize = 13.sp,
            fontWeight = FontWeight.Medium,
            color = if (isDark) TextSecondaryDark else TextSecondary
        )
    }
}

// A sensor that is not giving a reading. It has no gauge or details to open, but its control
// stays here, because the fan or pump it drives still works without the sensor
@Composable
fun NoReadingCard(
    sensor: SensorNode,
    onToggle: (Boolean) -> Unit = {},
    controlEnabled: Boolean = true,
    // Goes up when a switch change failed, so the switch snaps back to what the box really has
    resetKey: Int = 0,
    onClick: (() -> Unit)? = null,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    var isOn by remember(sensor.controlOn, resetKey) { mutableStateOf(sensor.controlOn) }

    Row(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp))
            .background(MaterialTheme.colorScheme.surface)
            .then(
                if (onClick != null) Modifier.clickable(role = Role.Button, onClickLabel = "Open details", onClick = onClick)
                else Modifier
            )
            .padding(14.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            modifier = Modifier
                .size(44.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(secondaryText.copy(alpha = 0.2f)),
            contentAlignment = Alignment.Center
        ) {
            SensorIcon(iconType = sensor.iconType, tint = secondaryText, size = 24.dp)
        }

        Spacer(modifier = Modifier.width(12.dp))

        Column(modifier = Modifier
            .weight(1f)
            .readAsOne()) {
            Text(
                text = sensor.title,
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface
            )
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = "No readings",
                    fontSize = 13.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = secondaryText
                )
                if (onClick != null) {
                    Icon(
                        imageVector = Icons.Filled.ChevronRight,
                        contentDescription = null,
                        tint = secondaryText,
                        modifier = Modifier.size(18.dp)
                    )
                }
            }
            Text(
                text = "Not connected, or not reporting",
                fontSize = 12.sp,
                color = secondaryText
            )
        }

        Spacer(modifier = Modifier.width(8.dp))

        Column(horizontalAlignment = Alignment.End) {
            Text(
                text = sensor.controlLabel,
                fontSize = 11.sp,
                fontWeight = FontWeight.Medium,
                color = secondaryText
            )
            Switch(
                checked = isOn && controlEnabled,
                enabled = controlEnabled,
                onCheckedChange = {
                    isOn = it
                    onToggle(it)
                },
                modifier = Modifier.switchLabel(sensor.controlLabel),
                colors = SwitchDefaults.colors(
                    checkedThumbColor = SwitchThumb,
                    checkedTrackColor = ForestGreen,
                    uncheckedThumbColor = SwitchThumb,
                    uncheckedTrackColor = SwitchTrackInactive,
                    uncheckedBorderColor = androidx.compose.ui.graphics.Color.Transparent
                )
            )
        }
    }
}
