package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import androidx.compose.foundation.border
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.ArrowDropUp
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Group
import androidx.compose.material.icons.filled.Place
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Surface
import com.example.smarthydro.components.AddStructureButtonCard
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.CreateSiteDialog
import com.example.smarthydro.components.SignOutConfirmDialog
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.StructureCard
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.Site
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.models.CreateSiteDto
import com.example.smarthydro.models.UpdateSiteDto
import com.example.smarthydro.models.StructureItem
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun StructureScreen(
    onSelectStructure: (String) -> Unit,
    onEditStructure: (String) -> Unit,
    onNavigateToAddStructure: () -> Unit,
    onManageTeam: () -> Unit = {},
    onOpenThresholds: () -> Unit = {},
    onSignIn: () -> Unit = {},
    onLogout: () -> Unit = {},
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    var structurePendingDelete by remember { mutableStateOf<StructureItem?>(null) }
    var showLogoutDialog by remember { mutableStateOf(false) }
    var showCreateSiteDialog by remember { mutableStateOf(false) }
    var sitePendingEdit by remember { mutableStateOf<Site?>(null) }
    var sitePendingDelete by remember { mutableStateOf<Site?>(null) }
    var isDeletingSite by remember { mutableStateOf(false) }
    var isCreatingSite by remember { mutableStateOf(false) }
    var createSiteError by remember { mutableStateOf<String?>(null) }
    var selectedSiteIdFilter by remember { mutableStateOf<String?>(null) } // null means "All Sites"
    var isSiteDropdownExpanded by remember { mutableStateOf(false) }
    val signedInEmail = ApiClient.getSignedInEmail()

    val userRole = ApiClient.getSignedInRole()
    val canManage = userRole != "Worker"
    val isOperator = userRole == "Operator"

    // Loads both sites and structures simultaneously from cache (0ms), then checks API for updates in background
    LaunchedEffect(Unit) {
        SiteStore.loadFromCache()
        StructureStore.loadFromCache()
        SiteStore.refresh()
        StructureStore.refresh()
    }

    // Pull to refresh will reload a site and structure from the API or a farmer's local structures offline
    var isRefreshing by remember { mutableStateOf(false) }
    fun refresh() {
        if (isRefreshing) return
        isRefreshing = true
        scope.launch {
            try {
                SiteStore.refresh()
                StructureStore.refresh()
            } finally {
                isRefreshing = false
            }
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        TopHeaderBar()

        PullToRefresh(
            isRefreshing = isRefreshing,
            onRefresh = { refresh() }
        ) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 16.dp),
                contentPadding = PaddingValues(top = 16.dp, bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Header Bar with Title and Sign Out
                item {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(bottom = 2.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column {
                            Text(
                                text = "Choose a Structure",
                                modifier = Modifier.screenHeading(),
                                fontSize = 21.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) TextPrimaryDark else ForestGreenDark
                            )
                            Text(
                                text = if (ApiClient.isSignedIn()) "Select a structure to monitor and control" else "Local structures",
                                fontSize = 12.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }

                        if (ApiClient.isSignedIn()) {
                            Row(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(20.dp))
                                    .background(if (isDark) StatusCautionBgDark else StatusCautionBg)
                                    .border(
                                        width = 1.dp,
                                        color = if (isDark) StatusCautionTextDark.copy(alpha = 0.35f) else StatusCautionText.copy(alpha = 0.25f),
                                        shape = RoundedCornerShape(20.dp)
                                    )
                                    .clickable { showLogoutDialog = true }
                                    .padding(horizontal = 12.dp, vertical = 6.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(6.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.AutoMirrored.Filled.Logout,
                                    contentDescription = "Sign out",
                                    tint = if (isDark) StatusCautionTextDark else StatusCautionText,
                                    modifier = Modifier.size(15.dp)
                                )
                                Text(
                                    text = "Sign out",
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = if (isDark) StatusCautionTextDark else StatusCautionText
                                )
                            }
                        }
                    }
                }

                // Quick Operator Action Bar
                if (isOperator) {
                    item {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(12.dp))
                                .background(if (isDark) SurfaceDark else MintBannerBg.copy(alpha = 0.5f))
                                .border(
                                    1.dp,
                                    if (isDark) CardBorderDark else ForestGreenLight.copy(alpha = 0.25f),
                                    RoundedCornerShape(12.dp)
                                )
                                .clickable { onManageTeam() }
                                .padding(horizontal = 14.dp, vertical = 10.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.Filled.Group,
                                    contentDescription = null,
                                    tint = ForestGreen,
                                    modifier = Modifier.size(18.dp)
                                )
                                Text(
                                    text = "Manage Team & Workers",
                                    fontSize = 13.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                                )
                            }
                            Text(
                                text = "›",
                                modifier = Modifier.decorative(),
                                fontSize = 18.sp,
                                fontWeight = FontWeight.Bold,
                                color = ForestGreen
                            )
                        }
                    }

                    // Safe ranges of each reading
                    item {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(12.dp))
                                .background(if (isDark) SurfaceDark else MintBannerBg.copy(alpha = 0.5f))
                                .border(
                                    1.dp,
                                    if (isDark) CardBorderDark else ForestGreenLight.copy(alpha = 0.25f),
                                    RoundedCornerShape(12.dp)
                                )
                                .clickable { onOpenThresholds() }
                                .padding(horizontal = 14.dp, vertical = 10.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.Filled.Tune,
                                    contentDescription = null,
                                    tint = ForestGreen,
                                    modifier = Modifier.size(18.dp)
                                )
                                Text(
                                    text = "Thresholds",
                                    fontSize = 13.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                                )
                            }
                            Text(
                                text = "›",
                                modifier = Modifier.decorative(),
                                fontSize = 18.sp,
                                fontWeight = FontWeight.Bold,
                                color = ForestGreen
                            )
                        }
                    }
                }

                // Site Selection Dropdown & "+ Add Site" Action Button
                if (ApiClient.isSignedIn() && SiteStore.sites.isNotEmpty()) {
                    item {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text(
                                text = "SITE",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) TextMutedDark else TextMuted,
                                letterSpacing = 1.sp
                            )

                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(10.dp)
                            ) {
                                // Dropdown Trigger Container
                                Box(modifier = Modifier.weight(1f)) {
                                    Surface(
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .heightIn(min = 56.dp),
                                        shape = RoundedCornerShape(14.dp),
                                        color = if (isDark) SurfaceDark else SurfaceWhite,
                                        border = androidx.compose.foundation.BorderStroke(
                                            width = 1.5.dp,
                                            color = if (isSiteDropdownExpanded) ForestGreen
                                            else if (isDark) CardBorderDark
                                            else ForestGreen.copy(alpha = 0.5f)
                                        ),
                                        onClick = { isSiteDropdownExpanded = !isSiteDropdownExpanded }
                                    ) {
                                        Row(
                                            modifier = Modifier
                                                .fillMaxWidth()
                                                .padding(horizontal = 16.dp, vertical = 12.dp),
                                            horizontalArrangement = Arrangement.SpaceBetween,
                                            verticalAlignment = Alignment.CenterVertically
                                        ) {
                                            Row(
                                                verticalAlignment = Alignment.CenterVertically,
                                                horizontalArrangement = Arrangement.spacedBy(12.dp),
                                                modifier = Modifier.weight(1f, fill = false)
                                            ) {
                                                Box(
                                                    modifier = Modifier
                                                        .size(36.dp)
                                                        .clip(CircleShape)
                                                        .background(if (isDark) StatusGoodBgDark else MintBannerBg),
                                                    contentAlignment = Alignment.Center
                                                ) {
                                                    Icon(
                                                        imageVector = Icons.Filled.Place,
                                                        contentDescription = null,
                                                        tint = if (isDark) StatusGoodTextDark else ForestGreen,
                                                        modifier = Modifier.size(18.dp)
                                                    )
                                                }

                                                val currentSiteName = if (selectedSiteIdFilter == null) {
                                                    "All Sites"
                                                } else {
                                                    SiteStore.sites.find { it.id == selectedSiteIdFilter }?.name ?: "All Sites"
                                                }

                                                Text(
                                                    text = currentSiteName,
                                                    fontSize = 15.sp,
                                                    fontWeight = FontWeight.SemiBold,
                                                    color = if (isDark) TextPrimaryDark else ForestGreenDark,
                                                    maxLines = 1
                                                )

                                                // Count badge
                                                val count = if (selectedSiteIdFilter == null) {
                                                    StructureStore.structures.size
                                                } else {
                                                    StructureStore.structures.count { it.siteId == selectedSiteIdFilter }
                                                }
                                                Box(
                                                    modifier = Modifier
                                                        .clip(RoundedCornerShape(10.dp))
                                                        .background(if (isDark) SurfaceDarkVariant else MintBannerBg)
                                                        .padding(horizontal = 8.dp, vertical = 3.dp)
                                                ) {
                                                    Text(
                                                        text = "$count",
                                                        fontSize = 12.sp,
                                                        fontWeight = FontWeight.Bold,
                                                        color = if (isDark) TextSecondaryDark else ForestGreen
                                                    )
                                                }
                                            }

                                            Icon(
                                                imageVector = if (isSiteDropdownExpanded) Icons.Filled.ArrowDropUp else Icons.Filled.ArrowDropDown,
                                                contentDescription = "Toggle site selector",
                                                tint = if (isDark) TextSecondaryDark else ForestGreen,
                                                modifier = Modifier.size(26.dp)
                                            )
                                        }
                                    }

                                    // Popup Dropdown Menu
                                    MaterialTheme(
                                        shapes = MaterialTheme.shapes.copy(
                                            extraSmall = RoundedCornerShape(14.dp)
                                        )
                                    ) {
                                        DropdownMenu(
                                            expanded = isSiteDropdownExpanded,
                                            onDismissRequest = { isSiteDropdownExpanded = false },
                                            modifier = Modifier
                                                .fillMaxWidth(0.92f)
                                                .background(if (isDark) SurfaceDark else SurfaceWhite)
                                                .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
                                        ) {
                                            // "All Sites" Option
                                            val isAllSelected = selectedSiteIdFilter == null
                                            DropdownMenuItem(
                                                modifier = Modifier.defaultMinSize(minHeight = 52.dp),
                                                text = {
                                                    Row(
                                                        modifier = Modifier
                                                            .fillMaxWidth()
                                                            .padding(vertical = 6.dp),
                                                        verticalAlignment = Alignment.CenterVertically,
                                                        horizontalArrangement = Arrangement.SpaceBetween
                                                    ) {
                                                        Row(
                                                            verticalAlignment = Alignment.CenterVertically,
                                                            horizontalArrangement = Arrangement.spacedBy(12.dp)
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
                                                                    modifier = Modifier.size(18.dp)
                                                                )
                                                            }
                                                            Text(
                                                                text = "All Sites",
                                                                fontSize = 15.sp,
                                                                fontWeight = if (isAllSelected) FontWeight.Bold else FontWeight.Medium,
                                                                color = if (isAllSelected) ForestGreen else if (isDark) TextPrimaryDark else TextPrimary
                                                            )
                                                            Box(
                                                                modifier = Modifier
                                                                    .clip(RoundedCornerShape(8.dp))
                                                                    .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                                                                    .padding(horizontal = 7.dp, vertical = 3.dp)
                                                            ) {
                                                                Text(
                                                                    text = "${StructureStore.structures.size}",
                                                                    fontSize = 12.sp,
                                                                    fontWeight = FontWeight.Bold,
                                                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                                                )
                                                            }
                                                        }

                                                        if (isAllSelected) {
                                                            Icon(
                                                                imageVector = Icons.Filled.Check,
                                                                contentDescription = "Selected",
                                                                tint = ForestGreen,
                                                                modifier = Modifier.size(20.dp)
                                                            )
                                                        }
                                                    }
                                                },
                                                onClick = {
                                                    selectedSiteIdFilter = null
                                                    isSiteDropdownExpanded = false
                                                }
                                            )

                                            // Individual Sites
                                            SiteStore.sites.forEach { site ->
                                                val isSelected = selectedSiteIdFilter == site.id
                                                val count = StructureStore.structures.count { it.siteId == site.id }
                                                DropdownMenuItem(
                                                    modifier = Modifier.defaultMinSize(minHeight = 56.dp),
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
                                                                horizontalArrangement = Arrangement.spacedBy(12.dp),
                                                                modifier = Modifier.weight(1f, fill = false)
                                                            ) {
                                                                Box(
                                                                    modifier = Modifier
                                                                        .size(34.dp)
                                                                        .clip(CircleShape)
                                                                        .background(if (isSelected) MintBannerBg else (if (isDark) SurfaceDarkVariant else BackgroundLight)),
                                                                    contentAlignment = Alignment.Center
                                                                ) {
                                                                    Icon(
                                                                        imageVector = Icons.Filled.Place,
                                                                        contentDescription = null,
                                                                        tint = if (isSelected) ForestGreen else (if (isDark) TextSecondaryDark else TextSecondary),
                                                                        modifier = Modifier.size(18.dp)
                                                                    )
                                                                }
                                                                Text(
                                                                    text = site.name,
                                                                    fontSize = 15.sp,
                                                                    fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Medium,
                                                                    color = if (isSelected) ForestGreen else if (isDark) TextPrimaryDark else TextPrimary
                                                                )
                                                                Box(
                                                                    modifier = Modifier
                                                                        .clip(RoundedCornerShape(8.dp))
                                                                        .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                                                                        .padding(horizontal = 7.dp, vertical = 3.dp)
                                                                ) {
                                                                    Text(
                                                                        text = "$count",
                                                                        fontSize = 12.sp,
                                                                        fontWeight = FontWeight.Bold,
                                                                        color = if (isDark) TextSecondaryDark else TextSecondary
                                                                    )
                                                                }
                                                            }

                                                            Row(
                                                                verticalAlignment = Alignment.CenterVertically,
                                                                horizontalArrangement = Arrangement.spacedBy(4.dp)
                                                            ) {
                                                                if (isSelected) {
                                                                    Icon(
                                                                        imageVector = Icons.Filled.Check,
                                                                        contentDescription = "Selected",
                                                                        tint = ForestGreen,
                                                                        modifier = Modifier
                                                                            .padding(end = 4.dp)
                                                                            .size(20.dp)
                                                                    )
                                                                }

                                                                if (canManage) {
                                                                    Box(
                                                                        modifier = Modifier
                                                                            .size(48.dp)
                                                                            .clip(RoundedCornerShape(10.dp))
                                                                            .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                                                                            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(10.dp))
                                                                            .clickable {
                                                                                isSiteDropdownExpanded = false
                                                                                sitePendingEdit = site
                                                                                createSiteError = null
                                                                                showCreateSiteDialog = true
                                                                            },
                                                                        contentAlignment = Alignment.Center
                                                                    ) {
                                                                        Icon(
                                                                            imageVector = Icons.Filled.Edit,
                                                                            contentDescription = "Edit Site ${site.name}",
                                                                            tint = if (isDark) TextSecondaryDark else TextSecondary,
                                                                            modifier = Modifier.size(19.dp)
                                                                        )
                                                                    }

                                                                    val canDelete = count == 0
                                                                    Box(
                                                                        modifier = Modifier
                                                                            .size(48.dp)
                                                                            .clip(RoundedCornerShape(10.dp))
                                                                            .background(
                                                                                if (canDelete) {
                                                                                    if (isDark) SurfaceDarkVariant else BackgroundLight
                                                                                } else {
                                                                                    (if (isDark) SurfaceDarkVariant else BackgroundLight).copy(alpha = 0.5f)
                                                                                }
                                                                            )
                                                                            .border(
                                                                                1.dp,
                                                                                if (canDelete) {
                                                                                    if (isDark) CardBorderDark else CardBorder
                                                                                } else {
                                                                                    (if (isDark) CardBorderDark else CardBorder).copy(alpha = 0.4f)
                                                                                },
                                                                                RoundedCornerShape(10.dp)
                                                                            )
                                                                            .clickable(enabled = canDelete) {
                                                                                isSiteDropdownExpanded = false
                                                                                sitePendingDelete = site
                                                                            },
                                                                        contentAlignment = Alignment.Center
                                                                    ) {
                                                                        Icon(
                                                                            imageVector = Icons.Filled.DeleteOutline,
                                                                            contentDescription = if (canDelete) "Delete Site ${site.name}" else "Cannot delete: site has structures",
                                                                            tint = if (canDelete) {
                                                                                if (isDark) StatusCautionTextDark else StatusCautionText
                                                                            } else {
                                                                                (if (isDark) TextMutedDark else TextMuted).copy(alpha = 0.35f)
                                                                            },
                                                                            modifier = Modifier.size(19.dp)
                                                                        )
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    },
                                                    onClick = {
                                                        selectedSiteIdFilter = site.id
                                                        isSiteDropdownExpanded = false
                                                    }
                                                )
                                            }
                                        }
                                    }
                                }

                                // "+ Add Site" Action Button next to dropdown
                                if (canManage) {
                                    Surface(
                                        modifier = Modifier.size(56.dp),
                                        shape = RoundedCornerShape(14.dp),
                                        color = ForestGreen,
                                        border = androidx.compose.foundation.BorderStroke(1.dp, ForestGreenLight),
                                        onClick = {
                                            createSiteError = null
                                            showCreateSiteDialog = true
                                        }
                                    ) {
                                        Box(
                                            contentAlignment = Alignment.Center,
                                            modifier = Modifier.fillMaxSize()
                                        ) {
                                            Icon(
                                                imageVector = Icons.Filled.Add,
                                                contentDescription = "Add New Site",
                                                tint = Color.White,
                                                modifier = Modifier.size(24.dp)
                                            )
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Structure List
                if (ApiClient.isSignedIn()) {
                    if (StructureStore.isLoading && StructureStore.structures.isEmpty()) {
                        item {
                            Text(
                                text = "Loading structures…",
                                fontSize = 14.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary,
                                modifier = Modifier.padding(vertical = 24.dp)
                            )
                        }
                    } else if (StructureStore.loadError != null) {
                        item {
                            Text(
                                text = StructureStore.loadError ?: "",
                                fontSize = 14.sp,
                                color = if (isDark) StatusCautionTextDark else StatusCautionText,
                                modifier = Modifier.padding(vertical = 24.dp)
                            )
                        }
                    } else {
                        val filteredStructures = if (selectedSiteIdFilter != null) {
                            StructureStore.structures.filter { it.siteId == selectedSiteIdFilter }
                        } else {
                            StructureStore.structures
                        }

                        if (filteredStructures.isEmpty()) {
                            item {
                                Box(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(vertical = 32.dp),
                                    contentAlignment = Alignment.Center
                                ) {
                                    Text(
                                        text = if (selectedSiteIdFilter != null) {
                                            "No structures in this site yet."
                                        } else {
                                            "No structures found."
                                        },
                                        fontSize = 14.sp,
                                        color = if (isDark) TextSecondaryDark else TextSecondary
                                    )
                                }
                            }
                        } else {
                            // When viewing all sites, group with readable headers; when a site is filtered, show clean list
                            if (selectedSiteIdFilter != null) {
                                items(filteredStructures, key = { it.id }) { structure ->
                                    StructureCard(
                                        structure = structure,
                                        canManage = canManage,
                                        onEdit = { onEditStructure(structure.id) },
                                        onSelect = {
                                            onSelectStructure(structure.id)
                                            Toast.makeText(context, "Selected ${structure.title}", Toast.LENGTH_SHORT).show()
                                        },
                                        onDelete = { structurePendingDelete = structure }
                                    )
                                }
                            } else {
                                val bySite = filteredStructures.groupBy { it.siteId }
                                SiteStore.sites.forEach { site ->
                                    val siteStructures = bySite[site.id].orEmpty()
                                    if (siteStructures.isNotEmpty()) {
                                        item(key = "site_header_${site.id}") {
                                            Row(
                                                modifier = Modifier
                                                    .fillMaxWidth()
                                                    .padding(top = 6.dp, bottom = 2.dp),
                                                horizontalArrangement = Arrangement.SpaceBetween,
                                                verticalAlignment = Alignment.CenterVertically
                                            ) {
                                                Row(
                                                    verticalAlignment = Alignment.CenterVertically,
                                                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                                                ) {
                                                    Icon(
                                                        imageVector = Icons.Filled.Place,
                                                        contentDescription = null,
                                                        tint = ForestGreen,
                                                        modifier = Modifier.size(16.dp)
                                                    )
                                                    Text(
                                                        text = site.name,
                                                        fontSize = 15.sp,
                                                        fontWeight = FontWeight.Bold,
                                                        color = if (isDark) TextPrimaryDark else ForestGreenDark
                                                    )
                                                }

                                                Text(
                                                    text = "${siteStructures.size} ${if (siteStructures.size == 1) "structure" else "structures"}",
                                                    fontSize = 12.sp,
                                                    fontWeight = FontWeight.Medium,
                                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                                )
                                            }
                                        }

                                        items(siteStructures, key = { it.id }) { structure ->
                                            StructureCard(
                                                structure = structure,
                                                canManage = canManage,
                                                onEdit = { onEditStructure(structure.id) },
                                                onSelect = {
                                                    onSelectStructure(structure.id)
                                                    Toast.makeText(context, "Selected ${structure.title}", Toast.LENGTH_SHORT).show()
                                                },
                                                onDelete = { structurePendingDelete = structure }
                                            )
                                        }
                                    }
                                }

                                // Any unassigned structures
                                val unassigned = filteredStructures.filter { it.siteId.isNullOrBlank() }
                                if (unassigned.isNotEmpty()) {
                                    item(key = "site_header_unassigned") {
                                        Text(
                                            text = "Unassigned Structures",
                                            fontSize = 14.sp,
                                            fontWeight = FontWeight.Bold,
                                            color = if (isDark) TextMutedDark else TextMuted,
                                            modifier = Modifier.padding(top = 6.dp, bottom = 2.dp)
                                        )
                                    }
                                    items(unassigned, key = { it.id }) { structure ->
                                        StructureCard(
                                            structure = structure,
                                            canManage = canManage,
                                            onEdit = { onEditStructure(structure.id) },
                                            onSelect = {
                                                onSelectStructure(structure.id)
                                                Toast.makeText(context, "Selected ${structure.title}", Toast.LENGTH_SHORT).show()
                                            },
                                            onDelete = { structurePendingDelete = structure }
                                        )
                                    }
                                }
                            }
                        }
                    }
                } else {
                    items(StructureStore.structures, key = { it.id }) { structure ->
                        StructureCard(
                            structure = structure,
                            canManage = canManage,
                            onEdit = { onEditStructure(structure.id) },
                            onSelect = {
                                onSelectStructure(structure.id)
                                Toast.makeText(context, "Selected ${structure.title}", Toast.LENGTH_SHORT).show()
                            },
                            onDelete = { structurePendingDelete = structure }
                        )
                    }
                }

                if (canManage) {
                    item {
                        AddStructureButtonCard(
                            onClick = {
                                onNavigateToAddStructure()
                            }
                        )
                    }
                }
            }
        }
    }

    // Modal Dialog to Create or Edit a Site
    if (showCreateSiteDialog) {
        val isEditing = sitePendingEdit != null
        CreateSiteDialog(
            isDark = isDark,
            isLoading = isCreatingSite,
            initialSiteName = sitePendingEdit?.name ?: "",
            isEdit = isEditing,
            serverErrorMessage = createSiteError,
            onDismiss = {
                showCreateSiteDialog = false
                sitePendingEdit = null
                createSiteError = null
            },
            onSaveSite = { name ->
                isCreatingSite = true
                createSiteError = null
                scope.launch {
                    try {
                        if (isEditing && sitePendingEdit != null) {
                            val targetId = sitePendingEdit!!.id
                            val response = ApiClient.service.updateSite(
                                targetId,
                                UpdateSiteDto(name = name, location = null)
                            )
                            if (response.isSuccessful && response.body() != null) {
                                SiteStore.refresh()
                                showCreateSiteDialog = false
                                sitePendingEdit = null
                                createSiteError = null
                                Toast.makeText(context, "Site updated to \"$name\".", Toast.LENGTH_SHORT).show()
                            } else {
                                val code = response.code()
                                val rawErr = response.errorBody()?.string() ?: ""
                                createSiteError = when {
                                    code == 409 || rawErr.contains("already exists", ignoreCase = true) || rawErr.contains("duplicate", ignoreCase = true) ->
                                        "A site with this name already exists. Please choose a different name."
                                    rawErr.isNotBlank() && rawErr.length < 80 && !rawErr.contains("<html", ignoreCase = true) ->
                                        rawErr
                                    else -> "Couldn't update the site. Check the name and try again."
                                }
                            }
                        } else {
                            val response = ApiClient.service.createSite(
                                CreateSiteDto(name = name, location = null)
                            )
                            if (response.isSuccessful && response.body() != null) {
                                val newSite = response.body()!!
                                SiteStore.refresh()
                                selectedSiteIdFilter = newSite.id
                                showCreateSiteDialog = false
                                sitePendingEdit = null
                                createSiteError = null
                                Toast.makeText(context, "Site \"$name\" created successfully!", Toast.LENGTH_SHORT).show()
                            } else {
                                val code = response.code()
                                val rawErr = response.errorBody()?.string() ?: ""
                                createSiteError = when {
                                    code == 409 || rawErr.contains("already exists", ignoreCase = true) || rawErr.contains("duplicate", ignoreCase = true) ->
                                        "A site with this name already exists. Please choose a different name."
                                    rawErr.isNotBlank() && rawErr.length < 80 && !rawErr.contains("<html", ignoreCase = true) ->
                                        rawErr
                                    else -> "Couldn't create the site. Please check the name and try again."
                                }
                            }
                        }
                    } catch (e: Exception) {
                        createSiteError = "Network error: couldn't reach the server. Please try again."
                    } finally {
                        isCreatingSite = false
                    }
                }
            }
        )
    }

    // Confirmation Dialog before deleting a site
    sitePendingDelete?.let { siteToDelete ->
        ConfirmationDialog(
            title = "Delete Site",
            message = "Are you sure you want to delete \"${siteToDelete.name}\"? This action cannot be undone.",
            confirmButtonText = if (isDeletingSite) "Deleting…" else "Yes, delete",
            dismissButtonText = "Cancel",
            isDestructive = true,
            isLoading = isDeletingSite,
            onDismiss = { if (!isDeletingSite) sitePendingDelete = null },
            onConfirm = {
                isDeletingSite = true
                scope.launch {
                    try {
                        val response = ApiClient.service.deleteSite(siteToDelete.id)
                        if (response.isSuccessful) {
                            SiteStore.refresh()
                            if (selectedSiteIdFilter == siteToDelete.id) {
                                selectedSiteIdFilter = null
                            }
                            Toast.makeText(context, "Site \"${siteToDelete.name}\" deleted.", Toast.LENGTH_SHORT).show()
                            sitePendingDelete = null
                        } else {
                            val err = response.errorBody()?.string() ?: ""
                            val msg = if (err.contains("structure", ignoreCase = true) || response.code() == 400) {
                                "Cannot delete \"${siteToDelete.name}\" because it still has structures assigned."
                            } else {
                                "Couldn't delete site. Please try again."
                            }
                            Toast.makeText(context, msg, Toast.LENGTH_LONG).show()
                            sitePendingDelete = null
                        }
                    } catch (e: Exception) {
                        Toast.makeText(context, "Network error: check connection.", Toast.LENGTH_SHORT).show()
                        sitePendingDelete = null
                    } finally {
                        isDeletingSite = false
                    }
                }
            }
        )
    }

    // Asks before deleting a structure, since it can't be undone
    structurePendingDelete?.let { structure ->
        ConfirmationDialog(
            title = "Delete Structure",
            annotatedMessage = buildAnnotatedString {
                withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append("\"${structure.title}\"") }
                append(" will be permanently deleted. This can't be undone.")
            },
            confirmButtonText = "Yes, delete",
            dismissButtonText = "Cancel",
            isDestructive = true,
            onDismiss = { structurePendingDelete = null },
            onConfirm = {
                val toDelete = structure
                structurePendingDelete = null
                scope.launch {
                    val deleted = StructureStore.remove(toDelete.id)
                    val message = if (deleted) {
                        "\"${toDelete.title}\" was deleted."
                    } else {
                        "Couldn't delete \"${toDelete.title}\". Check your connection and try again."
                    }
                    Toast.makeText(context, message, Toast.LENGTH_SHORT).show()
                }
            }
        )
    }

    // Confirms before signing out, since it drops back to the login screen
    if (showLogoutDialog) {
        SignOutConfirmDialog(
            userEmail = ApiClient.getSignedInEmail(),
            isDark = isDark,
            onDismiss = { showLogoutDialog = false },
            onConfirmSignOut = {
                showLogoutDialog = false
                onLogout()
            }
        )
    }
}