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
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.ui.theme.*

/**
 * Custom dropdown component for selecting a Structure Type (e.g. "Hydroponic Structure", "Fodder Structure").
 * Clean, modern design with matching theme borders, colored icons, and no subtitle.
 */
@Composable
fun StructureTypeDropdown(
    selectedType: String,
    options: List<String>,
    onTypeSelected: (String) -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    var isExpanded by remember { mutableStateOf(false) }

    Box(modifier = modifier.fillMaxWidth()) {
        // Selected Value Card - using Surface to guarantee perfect corner clipping and no bleed
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .height(58.dp),
            shape = RoundedCornerShape(14.dp),
            color = if (isDark) SurfaceDark else SurfaceWhite,
            border = androidx.compose.foundation.BorderStroke(
                width = 1.5.dp,
                color = if (isExpanded) ForestGreen else if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.5f)
            ),
            onClick = { isExpanded = !isExpanded }
        ) {
            Row(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 16.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    val isHydro = selectedType.contains("Hydro", ignoreCase = true)
                    Box(
                        modifier = Modifier
                            .size(36.dp)
                            .clip(CircleShape)
                            .background(if (isHydro) WaterBlue.copy(alpha = 0.16f) else MintBannerBg),
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(
                            painter = painterResource(id = if (isHydro) R.drawable.ic_water_drop else R.drawable.ic_flower),
                            contentDescription = null,
                            tint = if (isHydro) WaterBlue else ForestGreen,
                            modifier = Modifier.size(18.dp)
                        )
                    }

                    Text(
                        text = selectedType,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = if (isDark) TextPrimaryDark else ForestGreenDark
                    )
                }

                Icon(
                    imageVector = if (isExpanded) Icons.Filled.ArrowDropUp else Icons.Filled.ArrowDropDown,
                    contentDescription = if (isExpanded) "Collapse" else "Expand",
                    tint = if (isDark) TextSecondaryDark else ForestGreen,
                    modifier = Modifier.size(24.dp)
                )
            }
        }

        // Dropdown Items Menu
        MaterialTheme(
            shapes = MaterialTheme.shapes.copy(
                extraSmall = RoundedCornerShape(14.dp)
            )
        ) {
            DropdownMenu(
                expanded = isExpanded,
                onDismissRequest = { isExpanded = false },
                modifier = Modifier
                    .fillMaxWidth(0.92f)
                    .background(if (isDark) SurfaceDark else SurfaceWhite)
                    .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
            ) {
                options.forEach { type ->
                    val isSelected = selectedType == type
                    val isHydro = type.contains("Hydro", ignoreCase = true)

                    DropdownMenuItem(
                        text = {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 4.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.SpaceBetween
                            ) {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                                ) {
                                    Box(
                                        modifier = Modifier
                                            .size(32.dp)
                                            .clip(CircleShape)
                                            .background(if (isHydro) WaterBlue.copy(alpha = 0.14f) else MintBannerBg),
                                        contentAlignment = Alignment.Center
                                    ) {
                                        Icon(
                                            painter = painterResource(id = if (isHydro) R.drawable.ic_water_drop else R.drawable.ic_flower),
                                            contentDescription = null,
                                            tint = if (isHydro) WaterBlue else ForestGreen,
                                            modifier = Modifier.size(16.dp)
                                        )
                                    }

                                    Text(
                                        text = type,
                                        fontSize = 14.sp,
                                        fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                        color = if (isSelected) ForestGreen else if (isDark) TextPrimaryDark else TextPrimary
                                    )
                                }

                                if (isSelected) {
                                    Icon(
                                        imageVector = Icons.Filled.Check,
                                        contentDescription = "Selected",
                                        tint = ForestGreen,
                                        modifier = Modifier.size(18.dp)
                                    )
                                }
                            }
                        },
                        onClick = {
                            onTypeSelected(type)
                            isExpanded = false
                        }
                    )
                }
            }
        }
    }
}
