package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.ArrowDropUp
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Place
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.data.Site
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.ui.theme.*

/**
 * Custom dropdown component for selecting a Site in the Add / Edit Structure screen.
 * Shares the sleek Surface-based container styling of StructureTypeDropdown with rounded corners (14.dp).
 * Displays only the location icon, site name, and the count of structures assigned to that site.
 */
@Composable
fun SiteSelectDropdown(
    sites: List<Site>,
    selectedSiteId: String?,
    onSiteSelected: (String) -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    var isExpanded by remember { mutableStateOf(false) }

    val currentSite = sites.find { it.id == selectedSiteId } ?: sites.firstOrNull()
    val currentSiteName = currentSite?.name ?: "Select a Site"
    val currentStructureCount = currentSite?.let { site ->
        StructureStore.structures.count { it.siteId == site.id }
    } ?: 0

    Box(modifier = modifier.fillMaxWidth()) {
        // Selected Value Card
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
                    .padding(horizontal = 14.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    modifier = Modifier.weight(1f, fill = false)
                ) {
                    Box(
                        modifier = Modifier
                            .size(34.dp)
                            .clip(CircleShape)
                            .background(if (isDark) StatusGoodBgDark else MintBannerBg),
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(
                            imageVector = Icons.Filled.Place,
                            contentDescription = null,
                            tint = if (isDark) StatusGoodTextDark else ForestGreen,
                            modifier = Modifier.size(17.dp)
                        )
                    }

                    Text(
                        text = currentSiteName,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = if (isDark) TextPrimaryDark else ForestGreenDark,
                        maxLines = 1
                    )

                    // Structure Count Pill Badge
                    if (currentSite != null) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(8.dp))
                                .background(if (isDark) SurfaceDarkVariant else MintBannerBg)
                                .padding(horizontal = 7.dp, vertical = 2.dp)
                        ) {
                            Text(
                                text = "$currentStructureCount ${if (currentStructureCount == 1) "structure" else "structures"}",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) TextSecondaryDark else ForestGreen
                            )
                        }
                    }
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
                    .fillMaxWidth(0.94f)
                    .background(if (isDark) SurfaceDark else SurfaceWhite)
                    .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
            ) {
                sites.forEach { site ->
                    val isSelected = site.id == (currentSite?.id)
                    val siteStructureCount = StructureStore.structures.count { it.siteId == site.id }

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
                                            .size(32.dp)
                                            .clip(CircleShape)
                                            .background(if (isSelected) MintBannerBg else (if (isDark) SurfaceDarkVariant else BackgroundLight)),
                                        contentAlignment = Alignment.Center
                                    ) {
                                        Icon(
                                            imageVector = Icons.Filled.Place,
                                            contentDescription = null,
                                            tint = if (isSelected) ForestGreen else (if (isDark) TextSecondaryDark else TextSecondary),
                                            modifier = Modifier.size(16.dp)
                                        )
                                    }

                                    Text(
                                        text = site.name,
                                        fontSize = 14.sp,
                                        fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                        color = if (isSelected) ForestGreen else if (isDark) TextPrimaryDark else TextPrimary,
                                        maxLines = 1
                                    )

                                    Box(
                                        modifier = Modifier
                                            .clip(RoundedCornerShape(8.dp))
                                            .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                                            .padding(horizontal = 6.dp, vertical = 2.dp)
                                    ) {
                                        Text(
                                            text = "$siteStructureCount",
                                            fontSize = 11.sp,
                                            fontWeight = FontWeight.Bold,
                                            color = if (isDark) TextSecondaryDark else TextSecondary
                                        )
                                    }
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
                            onSiteSelected(site.id)
                            isExpanded = false
                        }
                    )
                }
            }
        }
    }
}

