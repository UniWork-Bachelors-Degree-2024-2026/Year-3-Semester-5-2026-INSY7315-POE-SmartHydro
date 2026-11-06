package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.AdminStore
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.EditUserDialog
import com.example.smarthydro.components.SignOutConfirmDialog
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.components.UserActionDialog
import com.example.smarthydro.models.AdminSiteDto
import com.example.smarthydro.models.AdminUserDto
import com.example.smarthydro.models.PendingOperatorDto
import com.example.smarthydro.models.UpdateUserDto
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun AdminDashboardScreen(
    onNavigateToApprovals: () -> Unit,
    onNavigateToSites: () -> Unit,
    onLogout: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    val users = AdminStore.users
    val pendingOperators = AdminStore.pendingOperators
    val sites = AdminStore.sites
    val isLoading = AdminStore.isLoading
    val errorMessage = AdminStore.loadError

    // Search and filters
    var searchQuery by remember { mutableStateOf("") }
    var selectedRoleFilter by remember { mutableStateOf("All") }
    var selectedStatusFilter by remember { mutableStateOf("All") }

    // Dialog state for edit/action
    var selectedUserForAction by remember { mutableStateOf<AdminUserDto?>(null) }
    var showEditDialog by remember { mutableStateOf(false) }
    var showDeleteConfirmDialog by remember { mutableStateOf(false) }
    var showLogoutConfirmDialog by remember { mutableStateOf(false) }

    // Edit user form fields
    var editFullName by remember { mutableStateOf("") }
    var editRole by remember { mutableStateOf("Worker") }
    var isSubmittingAction by remember { mutableStateOf(false) }
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

    fun loadData(isManualClick: Boolean = false) {
        if (isManualClick) {
            isRefreshing = true
        }
        scope.launch {
            val success = AdminStore.refresh()
            if (isManualClick) {
                isRefreshing = false
                if (success) {
                    Toast.makeText(context, "Dashboard data refreshed", Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    LaunchedEffect(Unit) {
        AdminStore.loadFromCache()
        loadData(isManualClick = false)
    }

    // Calculated metrics matching MVC Admin Dashboard
    val totalUsers = users.size
    val approvedOperators = users.count { it.role == "Operator" && it.status == "Approved" }
    val workerCount = users.count { it.role == "Worker" }
    val pendingCount = pendingOperators.size
    val siteCount = sites.size
    val structureCount = sites.sumOf { it.structureCount }

    // Filtered users list
    val filteredUsers = remember(users, searchQuery, selectedRoleFilter, selectedStatusFilter) {
        users.filter { user ->
            val matchesSearch = searchQuery.isBlank() ||
                    user.email.contains(searchQuery, ignoreCase = true) ||
                    (user.fullName?.contains(searchQuery, ignoreCase = true) == true) ||
                    (user.farmName?.contains(searchQuery, ignoreCase = true) == true) ||
                    user.assignedSites.any { it.contains(searchQuery, ignoreCase = true) }

            val matchesRole = selectedRoleFilter == "All" || user.role.equals(selectedRoleFilter, ignoreCase = true)
            val matchesStatus = selectedStatusFilter == "All" || user.status.equals(selectedStatusFilter, ignoreCase = true)

            matchesSearch && matchesRole && matchesStatus
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
            onRefresh = { loadData(isManualClick = true) }
        ) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 16.dp),
                contentPadding = PaddingValues(top = 16.dp, bottom = 28.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Header Row: Admin Dashboard Title and Log Out
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column {
                            Text(
                                text = "Admin Dashboard",
                                modifier = Modifier.screenHeading(),
                                fontSize = 20.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) TextPrimaryDark else ForestGreenDark
                            )
                            Text(
                                text = "Platform oversight & user accounts",
                                fontSize = 12.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }

                        Row(
                            modifier = Modifier
                                .clip(RoundedCornerShape(20.dp))
                                .background(if (isDark) StatusCautionBgDark else StatusCautionBg)
                                .border(
                                    width = 1.dp,
                                    color = if (isDark) StatusCautionTextDark.copy(alpha = 0.35f) else StatusCautionText.copy(alpha = 0.25f),
                                    shape = RoundedCornerShape(20.dp)
                                )
                                .clickable { showLogoutConfirmDialog = true }
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
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) StatusCautionTextDark else StatusCautionText
                            )
                        }
                    }
                }

                // High priority banner: Pending Approvals (if any)
                if (pendingCount > 0) {
                    item {
                        Surface(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(12.dp))
                                .border(1.dp, StatusWarnText.copy(alpha = 0.4f), RoundedCornerShape(12.dp))
                                .clickable { onNavigateToApprovals() },
                            color = if (isDark) StatusWarnBgDark else StatusWarnBg
                        ) {
                            Row(
                                modifier = Modifier.padding(14.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(12.dp)
                            ) {
                                Box(
                                    modifier = Modifier
                                        .size(36.dp)
                                        .clip(CircleShape)
                                        .background(StatusWarnText),
                                    contentAlignment = Alignment.Center
                                ) {
                                    Icon(
                                        imageVector = Icons.Filled.PendingActions,
                                        contentDescription = null,
                                        tint = Color.White,
                                        modifier = Modifier.size(20.dp)
                                    )
                                }
                                Column(modifier = Modifier.weight(1f)) {
                                    Text(
                                        text = "$pendingCount Operator${if (pendingCount > 1) "s" else ""} Awaiting Approval",
                                        fontWeight = FontWeight.Bold,
                                        fontSize = 14.sp,
                                        color = if (isDark) StatusWarnTextDark else StatusWarnText
                                    )
                                    Text(
                                        text = "Tap to review and approve farm accounts",
                                        fontSize = 12.sp,
                                        color = if (isDark) TextSecondaryDark else TextSecondary
                                    )
                                }
                                Icon(
                                    imageVector = Icons.Filled.ChevronRight,
                                    contentDescription = null,
                                    tint = if (isDark) StatusWarnTextDark else StatusWarnText
                                )
                            }
                        }
                    }
                }

                // Metric Cards Grid (2 columns)
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            AdminStatCard(
                                title = "Total Users",
                                value = totalUsers.toString(),
                                subtitle = "$approvedOperators operators, $workerCount workers",
                                icon = Icons.Filled.People,
                                iconTint = ForestGreen,
                                modifier = Modifier.weight(1f),
                                isDark = isDark
                            )
                            AdminStatCard(
                                title = "Farm Sites",
                                value = siteCount.toString(),
                                subtitle = "$structureCount active structures",
                                icon = Icons.Filled.HolidayVillage,
                                iconTint = WaterBlue,
                                modifier = Modifier.weight(1f),
                                isDark = isDark,
                                onClick = onNavigateToSites
                            )
                        }

                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            AdminStatCard(
                                title = "Operators",
                                value = approvedOperators.toString(),
                                subtitle = "Approved farm managers",
                                icon = Icons.Filled.Agriculture,
                                iconTint = ForestGreenLight,
                                modifier = Modifier.weight(1f),
                                isDark = isDark
                            )
                            AdminStatCard(
                                title = "Pending",
                                value = pendingCount.toString(),
                                subtitle = if (pendingCount > 0) "Action required" else "All caught up",
                                icon = Icons.Filled.HowToReg,
                                iconTint = if (pendingCount > 0) StatusWarnText else TextSecondary,
                                modifier = Modifier.weight(1f),
                                isDark = isDark,
                                onClick = onNavigateToApprovals
                            )
                        }
                    }
                }

                // Search Bar & Filter Chips
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        // Elevated Search Field matching SmartHydro theme
                        Surface(
                            shape = RoundedCornerShape(14.dp),
                            color = MaterialTheme.colorScheme.surface,
                            border = androidx.compose.foundation.BorderStroke(
                                1.dp,
                                if (isDark) CardBorderDark else CardBorder
                            ),
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            OutlinedTextField(
                                value = searchQuery,
                                onValueChange = { searchQuery = it },
                                modifier = Modifier.fillMaxWidth(),
                                placeholder = {
                                    Text(
                                        "Search by name, email, or site...",
                                        fontSize = 13.sp,
                                        color = if (isDark) TextSecondaryDark else TextSecondary
                                    )
                                },
                                leadingIcon = {
                                    Icon(
                                        imageVector = Icons.Filled.Search,
                                        contentDescription = "Search",
                                        tint = if (isDark) ForestGreenLight else ForestGreen,
                                        modifier = Modifier.size(20.dp)
                                    )
                                },
                                trailingIcon = {
                                    if (searchQuery.isNotEmpty()) {
                                        IconButton(onClick = { searchQuery = "" }) {
                                            Icon(
                                                imageVector = Icons.Filled.Clear,
                                                contentDescription = "Clear",
                                                tint = if (isDark) TextSecondaryDark else TextSecondary,
                                                modifier = Modifier.size(18.dp)
                                            )
                                        }
                                    }
                                },
                                singleLine = true,
                                colors = OutlinedTextFieldDefaults.colors(
                                    focusedBorderColor = Color.Transparent,
                                    unfocusedBorderColor = Color.Transparent,
                                    cursorColor = ForestGreen
                                )
                            )
                        }

                        // Role filter pills with badge counts & theme highlights
                        val roleOptions = listOf(
                            "All" to users.size,
                            "Operator" to users.count { it.role.equals("Operator", ignoreCase = true) },
                            "Worker" to users.count { it.role.equals("Worker", ignoreCase = true) },
                            "Admin" to users.count { it.role.equals("Admin", ignoreCase = true) }
                        )

                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            roleOptions.forEach { (role, count) ->
                                val isSelected = selectedRoleFilter.equals(role, ignoreCase = true)

                                Surface(
                                    modifier = Modifier
                                        .weight(1f)
                                        .clip(RoundedCornerShape(12.dp))
                                        .border(
                                            width = 1.dp,
                                            color = if (isSelected) {
                                                if (isDark) ForestGreenLight else ForestGreen
                                            } else {
                                                if (isDark) CardBorderDark else CardBorder
                                            },
                                            shape = RoundedCornerShape(12.dp)
                                        )
                                        .clickable { selectedRoleFilter = role },
                                    color = if (isSelected) {
                                        if (isDark) NavSelectedPillDark else MintBannerBg
                                    } else {
                                        MaterialTheme.colorScheme.surface
                                    }
                                ) {
                                    Column(
                                        modifier = Modifier.padding(vertical = 8.dp),
                                        horizontalAlignment = Alignment.CenterHorizontally,
                                        verticalArrangement = Arrangement.Center
                                    ) {
                                        Text(
                                            text = role,
                                            fontSize = 12.sp,
                                            fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Medium,
                                            color = if (isSelected) {
                                                if (isDark) ForestGreenLight else ForestGreenDark
                                            } else {
                                                if (isDark) TextSecondaryDark else TextSecondary
                                            }
                                        )
                                        Text(
                                            text = count.toString(),
                                            fontSize = 11.sp,
                                            fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                            color = if (isSelected) {
                                                if (isDark) ForestGreenLight else ForestGreen
                                            } else {
                                                if (isDark) TextMuted else TextMuted
                                            }
                                        )
                                    }
                                }
                            }
                        }
                    }
                }

                // Section Header
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = "User Accounts (${filteredUsers.size})",
                            fontWeight = FontWeight.Bold,
                            fontSize = 16.sp,
                            color = if (isDark) TextPrimaryDark else ForestGreenDark
                        )

                        IconButton(
                            onClick = { loadData(isManualClick = true) },
                            enabled = !isRefreshing
                        ) {
                            Icon(
                                imageVector = Icons.Filled.Refresh,
                                contentDescription = "Refresh User Accounts",
                                tint = ForestGreen,
                                modifier = Modifier
                                    .size(20.dp)
                                    .graphicsLayer { rotationZ = refreshRotation }
                            )
                        }
                    }
                }

                // Empty state or Loading
                if (isLoading) {
                    item {
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(vertical = 32.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            CircularProgressIndicator(color = ForestGreen)
                        }
                    }
                } else if (filteredUsers.isEmpty()) {
                    item {
                        Surface(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(12.dp))
                                .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(12.dp)),
                            color = MaterialTheme.colorScheme.surface
                        ) {
                            Column(
                                modifier = Modifier.padding(24.dp),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.Filled.PersonSearch,
                                    contentDescription = null,
                                    tint = TextSecondary,
                                    modifier = Modifier.size(36.dp)
                                )
                                Text(
                                    text = "No users match your criteria",
                                    fontWeight = FontWeight.SemiBold,
                                    color = if (isDark) TextPrimaryDark else TextPrimary
                                )
                            }
                        }
                    }
                } else {
                    // User cards
                    items(filteredUsers, key = { it.id }) { user ->
                        AdminUserCard(
                            user = user,
                            isDark = isDark,
                            onActionClick = {
                                selectedUserForAction = user
                            }
                        )
                    }
                }
            }
        }
    }

    // Custom popup card for User Actions (triggered by 3-dots menu)
    if (selectedUserForAction != null) {
        val user = selectedUserForAction!!
        UserActionDialog(
            user = user,
            isDark = isDark,
            isSubmitting = isSubmittingAction,
            onDismiss = { selectedUserForAction = null },
            onEditDetails = {
                editFullName = user.fullName ?: ""
                editRole = user.role
                showEditDialog = true
            },
            onDeactivate = {
                isSubmittingAction = true
                scope.launch {
                    try {
                        val res = ApiClient.service.deactivateUser(user.id)
                        if (res.isSuccessful) {
                            Toast.makeText(context, "User deactivated", Toast.LENGTH_SHORT).show()
                            selectedUserForAction = null
                            loadData()
                        } else {
                            Toast.makeText(context, apiMessage(res.errorBody()?.string(), "Failed to deactivate"), Toast.LENGTH_LONG).show()
                        }
                    } catch (e: Exception) {
                        Toast.makeText(context, "Network error", Toast.LENGTH_SHORT).show()
                    } finally {
                        isSubmittingAction = false
                    }
                }
            },
            onReactivate = {
                isSubmittingAction = true
                scope.launch {
                    try {
                        val res = ApiClient.service.reactivateUser(user.id)
                        if (res.isSuccessful) {
                            Toast.makeText(context, "User reactivated", Toast.LENGTH_SHORT).show()
                            selectedUserForAction = null
                            loadData()
                        } else {
                            Toast.makeText(context, apiMessage(res.errorBody()?.string(), "Failed to reactivate"), Toast.LENGTH_LONG).show()
                        }
                    } catch (e: Exception) {
                        Toast.makeText(context, "Network error", Toast.LENGTH_SHORT).show()
                    } finally {
                        isSubmittingAction = false
                    }
                }
            },
            onDelete = {
                showDeleteConfirmDialog = true
            }
        )
    }

    // Custom Edit User Dialog
    if (showEditDialog && selectedUserForAction != null) {
        val user = selectedUserForAction!!
        EditUserDialog(
            user = user,
            isDark = isDark,
            isSubmitting = isSubmittingAction,
            onDismiss = { showEditDialog = false },
            onSave = { fullName, role ->
                isSubmittingAction = true
                scope.launch {
                    try {
                        val res = ApiClient.service.updateAdminUser(
                            id = user.id,
                            dto = UpdateUserDto(
                                fullName = fullName.takeIf { it.isNotBlank() },
                                farmName = user.farmName,
                                role = role
                            )
                        )
                        if (res.isSuccessful) {
                            Toast.makeText(context, "User updated successfully", Toast.LENGTH_SHORT).show()
                            showEditDialog = false
                            selectedUserForAction = null
                            loadData()
                        } else {
                            Toast.makeText(context, apiMessage(res.errorBody()?.string(), "Update failed"), Toast.LENGTH_LONG).show()
                        }
                    } catch (e: Exception) {
                        Toast.makeText(context, "Network error", Toast.LENGTH_SHORT).show()
                    } finally {
                        isSubmittingAction = false
                    }
                }
            }
        )
    }

    // Delete confirmation dialog
    if (showDeleteConfirmDialog && selectedUserForAction != null) {
        val user = selectedUserForAction!!
        ConfirmationDialog(
            title = "Delete Account?",
            message = "Are you sure you want to permanently delete ${user.email}? Any sites or structures owned will be removed.",
            confirmButtonText = if (isSubmittingAction) "Deleting…" else "Delete",
            dismissButtonText = "Cancel",
            isDestructive = true,
            isLoading = isSubmittingAction,
            onDismiss = { if (!isSubmittingAction) showDeleteConfirmDialog = false },
            onConfirm = {
                isSubmittingAction = true
                scope.launch {
                    try {
                        val res = ApiClient.service.deleteAdminUser(user.id)
                        if (res.isSuccessful) {
                            Toast.makeText(context, "Account deleted", Toast.LENGTH_SHORT).show()
                            showDeleteConfirmDialog = false
                            selectedUserForAction = null
                            loadData()
                        } else {
                            Toast.makeText(context, apiMessage(res.errorBody()?.string(), "Failed to delete account"), Toast.LENGTH_LONG).show()
                        }
                    } finally {
                        isSubmittingAction = false
                    }
                }
            }
        )
    }

    // Sign Out confirmation dialog
    if (showLogoutConfirmDialog) {
        SignOutConfirmDialog(
            userEmail = ApiClient.getSignedInEmail(),
            isDark = isDark,
            onDismiss = { showLogoutConfirmDialog = false },
            onConfirmSignOut = {
                showLogoutConfirmDialog = false
                onLogout()
            }
        )
    }
}

