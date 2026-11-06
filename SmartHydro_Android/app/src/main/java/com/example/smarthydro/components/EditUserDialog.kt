package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.smarthydro.models.AdminUserDto
import com.example.smarthydro.ui.theme.*

/**
 * Custom dialog card for updating a user's details (Full Name, Farm Name, Role).
 * Matches the design aesthetics of UserActionDialog.
 */
@Composable
fun EditUserDialog(
    user: AdminUserDto,
    isDark: Boolean,
    isSubmitting: Boolean,
    onDismiss: () -> Unit,
    onSave: (fullName: String, role: String) -> Unit
) {
    var fullNameInput by remember { mutableStateOf(user.fullName ?: "") }
    var roleInput by remember { mutableStateOf(user.role) }

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
                // Header with title and close button
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp),
                        modifier = Modifier.weight(1f)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(40.dp)
                                .clip(CircleShape)
                                .background((if (isDark) ForestGreenLight else ForestGreen).copy(alpha = 0.15f)),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = Icons.Filled.Edit,
                                contentDescription = null,
                                tint = if (isDark) ForestGreenLight else ForestGreen,
                                modifier = Modifier.size(20.dp)
                            )
                        }

                        Column {
                            Text(
                                text = "Edit User",
                                fontWeight = FontWeight.Bold,
                                fontSize = 18.sp,
                                color = if (isDark) TextPrimaryDark else TextPrimary
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

                    IconButton(
                        onClick = onDismiss,
                        enabled = !isSubmitting,
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

                HorizontalDivider(
                    color = if (isDark) CardBorderDark else CardBorder,
                    thickness = 1.dp
                )

                // Input fields
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    // Full Name
                    OutlinedTextField(
                        value = fullNameInput,
                        onValueChange = { fullNameInput = it },
                        label = { Text("Full Name") },
                        placeholder = { Text("e.g. John Doe") },
                        singleLine = true,
                        leadingIcon = {
                            Icon(
                                imageVector = Icons.Filled.Person,
                                contentDescription = null,
                                tint = if (isDark) ForestGreenLight else ForestGreen,
                                modifier = Modifier.size(20.dp)
                            )
                        },
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = ForestGreen,
                            focusedLabelColor = ForestGreen,
                            cursorColor = ForestGreen
                        ),
                        shape = RoundedCornerShape(12.dp),
                        modifier = Modifier.fillMaxWidth()
                    )

                    // Role Selection
                    if (!user.isProtected) {
                        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                            Text(
                                text = "ASSIGN ROLE",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) TextSecondaryDark else TextSecondary,
                                letterSpacing = 0.5.sp
                            )

                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                listOf("Operator", "Worker", "Admin").forEach { roleOption ->
                                    val isSelected = roleInput == roleOption

                                    // Only approved accounts can be made admins
                                    val isAvailable = roleOption != "Admin" || user.role == "Admin" || user.status == "Approved"

                                    Surface(
                                        modifier = Modifier
                                            .weight(1f)
                                            .clip(RoundedCornerShape(10.dp))
                                            .border(
                                                1.dp,
                                                if (isSelected) ForestGreen else if (isDark) CardBorderDark else CardBorder,
                                                RoundedCornerShape(10.dp)
                                            )
                                            .alpha(if (isAvailable) 1f else 0.4f)
                                            .clickable(enabled = isAvailable) { roleInput = roleOption },
                                        color = if (isSelected) {
                                            (if (isDark) ForestGreenLight else ForestGreen).copy(alpha = 0.15f)
                                        } else {
                                            if (isDark) SurfaceDarkVariant else BackgroundLight
                                        }
                                    ) {
                                        Box(
                                            modifier = Modifier.padding(vertical = 10.dp),
                                            contentAlignment = Alignment.Center
                                        ) {
                                            Text(
                                                text = roleOption,
                                                fontSize = 12.sp,
                                                fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Medium,
                                                color = if (isSelected) {
                                                    if (isDark) ForestGreenLight else ForestGreen
                                                } else {
                                                    if (isDark) TextSecondaryDark else TextSecondary
                                                }
                                            )
                                        }
                                    }
                                }
                            }

                            // Explains the Admin choice such as what it gives or why it can't be picked yet
                            val roleHint = when {
                                user.role != "Admin" && user.status != "Approved" ->
                                    "Only approved accounts can be made admins."
                                roleInput == "Admin" && user.role != "Admin" ->
                                    "Admins can see and manage every account, site and structure. They'll need to sign in again."
                                else -> null
                            }

                            if (roleHint != null) {
                                Text(
                                    text = roleHint,
                                    fontSize = 11.sp,
                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }
                        }
                    } else {
                        Surface(
                            shape = RoundedCornerShape(10.dp),
                            color = (if (isDark) ForestGreenLight else ForestGreen).copy(alpha = 0.10f),
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Row(
                                modifier = Modifier.padding(10.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.Filled.Lock,
                                    contentDescription = null,
                                    tint = if (isDark) ForestGreenLight else ForestGreen,
                                    modifier = Modifier.size(16.dp)
                                )
                                Text(
                                    // Your own account and the built-in admin accounts can only have their name changed
                                    text = if (user.isCurrentUser) {
                                        "This is your account. You can change your name, but not your own role."
                                    } else {
                                        "This root account is protected. Role cannot be changed."
                                    },
                                    fontSize = 11.sp,
                                    color = if (isDark) ForestGreenLight else ForestGreen
                                )
                            }
                        }
                    }
                }

                HorizontalDivider(
                    color = if (isDark) CardBorderDark else CardBorder,
                    thickness = 1.dp
                )

                // Buttons: Cancel & Save (identical height and padding)
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedButton(
                        onClick = onDismiss,
                        enabled = !isSubmitting,
                        shape = RoundedCornerShape(12.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp),
                        border = BorderStroke(1.dp, if (isDark) CardBorderDark else CardBorder),
                        colors = ButtonDefaults.outlinedButtonColors(
                            contentColor = if (isDark) TextPrimaryDark else TextPrimary
                        ),
                        modifier = Modifier
                            .weight(1f)
                            .height(48.dp)
                    ) {
                        Text(
                            text = "Cancel",
                            fontWeight = FontWeight.SemiBold,
                            color = if (isDark) TextPrimaryDark else TextPrimary
                        )
                    }

                    Button(
                        onClick = {
                            onSave(fullNameInput, roleInput)
                        },
                        enabled = !isSubmitting,
                        shape = RoundedCornerShape(12.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                            contentColor = Color.White
                        ),
                        modifier = Modifier
                            .weight(1f)
                            .height(48.dp)
                    ) {
                        if (isSubmitting) {
                            CircularProgressIndicator(
                                color = Color.White,
                                modifier = Modifier.size(18.dp),
                                strokeWidth = 2.dp
                            )
                        } else {
                            Text(
                                text = "Save Changes",
                                fontWeight = FontWeight.Bold,
                                color = Color.White
                            )
                        }
                    }
                }
            }
        }
    }
}