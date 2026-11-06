package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.ReassignSiteDialog
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.AdminStore
import com.example.smarthydro.models.AdminSiteDto
import com.example.smarthydro.models.AdminUserDto
import com.example.smarthydro.models.AssignSiteOwnerDto
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun AdminSitesScreen(
    onNavigateBack: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    val sites = AdminStore.sites
    val operators = remember(AdminStore.users) {
        AdminStore.users.filter { it.role == "Operator" && it.status == "Approved" }
    }
    val isLoading = AdminStore.isLoading
    var isAssigning by remember { mutableStateOf(false) }
    var isRefreshing by remember { mutableStateOf(false) }

    val refreshRotation by animateFloatAsState(
        targetValue = if (isRefreshing) 360f else 0f,
        animationSpec = if (isRefreshing) {
            infiniteRepeatable(
                animation = tween(750, easing = LinearEasing),
                repeatMode = RepeatMode.Restart
            )
        } else {
            tween(200)
        },
        label = "rotation"
    )

    // Reassign owner dialog state
    var selectedSiteForReassign by remember { mutableStateOf<AdminSiteDto?>(null) }

    fun loadSitesAndOperators(isManualClick: Boolean = false) {
        if (isManualClick) {
            isRefreshing = true
        }
        scope.launch {
            val success = AdminStore.refresh()
            if (isManualClick) {
                isRefreshing = false
                if (success) {
                    Toast.makeText(context, "Sites refreshed", Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    LaunchedEffect(Unit) {
        AdminStore.loadFromCache()
        loadSitesAndOperators(isManualClick = false)
    }

    // Group sites by owner email matching MVC Sites.cshtml
    val sitesByOwner = remember(sites) {
        sites.groupBy { it.ownerEmail ?: "Unassigned" }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        TopHeaderBar()

        PullToRefresh(
            isRefreshing = isRefreshing,
            onRefresh = { loadSitesAndOperators(isManualClick = true) }
        ) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 16.dp),
                contentPadding = PaddingValues(top = 16.dp, bottom = 28.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Header Row
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Column {
                            Text(
                                text = "Sites by Operator",
                                modifier = Modifier.screenHeading(),
                                fontSize = 20.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) TextPrimaryDark else ForestGreenDark
                            )
                            Text(
                                text = "${sites.size} farm sites across all operators",
                                fontSize = 12.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }

                        IconButton(onClick = { loadSitesAndOperators(isManualClick = true) }, enabled = !isRefreshing) {
                            Icon(
                                imageVector = Icons.Filled.Refresh,
                                contentDescription = "Refresh",
                                tint = ForestGreen,
                                modifier = Modifier
                                    .size(24.dp)
                                    .graphicsLayer { rotationZ = refreshRotation }
                            )
                        }
                    }
                }

                if (isLoading) {
                    item {
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(vertical = 40.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            CircularProgressIndicator(color = ForestGreen)
                        }
                    }
                } else if (sites.isEmpty()) {
                    item {
                        Surface(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(16.dp))
                                .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp)),
                            color = MaterialTheme.colorScheme.surface
                        ) {
                            Column(
                                modifier = Modifier.padding(32.dp),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.spacedBy(12.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.Filled.HolidayVillage,
                                    contentDescription = null,
                                    tint = TextSecondary,
                                    modifier = Modifier.size(36.dp)
                                )
                                Text(
                                    text = "No Farm Sites Found",
                                    fontSize = 16.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = if (isDark) TextPrimaryDark else TextPrimary
                                )
                            }
                        }
                    }
                } else {
                    sitesByOwner.forEach { (ownerEmail, operatorSites) ->
                        item {
                            OperatorSiteGroupHeader(
                                ownerEmail = ownerEmail,
                                siteCount = operatorSites.size,
                                isDark = isDark
                            )
                        }

                        items(operatorSites, key = { it.id }) { site ->
                            AdminSiteCard(
                                site = site,
                                isDark = isDark,
                                onReassignClick = {
                                    selectedSiteForReassign = site
                                }
                            )
                        }
                    }
                }
            }
        }
    }

    // Custom Reassign Site Owner Dialog
    if (selectedSiteForReassign != null) {
        val site = selectedSiteForReassign!!
        ReassignSiteDialog(
            site = site,
            operators = operators,
            isDark = isDark,
            isAssigning = isAssigning,
            onDismiss = { selectedSiteForReassign = null },
            onConfirm = { targetEmail ->
                isAssigning = true
                scope.launch {
                    try {
                        val res = ApiClient.service.assignSiteOwner(
                            id = site.id,
                            dto = AssignSiteOwnerDto(ownerEmail = targetEmail)
                        )
                        if (res.isSuccessful) {
                            Toast.makeText(context, "Site owner updated successfully", Toast.LENGTH_SHORT).show()
                            selectedSiteForReassign = null
                            AdminStore.updateSiteOwner(site.id, targetEmail)
                            AdminStore.refresh()
                        } else {
                            Toast.makeText(context, "Could not update site owner", Toast.LENGTH_SHORT).show()
                        }
                    } catch (e: Exception) {
                        Toast.makeText(context, "Network error", Toast.LENGTH_SHORT).show()
                    } finally {
                        isAssigning = false
                    }
                }
            }
        )
    }
}

