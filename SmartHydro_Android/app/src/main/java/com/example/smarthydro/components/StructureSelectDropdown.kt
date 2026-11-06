package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.models.StructureItem
import com.example.smarthydro.ui.theme.*

// Dropdown for selecting a structure
@Composable
fun StructureSelectDropdown(
    structures: List<StructureItem>,
    selectedStructureId: String?,
    onStructureSelected: (String?) -> Unit,
    modifier: Modifier = Modifier,
    titleSuffix: String = "",
    showAllOption: Boolean = false,
    placeholder: String = "Select a structure",
    isHeadingStyle: Boolean = true
) {
    val isDark = LocalThemeIsDark.current
    var isExpanded by remember { mutableStateOf(false) }

    val currentStructure = structures.find { it.id == selectedStructureId }
    val displayTitle = when {
        currentStructure != null -> "${currentStructure.title}$titleSuffix"
        showAllOption && selectedStructureId == null -> "All structures"
        else -> placeholder
    }

    Box(
        modifier = modifier,
        contentAlignment = Alignment.Center
    ) {
        // Dropdown trigger
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Center,
            modifier = Modifier
                .clip(RoundedCornerShape(12.dp))
                .clickable { isExpanded = !isExpanded }
                .padding(horizontal = 8.dp, vertical = 4.dp)
        ) {
            if (currentStructure != null) {
                // Structure icon
                Box(
                    modifier = Modifier
                        .size(if (isHeadingStyle) 32.dp else 28.dp)
                        .clip(RoundedCornerShape(8.dp))
                        .background(currentStructure.themeColor),
                    contentAlignment = Alignment.Center
                ) {
                    SensorIcon(
                        iconType = currentStructure.iconType,
                        tint = Color.White,
                        size = if (isHeadingStyle) 18.dp else 16.dp
                    )
                }
                Spacer(modifier = Modifier.width(10.dp))
            } else if (showAllOption && selectedStructureId == null) {
                // All structures icon
                Box(
                    modifier = Modifier
                        .size(if (isHeadingStyle) 32.dp else 28.dp)
                        .clip(RoundedCornerShape(8.dp))
                        .background(ForestGreen),
                    contentAlignment = Alignment.Center
                ) {
                    SensorIcon(
                        iconType = "sensors",
                        tint = Color.White,
                        size = if (isHeadingStyle) 18.dp else 16.dp
                    )
                }
                Spacer(modifier = Modifier.width(10.dp))
            }

            Text(
                text = displayTitle,
                fontSize = if (isHeadingStyle) 20.sp else 15.sp,
                fontWeight = if (isHeadingStyle) FontWeight.Bold else FontWeight.SemiBold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )

            Spacer(modifier = Modifier.width(4.dp))

            Icon(
                imageVector = if (isExpanded) Icons.Filled.ArrowDropUp else Icons.Filled.ArrowDropDown,
                contentDescription = if (isExpanded) "Collapse structure selector" else "Expand structure selector",
                tint = if (isDark) TextSecondaryDark else ForestGreen,
                modifier = Modifier.size(if (isHeadingStyle) 24.dp else 20.dp)
            )
        }

        // Dropdown menu
        MaterialTheme(
            shapes = MaterialTheme.shapes.copy(
                extraSmall = RoundedCornerShape(14.dp)
            )
        ) {
            DropdownMenu(
                expanded = isExpanded,
                onDismissRequest = { isExpanded = false },
                modifier = Modifier
                    .widthIn(min = 250.dp, max = 340.dp)
                    .background(if (isDark) SurfaceDark else SurfaceWhite)
                    .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
            ) {
                // All structures option
                if (showAllOption) {
                    val isAllSelected = selectedStructureId == null
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
                                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                                    modifier = Modifier.weight(1f, fill = false)
                                ) {
                                    Box(
                                        modifier = Modifier
                                            .size(34.dp)
                                            .clip(RoundedCornerShape(9.dp))
                                            .background(ForestGreen),
                                        contentAlignment = Alignment.Center
                                    ) {
                                        SensorIcon(
                                            iconType = "sensors",
                                            tint = Color.White,
                                            size = 18.dp
                                        )
                                    }

                                    Column {
                                        Text(
                                            text = "All structures",
                                            fontSize = 15.sp,
                                            fontWeight = if (isAllSelected) FontWeight.Bold else FontWeight.SemiBold,
                                            color = if (isAllSelected) ForestGreen else if (isDark) TextPrimaryDark else TextPrimary
                                        )
                                        Text(
                                            text = "${structures.size} total structures",
                                            fontSize = 12.sp,
                                            color = if (isDark) TextSecondaryDark else TextSecondary
                                        )
                                    }
                                }

                                if (isAllSelected) {
                                    Spacer(modifier = Modifier.width(8.dp))
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
                            isExpanded = false
                            onStructureSelected(null)
                        }
                    )
                }

                // Individual structures
                structures.forEach { structure ->
                    val isSelected = structure.id == selectedStructureId

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
                                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                                    modifier = Modifier.weight(1f, fill = false)
                                ) {
                                    Box(
                                        modifier = Modifier
                                            .size(34.dp)
                                            .clip(RoundedCornerShape(9.dp))
                                            .background(structure.themeColor),
                                        contentAlignment = Alignment.Center
                                    ) {
                                        SensorIcon(
                                            iconType = structure.iconType,
                                            tint = Color.White,
                                            size = 18.dp
                                        )
                                    }

                                    Column {
                                        Text(
                                            text = structure.title,
                                            fontSize = 15.sp,
                                            fontWeight = if (isSelected) FontWeight.Bold else FontWeight.SemiBold,
                                            color = if (isSelected) ForestGreen else if (isDark) TextPrimaryDark else TextPrimary,
                                            maxLines = 1
                                        )
                                        if (ApiClient.isSignedIn()) {
                                            Text(
                                                text = SiteStore.nameFor(structure.siteId),
                                                fontSize = 12.sp,
                                                color = if (isDark) TextSecondaryDark else TextSecondary,
                                                maxLines = 1
                                            )
                                        }
                                    }
                                }

                                if (isSelected) {
                                    Spacer(modifier = Modifier.width(8.dp))
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
                            isExpanded = false
                            if (structure.id != selectedStructureId) {
                                onStructureSelected(structure.id)
                            }
                        }
                    )
                }
            }
        }
    }
}
