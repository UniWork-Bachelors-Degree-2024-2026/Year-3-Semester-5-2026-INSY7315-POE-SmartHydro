package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.smarthydro.models.AdminUserDto
import com.example.smarthydro.ui.theme.*

/**
 * A customized, modern popup card that displays detailed user info and administrative actions
 * when clicking the 3-dots action menu on a user card in the Admin Dashboard.
 */
@Composable
fun UserActionDialog(
    user: AdminUserDto,
    isDark: Boolean,
    isSubmitting: Boolean,
    onDismiss: () -> Unit,
    onEditDetails: () -> Unit,
    onDeactivate: () -> Unit,
    onReactivate: () -> Unit,
    onDelete: () -> Unit
) {
    val statusColor = when (user.status) {
        "Approved" -> if (isDark) StatusGoodTextDark else StatusGoodText
        "PendingApproval" -> if (isDark) StatusWarnTextDark else StatusWarnText
        "Denied", "Disabled" -> if (isDark) StatusCautionTextDark else StatusCautionText
        else -> if (isDark) TextSecondaryDark else TextSecondary
    }

    val statusBg = when (user.status) {
        "Approved" -> if (isDark) StatusGoodBgDark else StatusGoodBg
        "PendingApproval" -> if (isDark) StatusWarnBgDark else StatusWarnBg
        "Denied", "Disabled" -> if (isDark) StatusCautionBgDark else StatusCautionBg
        else -> if (isDark) SurfaceDarkVariant else MintBannerBg
    }

    Dialog(onDismissRequest = { if (!isSubmitting) onDismiss() }) {
        Card(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 16.dp),
            shape = RoundedCornerShape(24.dp),
            colors = CardDefaults.cardColors(
                containerColor = if (isDark) SurfaceDark else SurfaceWhite
            ),
            border = androidx.compose.foundation.BorderStroke(
                1.dp,
                if (isDark) CardBorderDark else CardBorder
            ),
            elevation = CardDefaults.cardElevation(defaultElevation = 8.dp)
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Top Header Row: User Identity + Close Button
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                        modifier = Modifier.weight(1f)
                    ) {
                        // User Avatar
                        Box(
                            modifier = Modifier
                                .size(46.dp)
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
                                fontSize = 20.sp
                            )
                        }

                        Column {
                            Text(
                                text = user.fullName ?: user.email,
                                fontWeight = FontWeight.Bold,
                                fontSize = 17.sp,
                                color = if (isDark) TextPrimaryDark else TextPrimary,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                            Text(
                                text = user.email,
                                fontSize = 12.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                    }

                    // Close "X" Button
                    IconButton(
                        onClick = onDismiss,
                        modifier = Modifier
                            .size(32.dp)
                            .clip(CircleShape)
                            .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                    ) {
                        Icon(
                            imageVector = Icons.Filled.Close,
                            contentDescription = "Close",
                            tint = if (isDark) TextSecondaryDark else TextSecondary,
                            modifier = Modifier.size(18.dp)
                        )
                    }
                }

                // Metadata Pill Row (Role & Status Badges)
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // Role Tag
                    Surface(
                        shape = RoundedCornerShape(8.dp),
                        color = (if (isDark) TextSecondaryDark else ForestGreen).copy(alpha = 0.12f)
                    ) {
                        Text(
                            text = "Role: ${user.role}",
                            fontSize = 11.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = if (isDark) ForestGreenLight else ForestGreen,
                            modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp)
                        )
                    }

                    // Status Tag
                    Surface(
                        shape = RoundedCornerShape(8.dp),
                        color = statusBg
                    ) {
                        Text(
                            text = "Status: ${if (user.status == "PendingApproval") "Pending" else user.status}",
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Medium,
                            color = statusColor,
                            modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp)
                        )
                    }

                    if (user.isProtected) {
                        Surface(
                            shape = RoundedCornerShape(8.dp),
                            color = (if (isDark) ForestGreenLight else ForestGreenDark).copy(alpha = 0.12f)
                        ) {
                            Text(
                                text = if (user.isCurrentUser) "You" else "Protected",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) ForestGreenLight else ForestGreenDark,
                                modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp)
                            )
                        }
                    }
                }

                // Farm & Assigned Sites information (if present)
                if (!user.farmName.isNullOrBlank() || user.assignedSites.isNotEmpty()) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(12.dp))
                            .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                            .padding(12.dp),
                        verticalArrangement = Arrangement.spacedBy(4.dp)
                    ) {
                        if (!user.farmName.isNullOrBlank()) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Icon(
                                    imageVector = Icons.Filled.Home,
                                    contentDescription = null,
                                    tint = if (isDark) TextSecondaryDark else TextSecondary,
                                    modifier = Modifier.size(14.dp)
                                )
                                Spacer(modifier = Modifier.width(6.dp))
                                Text(
                                    text = "Farm: ${user.farmName}",
                                    fontSize = 12.sp,
                                    color = if (isDark) TextPrimaryDark else TextPrimary,
                                    fontWeight = FontWeight.Medium
                                )
                            }
                        }

                        if (user.assignedSites.isNotEmpty()) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Icon(
                                    imageVector = Icons.Filled.Place,
                                    contentDescription = null,
                                    tint = if (isDark) TextSecondaryDark else TextSecondary,
                                    modifier = Modifier.size(14.dp)
                                )
                                Spacer(modifier = Modifier.width(6.dp))
                                Text(
                                    text = "Sites: ${user.assignedSites.joinToString(", ")}",
                                    fontSize = 12.sp,
                                    color = if (isDark) TextPrimaryDark else TextPrimary,
                                    maxLines = 2,
                                    overflow = TextOverflow.Ellipsis
                                )
                            }
                        }
                    }
                }

                HorizontalDivider(
                    color = if (isDark) CardBorderDark else CardBorder,
                    thickness = 1.dp
                )

                // Actions Section
                Text(
                    text = "ACTIONS",
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (isDark) TextSecondaryDark else TextSecondary,
                    letterSpacing = 0.5.sp
                )

                Column(
                    modifier = Modifier.fillMaxWidth(),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    // Action 1: Edit Account Details & Role (hidden if user is Denied)
                    if (user.status != "Denied") {
                        ActionCardButton(
                            icon = Icons.Filled.Edit,
                            title = "Edit Details & Role",
                            subtitle = "Update user",
                            tint = if (isDark) ForestGreenLight else ForestGreen,
                            bg = if (isDark) SurfaceDarkVariant else MintBannerBg,
                            onClick = onEditDetails,
                            enabled = !isSubmitting
                        )
                    }

                    // Action 2: Deactivate / Reactivate
                    if (user.status == "Approved" && !user.isProtected) {
                        ActionCardButton(
                            icon = Icons.Filled.Block,
                            title = "Deactivate Account",
                            subtitle = "Temporarily suspend user access",
                            tint = StatusCautionText,
                            bg = if (isDark) StatusCautionBgDark else StatusCautionBg,
                            onClick = onDeactivate,
                            enabled = !isSubmitting
                        )
                    } else if (user.status == "Disabled" && !user.isProtected) {
                        ActionCardButton(
                            icon = Icons.Filled.CheckCircle,
                            title = "Reactivate Account",
                            subtitle = "Restore user access to the system",
                            tint = ForestGreen,
                            bg = if (isDark) StatusGoodBgDark else StatusGoodBg,
                            onClick = onReactivate,
                            enabled = !isSubmitting
                        )
                    }

                    // Action 3: Delete Account
                    if (!user.isProtected) {
                        ActionCardButton(
                            icon = Icons.Filled.Delete,
                            title = "Delete Account",
                            subtitle = "Permanently remove this user record",
                            tint = Color.Red,
                            bg = if (isDark) Color(0xFF3B1515) else Color(0xFFFFECEB),
                            onClick = onDelete,
                            enabled = !isSubmitting
                        )
                    }
                }

                if (isSubmitting) {
                    Box(
                        modifier = Modifier.fillMaxWidth(),
                        contentAlignment = Alignment.Center
                    ) {
                        CircularProgressIndicator(
                            color = ForestGreen,
                            modifier = Modifier.size(24.dp)
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ActionCardButton(
    icon: ImageVector,
    title: String,
    subtitle: String,
    tint: Color,
    bg: Color,
    onClick: () -> Unit,
    enabled: Boolean
) {
    Surface(
        onClick = onClick,
        enabled = enabled,
        shape = RoundedCornerShape(12.dp),
        color = bg,
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Box(
                modifier = Modifier
                    .size(34.dp)
                    .clip(CircleShape)
                    .background(tint.copy(alpha = 0.15f)),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = tint,
                    modifier = Modifier.size(18.dp)
                )
            }

            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    fontWeight = FontWeight.SemiBold,
                    fontSize = 14.sp,
                    color = tint
                )
                Text(
                    text = subtitle,
                    fontSize = 11.sp,
                    color = TextSecondary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            }

            Icon(
                imageVector = Icons.Filled.ChevronRight,
                contentDescription = null,
                tint = tint.copy(alpha = 0.5f),
                modifier = Modifier.size(16.dp)
            )
        }
    }
}