package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.ArrowDropUp
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.ui.theme.*

// Filter option data class
data class FilterOption<T>(
    val value: T,
    val label: String,
    val subtitle: String? = null,
    val iconRes: Int? = null,
    val iconColor: Color? = null,
    val iconType: String? = null
)

// Filter dropdown menu
@Composable
fun <T> CustomFilterDropdown(
    label: String,
    selectedOption: T?,
    options: List<FilterOption<T>>,
    onOptionSelected: (T?) -> Unit,
    modifier: Modifier = Modifier,
    placeholder: String = label,
    leadingIcon: @Composable (() -> Unit)? = null
) {
    val isDark = LocalThemeIsDark.current
    var isExpanded by remember { mutableStateOf(false) }
    val isActive = selectedOption != null

    Box(modifier = modifier) {
        Surface(
            shape = RoundedCornerShape(20.dp),
            color = if (isActive) ForestGreen.copy(alpha = if (isDark) 0.28f else 0.14f) else MaterialTheme.colorScheme.surface,
            border = androidx.compose.foundation.BorderStroke(
                width = 1.dp,
                color = if (isActive) ForestGreen else if (isDark) CardBorderDark else CardBorder
            ),
            onClick = { isExpanded = !isExpanded }
        ) {
            Row(
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 7.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                if (leadingIcon != null) {
                    leadingIcon()
                }

                Text(
                    text = label,
                    fontSize = 13.sp,
                    fontWeight = if (isActive) FontWeight.SemiBold else FontWeight.Normal,
                    color = if (isActive) (if (isDark) TextPrimaryDark else ForestGreenDark) else (if (isDark) TextSecondaryDark else TextSecondary),
                    maxLines = 1
                )

                Icon(
                    imageVector = if (isExpanded) Icons.Filled.ArrowDropUp else Icons.Filled.ArrowDropDown,
                    contentDescription = null,
                    tint = if (isActive) ForestGreen else (if (isDark) TextSecondaryDark else TextSecondary),
                    modifier = Modifier.size(18.dp)
                )
            }
        }

        MaterialTheme(
            shapes = MaterialTheme.shapes.copy(
                extraSmall = RoundedCornerShape(14.dp)
            )
        ) {
            DropdownMenu(
                expanded = isExpanded,
                onDismissRequest = { isExpanded = false },
                modifier = Modifier
                    .widthIn(min = 180.dp, max = 280.dp)
                    .background(if (isDark) SurfaceDark else SurfaceWhite)
                    .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
            ) {
                options.forEach { option ->
                    val isSelected = option.value == selectedOption

                    DropdownMenuItem(
                        text = {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 3.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.SpaceBetween
                            ) {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                                    modifier = Modifier.weight(1f, fill = false)
                                ) {
                                    if (option.iconType != null) {
                                        Box(
                                            modifier = Modifier
                                                .size(26.dp)
                                                .clip(RoundedCornerShape(6.dp))
                                                .background(option.iconColor ?: ForestGreen),
                                            contentAlignment = Alignment.Center
                                        ) {
                                            SensorIcon(
                                                iconType = option.iconType,
                                                tint = Color.White,
                                                size = 14.dp
                                            )
                                        }
                                    } else if (option.iconRes != null) {
                                        Icon(
                                            painter = painterResource(id = option.iconRes),
                                            contentDescription = null,
                                            tint = option.iconColor ?: ForestGreen,
                                            modifier = Modifier.size(16.dp)
                                        )
                                    }

                                    Column {
                                        Text(
                                            text = option.label,
                                            fontSize = 14.sp,
                                            fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                            color = if (isSelected) ForestGreen else if (isDark) TextPrimaryDark else TextPrimary,
                                            maxLines = 1
                                        )
                                        if (option.subtitle != null) {
                                            Text(
                                                text = option.subtitle,
                                                fontSize = 11.sp,
                                                color = if (isDark) TextSecondaryDark else TextSecondary,
                                                maxLines = 1
                                            )
                                        }
                                    }
                                }

                                if (isSelected) {
                                    Spacer(modifier = Modifier.width(6.dp))
                                    Icon(
                                        imageVector = Icons.Filled.Check,
                                        contentDescription = "Selected",
                                        tint = ForestGreen,
                                        modifier = Modifier.size(16.dp)
                                    )
                                }
                            }
                        },
                        onClick = {
                            isExpanded = false
                            onOptionSelected(option.value)
                        }
                    )
                }
            }
        }
    }
}
