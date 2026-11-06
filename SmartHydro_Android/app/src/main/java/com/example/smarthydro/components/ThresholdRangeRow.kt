package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.RestartAlt
import androidx.compose.material.icons.filled.Science
import androidx.compose.material.icons.filled.Spa
import androidx.compose.material.icons.filled.Thermostat
import androidx.compose.material.icons.filled.WaterDrop
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.data.ThresholdEditor
import com.example.smarthydro.data.ThresholdSensor
import com.example.smarthydro.screens.authFieldColors
import com.example.smarthydro.ui.theme.*

// One reading's safe range with its name and unit
@Composable
fun ThresholdRangeRow(
    sensor: ThresholdSensor,
    minText: String,
    maxText: String,
    onMinChange: (String) -> Unit,
    onMaxChange: (String) -> Unit,
    isDefault: Boolean,
    error: String?,
    enabled: Boolean,
    onReset: (() -> Unit)?,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText
    val (icon, accent) = iconFor(sensor)

    Column(modifier = modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 36.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            // The same icon and colour the reading has on its sensor card
            Box(
                modifier = Modifier
                    .size(30.dp)
                    .clip(CircleShape)
                    .background(accent.copy(alpha = 0.15f)),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = accent,
                    modifier = Modifier.size(17.dp)
                )
            }
            Spacer(modifier = Modifier.width(10.dp))
            Text(
                text = sensor.label,
                fontSize = 15.sp,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface
            )

            if (!sensor.unit.equals(sensor.label, ignoreCase = true)) {
                Spacer(modifier = Modifier.width(6.dp))
                Text(
                    text = sensor.unit,
                    fontSize = 12.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary
                )
            }

            // Shows that the structure is still on the healthy starting range for this reading
            if (isDefault) {
                Spacer(modifier = Modifier.width(8.dp))
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(10.dp))
                        .background(if (isDark) StatusNeutralBgDark else StatusNeutralBg)
                        .padding(horizontal = 8.dp, vertical = 2.dp)
                ) {
                    Text(
                        text = "Default",
                        fontSize = 11.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = if (isDark) StatusNeutralTextDark else StatusNeutralText
                    )
                }
            }

            Spacer(modifier = Modifier.weight(1f))

            // Only a range someone has changed can be put back to its default
            if (onReset != null) {
                TextButton(
                    onClick = onReset,
                    enabled = enabled,
                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 0.dp)
                ) {
                    Icon(
                        imageVector = Icons.Filled.RestartAlt,
                        contentDescription = null,
                        tint = ForestGreen,
                        modifier = Modifier.size(16.dp)
                    )
                    Spacer(modifier = Modifier.width(4.dp))
                    Text(
                        text = "Reset",
                        fontSize = 13.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = ForestGreen
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(8.dp))

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            OutlinedTextField(
                value = minText,
                onValueChange = onMinChange,
                label = { Text("Minimum") },
                placeholder = { Text("Min") },
                singleLine = true,
                enabled = enabled,
                isError = error != null,
                textStyle = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.SemiBold),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal, imeAction = ImeAction.Next),
                modifier = Modifier.weight(1f),
                shape = RoundedCornerShape(12.dp),
                colors = authFieldColors(isDark)
            )
            OutlinedTextField(
                value = maxText,
                onValueChange = onMaxChange,
                label = { Text("Maximum") },
                placeholder = { Text("Max") },
                singleLine = true,
                enabled = enabled,
                isError = error != null,
                textStyle = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.SemiBold),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal, imeAction = ImeAction.Done),
                modifier = Modifier.weight(1f),
                shape = RoundedCornerShape(12.dp),
                colors = authFieldColors(isDark)
            )
        }

        Spacer(modifier = Modifier.height(4.dp))

        Text(
            text = error ?: ThresholdEditor.allowedText(sensor),
            fontSize = 12.sp,
            color = if (error != null) cautionText else if (isDark) TextSecondaryDark else TextSecondary,
            modifier = Modifier.padding(start = 4.dp)
        )
    }
}

// Each reading's icon and colour
private fun iconFor(sensor: ThresholdSensor): Pair<ImageVector, Color> = when (sensor.key) {
    "PH" -> Icons.Filled.Science to CleanWaterBlue
    "EC" -> Icons.Filled.Spa to NutrientsGreen
    "TEMPERATURE" -> Icons.Filled.Thermostat to TempTerracotta
    else -> Icons.Filled.WaterDrop to HumidityOrange
}