@Composable
private fun AdminStatCard(
    title: String,
    value: String,
    subtitle: String,
    icon: ImageVector,
    iconTint: Color,
    isDark: Boolean,
    modifier: Modifier = Modifier,
    onClick: (() -> Unit)? = null
) {
    Surface(
        modifier = modifier
            .clip(RoundedCornerShape(14.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
            .then(if (onClick != null) Modifier.clickable { onClick() } else Modifier),
        color = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = title,
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Medium,
                    color = if (isDark) TextSecondaryDark else TextSecondary
                )
                Box(
                    modifier = Modifier
                        .size(28.dp)
                        .clip(CircleShape)
                        .background(iconTint.copy(alpha = 0.12f)),
                    contentAlignment = Alignment.Center
                ) {
                    Icon(imageVector = icon, contentDescription = null, tint = iconTint, modifier = Modifier.size(16.dp))
                }
            }

            Text(
                text = value,
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else TextPrimary
            )

            Text(
                text = subtitle,
                fontSize = 11.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}

@Composable
private fun AdminUserCard(
    user: AdminUserDto,
    isDark: Boolean,
    onActionClick: () -> Unit
) {
    val statusColor = when (user.status) {
        "Approved" -> StatusGoodText
        "PendingApproval" -> StatusWarnText
        "Denied" -> StatusCautionText
        else -> TextSecondary
    }

    val statusBg = when (user.status) {
        "Approved" -> if (isDark) StatusGoodBgDark else StatusGoodBg
        "PendingApproval" -> if (isDark) StatusWarnBgDark else StatusWarnBg
        "Denied" -> if (isDark) StatusCautionBgDark else StatusCautionBg
        else -> if (isDark) SurfaceDarkVariant else MintBannerBg
    }

    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
            .clickable { onActionClick() },
        color = MaterialTheme.colorScheme.surface
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            // Avatar with Initial
            Box(
                modifier = Modifier
                    .size(42.dp)
                    .clip(CircleShape)
                    .background(
                        when (user.role) {
                            "Admin" -> ForestGreenDark
                            "Operator" -> ForestGreen
                            else -> WaterBlue
                        }
                    ),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = (user.fullName?.firstOrNull() ?: user.email.firstOrNull() ?: 'U').uppercase(),
                    color = Color.White,
                    fontWeight = FontWeight.Bold,
                    fontSize = 18.sp
                )
            }

            Column(modifier = Modifier.weight(1f)) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    Text(
                        text = user.fullName ?: user.email,
                        fontWeight = FontWeight.Bold,
                        fontSize = 15.sp,
                        color = if (isDark) TextPrimaryDark else TextPrimary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }

                if (user.fullName != null) {
                    Text(
                        text = user.email,
                        fontSize = 12.sp,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }

                Spacer(modifier = Modifier.height(4.dp))

                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    // Role tag
                    Surface(
                        shape = RoundedCornerShape(6.dp),
                        color = (if (isDark) TextSecondaryDark else ForestGreen).copy(alpha = 0.12f)
                    ) {
                        Text(
                            text = user.role,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = if (isDark) ForestGreenLight else ForestGreen,
                            modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp)
                        )
                    }

                    // Status tag
                    Surface(
                        shape = RoundedCornerShape(6.dp),
                        color = statusBg
                    ) {
                        Text(
                            text = if (user.status == "PendingApproval") "Pending" else user.status,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Medium,
                            color = statusColor,
                            modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp)
                        )
                    }

                    // Marks the signed-in admin's own account
                    if (user.isCurrentUser) {
                        Surface(
                            shape = RoundedCornerShape(6.dp),
                            color = (if (isDark) ForestGreenLight else ForestGreenDark).copy(alpha = 0.12f)
                        ) {
                            Text(
                                text = "You",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) ForestGreenLight else ForestGreenDark,
                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp)
                            )
                        }
                    }
                }
            }

            IconButton(onClick = onActionClick) {
                Icon(
                    imageVector = Icons.Filled.MoreVert,
                    contentDescription = "Manage",
                    tint = if (isDark) TextSecondaryDark else TextSecondary
                )
            }
        }
    }
}

private fun apiMessage(errorBody: String?, fallback: String): String {
    if (errorBody.isNullOrBlank()) return fallback

    return try {
        val message = org.json.JSONObject(errorBody).optString("message")
        if (message.isBlank()) fallback else message
    } catch (e: Exception) {
        fallback
    }
}