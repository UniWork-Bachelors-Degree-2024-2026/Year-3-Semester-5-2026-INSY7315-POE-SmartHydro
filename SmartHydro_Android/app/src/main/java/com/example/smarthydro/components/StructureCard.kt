package com.example.smarthydro.components

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.models.StructureItem
import com.example.smarthydro.ui.theme.*

@Composable
fun StructureCard(
    structure: StructureItem,
    onSelect: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
    canManage: Boolean = true,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current

    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .border(1.5.dp, structure.themeColor, RoundedCornerShape(16.dp))
            .background(MaterialTheme.colorScheme.surface)
            .clickable { onSelect() }
            .padding(14.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Left squircle icon
            Box(
                modifier = Modifier
                    .size(54.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(structure.themeColor),
                contentAlignment = Alignment.Center
            ) {
                SensorIcon(
                    iconType = structure.iconType,
                    tint = Color.White,
                    size = 28.dp
                )
            }

            Spacer(modifier = Modifier.width(14.dp))

            // Title and Subtitle
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = structure.title,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )

                if (structure.subtitle != null) {
                    Spacer(modifier = Modifier.height(2.dp))
                    Text(
                        text = structure.subtitle,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium,
                        color = structure.subtitleColor ?: (if (isDark) TextSecondaryDark else TextSecondary)
                    )
                }
            }

            // Edit and delete are only offered to whoever is allowed to change this structure
            if (canManage) {
                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .clip(RoundedCornerShape(12.dp))
                        .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                        .clickable { onEdit() },
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        painter = painterResource(id = R.drawable.ic_edit),
                        contentDescription = "Edit Structure ${structure.title}",
                        tint = if (isDark) TextSecondaryDark else TextSecondary,
                        modifier = Modifier.size(22.dp)
                    )
                }

                Spacer(modifier = Modifier.width(8.dp))

                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .clip(RoundedCornerShape(12.dp))
                        .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                        .clickable { onDelete() },
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        painter = painterResource(id = R.drawable.ic_delete),
                        contentDescription = "Delete Structure ${structure.title}",
                        tint = if (isDark) StatusCautionTextDark else StatusCautionText,
                        modifier = Modifier.size(22.dp)
                    )
                }
            }
        }
    }
}

@Composable
fun AddStructureButtonCard(
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val borderColor = if (isDark) CardBorderDark else TextPrimary

    Box(
        modifier = modifier
            .fillMaxWidth()
            .height(72.dp)
            .clip(RoundedCornerShape(16.dp))
            .clickable { onClick() },
        contentAlignment = Alignment.Center
    ) {
        Canvas(modifier = Modifier.fillMaxSize()) {
            val stroke = Stroke(
                width = 2.dp.toPx(),
                pathEffect = PathEffect.dashPathEffect(floatArrayOf(14f, 10f), 0f)
            )
            drawRoundRect(
                color = borderColor,
                cornerRadius = CornerRadius(16.dp.toPx()),
                style = stroke
            )
        }

        Text(
            text = "+ Add a Structure",
            fontSize = 18.sp,
            fontWeight = FontWeight.Bold,
            color = if (isDark) AccentGreenDark else ForestGreen
        )
    }
}

