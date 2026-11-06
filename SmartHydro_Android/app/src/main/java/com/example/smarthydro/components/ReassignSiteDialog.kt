package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.smarthydro.models.AdminSiteDto
import com.example.smarthydro.models.AdminUserDto
import com.example.smarthydro.ui.theme.*

/**
 * Modern custom dialog card for reassigning site ownership to an operator.
 * Features:
 * - Shows current site and owner details
 * - Search bar with instant filtering by name or email
 * - Displays first 5 operators initially or all matching operators if searching
 * - Clean selection indicator and role tag
 * - Matched 48dp symmetrical action buttons
 */
@Composable
fun ReassignSiteDialog(
    site: AdminSiteDto,
    operators: List<AdminUserDto>,
    isDark: Boolean,
    isAssigning: Boolean,
    onDismiss: () -> Unit,
    onConfirm: (targetEmail: String) -> Unit
) {
    var searchQuery by remember { mutableStateOf("") }
    var selectedEmail by remember { mutableStateOf(site.ownerEmail ?: "") }

    // Filter operators matching search query
    val filteredOperators = remember(searchQuery, operators) {
        if (searchQuery.isBlank()) {
            operators.take(5)
        } else {
            operators.filter {
                (it.fullName ?: "").contains(searchQuery, ignoreCase = true) ||
                        it.email.contains(searchQuery, ignoreCase = true)
            }
        }
    }

    Dialog(onDismissRequest = { if (!isAssigning) onDismiss() }) {
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
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                // Header Row
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
                                imageVector = Icons.Filled.SwapHoriz,
                                contentDescription = null,
                                tint = if (isDark) ForestGreenLight else ForestGreen,
                                modifier = Modifier.size(22.dp)
                            )
                        }

                        Column {
                            Text(
                                text = "Reassign Site Owner",
                                fontWeight = FontWeight.Bold,
                                fontSize = 17.sp,
                                color = if (isDark) TextPrimaryDark else TextPrimary
                            )
                            Text(
                                text = site.name,
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Medium,
                                color = if (isDark) ForestGreenLight else ForestGreen,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                    }

                    IconButton(
                        onClick = onDismiss,
                        enabled = !isAssigning,
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

                // Current Owner Info Banner
                Surface(
                    shape = RoundedCornerShape(10.dp),
                    color = if (isDark) SurfaceDarkVariant else BackgroundLight,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Row(
                        modifier = Modifier.padding(10.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Filled.Person,
                            contentDescription = null,
                            tint = if (isDark) TextSecondaryDark else TextSecondary,
                            modifier = Modifier.size(16.dp)
                        )
                        Text(
                            text = "Current Owner: ${site.ownerEmail ?: "Unassigned"}",
                            fontSize = 12.sp,
                            color = if (isDark) TextSecondaryDark else TextSecondary
                        )
                    }
                }

                // Search Input
                OutlinedTextField(
                    value = searchQuery,
                    onValueChange = { searchQuery = it },
                    placeholder = { Text("Search operator by name or email...", fontSize = 13.sp) },
                    singleLine = true,
                    leadingIcon = {
                        Icon(
                            imageVector = Icons.Filled.Search,
                            contentDescription = "Search",
                            tint = if (isDark) ForestGreenLight else ForestGreen,
                            modifier = Modifier.size(18.dp)
                        )
                    },
                    trailingIcon = {
                        if (searchQuery.isNotEmpty()) {
                            IconButton(onClick = { searchQuery = "" }) {
                                Icon(
                                    imageVector = Icons.Filled.Clear,
                                    contentDescription = "Clear",
                                    tint = TextSecondary,
                                    modifier = Modifier.size(16.dp)
                                )
                            }
                        }
                    },
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = ForestGreen,
                        focusedLabelColor = ForestGreen,
                        cursorColor = ForestGreen
                    ),
                    shape = RoundedCornerShape(12.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(50.dp)
                )

                // Subtitle: Showing count
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = if (searchQuery.isBlank()) "SUGGESTED OPERATORS" else "SEARCH RESULTS",
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        letterSpacing = 0.5.sp
                    )
                    if (searchQuery.isBlank() && operators.size > 5) {
                        Text(
                            text = "Showing 5 of ${operators.size}",
                            fontSize = 10.sp,
                            color = if (isDark) TextSecondaryDark else TextSecondary
                        )
                    }
                }

                // Operators List (max height 220dp to maintain dialog ergonomics)
                LazyColumn(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 220.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    if (filteredOperators.isEmpty()) {
                        item {
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 24.dp),
                                contentAlignment = Alignment.Center
                            ) {
                                Text(
                                    text = "No operators found matching \"$searchQuery\"",
                                    fontSize = 12.sp,
                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }
                        }
                    } else {
                        items(filteredOperators, key = { it.id }) { op ->
                            val isSelected = selectedEmail.equals(op.email, ignoreCase = true)

                            Surface(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clip(RoundedCornerShape(10.dp))
                                    .border(
                                        1.dp,
                                        if (isSelected) ForestGreen else if (isDark) CardBorderDark else CardBorder,
                                        RoundedCornerShape(10.dp)
                                    )
                                    .selectable(selected = isSelected, role = Role.RadioButton, onClick = { selectedEmail = op.email }),
                                color = if (isSelected) {
                                    (if (isDark) ForestGreenLight else ForestGreen).copy(alpha = 0.12f)
                                } else {
                                    if (isDark) SurfaceDarkVariant else BackgroundLight
                                }
                            ) {
                                Row(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(horizontal = 12.dp, vertical = 8.dp),
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                                ) {
                                    // Custom Radio Indicator
                                    RadioButton(
                                        selected = isSelected,
                                        onClick = null,
                                        colors = RadioButtonDefaults.colors(
                                            selectedColor = ForestGreen
                                        ),
                                        modifier = Modifier.size(20.dp)
                                    )

                                    Column(modifier = Modifier.weight(1f)) {
                                        Text(
                                            text = op.fullName ?: op.email,
                                            fontSize = 13.sp,
                                            fontWeight = if (isSelected) FontWeight.Bold else FontWeight.SemiBold,
                                            color = if (isSelected && !isDark) ForestGreenDark else if (isDark) TextPrimaryDark else TextPrimary,
                                            maxLines = 1,
                                            overflow = TextOverflow.Ellipsis
                                        )
                                        Text(
                                            text = op.email,
                                            fontSize = 11.sp,
                                            color = if (isDark) TextSecondaryDark else TextSecondary,
                                            maxLines = 1,
                                            overflow = TextOverflow.Ellipsis
                                        )
                                    }

                                    // Role Tag
                                    Surface(
                                        shape = RoundedCornerShape(6.dp),
                                        color = (if (isDark) TextSecondaryDark else ForestGreen).copy(alpha = 0.10f)
                                    ) {
                                        Text(
                                            text = op.role,
                                            fontSize = 10.sp,
                                            fontWeight = FontWeight.Medium,
                                            color = if (isDark) ForestGreenLight else ForestGreen,
                                            modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp)
                                        )
                                    }
                                }
                            }
                        }
                    }
                }

                HorizontalDivider(
                    color = if (isDark) CardBorderDark else CardBorder,
                    thickness = 1.dp
                )

                // Buttons: Cancel & Reassign
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedButton(
                        onClick = onDismiss,
                        enabled = !isAssigning,
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
                            if (selectedEmail.isNotBlank()) {
                                onConfirm(selectedEmail)
                            }
                        },
                        enabled = !isAssigning && selectedEmail.isNotBlank(),
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
                        if (isAssigning) {
                            CircularProgressIndicator(
                                color = Color.White,
                                modifier = Modifier.size(18.dp),
                                strokeWidth = 2.dp
                            )
                        } else {
                            Text(
                                text = "Confirm",
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