@Composable
private fun OperatorSiteGroupHeader(
    ownerEmail: String,
    siteCount: Int,
    isDark: Boolean
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 10.dp, bottom = 2.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Box(
                modifier = Modifier
                    .size(28.dp)
                    .clip(CircleShape)
                    .background(ForestGreen.copy(alpha = 0.15f)),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = Icons.Filled.Agriculture,
                    contentDescription = null,
                    tint = ForestGreen,
                    modifier = Modifier.size(16.dp)
                )
            }
            Text(
                text = ownerEmail,
                fontWeight = FontWeight.Bold,
                fontSize = 14.sp,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )
        }

        Surface(
            shape = RoundedCornerShape(12.dp),
            color = (if (isDark) CardBorderDark else CardBorder)
        ) {
            Text(
                text = "$siteCount site${if (siteCount > 1) "s" else ""}",
                fontSize = 11.sp,
                fontWeight = FontWeight.Medium,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp)
            )
        }
    }
}

@Composable
private fun AdminSiteCard(
    site: AdminSiteDto,
    isDark: Boolean,
    onReassignClick: () -> Unit
) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp)),
        color = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = site.name,
                        fontWeight = FontWeight.Bold,
                        fontSize = 16.sp,
                        color = if (isDark) TextPrimaryDark else TextPrimary
                    )
                    if (!site.location.isNullOrBlank()) {
                        Text(
                            text = site.location,
                            fontSize = 12.sp,
                            color = if (isDark) TextSecondaryDark else TextSecondary
                        )
                    }
                }

                OutlinedButton(
                    onClick = onReassignClick,
                    shape = RoundedCornerShape(8.dp),
                    contentPadding = PaddingValues(horizontal = 10.dp, vertical = 4.dp),
                    border = BorderStroke(1.dp, if (isDark) OutlinedButtonBorderDark else ForestGreen.copy(alpha = 0.5f)),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = if (isDark) AccentGreenDark else ForestGreen)
                ) {
                    Icon(Icons.Filled.SwapHoriz, contentDescription = null, modifier = Modifier.size(14.dp))
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("Reassign", fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                }
            }

            HorizontalDivider(color = if (isDark) CardDividerDark else CardDivider)

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(16.dp)
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(4.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Filled.GridView,
                            contentDescription = null,
                            tint = WaterBlue,
                            modifier = Modifier.size(16.dp)
                        )
                        Text(
                            text = "${site.structureCount} Structures",
                            fontSize = 12.sp,
                            color = if (isDark) TextSecondaryDark else TextSecondary
                        )
                    }

                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(4.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Filled.Engineering,
                            contentDescription = null,
                            tint = ForestGreen,
                            modifier = Modifier.size(16.dp)
                        )
                        Text(
                            text = "${site.workerCount} Workers",
                            fontSize = 12.sp,
                            color = if (isDark) TextSecondaryDark else TextSecondary
                        )
                    }
                }
            }
        }
    }
}