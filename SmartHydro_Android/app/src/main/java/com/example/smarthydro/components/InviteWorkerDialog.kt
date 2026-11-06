package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.ArrowDropUp
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.PersonAdd
import androidx.compose.material.icons.filled.Place
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.ui.theme.*

/**
 * Custom styled dialog for inviting a worker to a site.
 */
@Composable
fun InviteWorkerDialog(
    onDismiss: () -> Unit,
    onInvite: (siteId: String, email: String) -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    var email by remember { mutableStateOf("") }
    var selectedSiteId by remember { mutableStateOf(SiteStore.sites.firstOrNull()?.id) }
    var isSiteDropdownExpanded by remember { mutableStateOf(false) }

    Dialog(onDismissRequest = onDismiss) {
        Card(
            modifier = modifier
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
                // Header with icon badge, title and close button
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
                        Box(
                            modifier = Modifier
                                .size(42.dp)
                                .clip(CircleShape)
                                .background(if (isDark) StatusGoodBgDark else MintBannerBg),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = Icons.Filled.PersonAdd,
                                contentDescription = null,
                                tint = if (isDark) StatusGoodTextDark else ForestGreen,
                                modifier = Modifier.size(22.dp)
                            )
                        }

                        Column {
                            Text(
                                text = "Invite a Worker",
                                fontWeight = FontWeight.Bold,
                                fontSize = 18.sp,
                                color = if (isDark) TextPrimaryDark else TextPrimary
                            )
                            Text(
                                text = "Add team member to site",
                                fontSize = 12.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }
                    }

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

                Text(
                    text = "They'll receive an email with a sign-up link and an access code for the Android app.",
                    fontSize = 13.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary,
                    lineHeight = 18.sp
                )

                // Worker email input
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text(
                        text = "WORKER EMAIL",
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        color = if (isDark) TextMutedDark else TextMuted,
                        letterSpacing = 1.sp
                    )

                    OutlinedTextField(
                        value = email,
                        onValueChange = { email = it },
                        placeholder = { Text("worker@example.com") },
                        singleLine = true,
                        shape = RoundedCornerShape(12.dp),
                        modifier = Modifier.fillMaxWidth(),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = ForestGreen,
                            unfocusedBorderColor = if (isDark) CardBorderDark else CardBorder
                        )
                    )
                }

                // Site selection (if multiple sites)
                if (SiteStore.sites.size > 1) {
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(
                            text = "ASSIGN TO SITE",
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (isDark) TextMutedDark else TextMuted,
                            letterSpacing = 1.sp
                        )

                        var triggerWidthDp by remember { mutableStateOf(0.dp) }
                        val density = LocalDensity.current

                        Box(modifier = Modifier.fillMaxWidth()) {
                            Surface(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(52.dp)
                                    .onGloballyPositioned { coordinates ->
                                        triggerWidthDp = with(density) { coordinates.size.width.toDp() }
                                    },
                                shape = RoundedCornerShape(14.dp),
                                color = if (isDark) SurfaceDarkVariant else BackgroundLight,
                                border = androidx.compose.foundation.BorderStroke(
                                    width = 1.dp,
                                    color = if (isSiteDropdownExpanded) ForestGreen
                                    else if (isDark) CardBorderDark
                                    else CardBorder
                                ),
                                onClick = { isSiteDropdownExpanded = !isSiteDropdownExpanded }
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
                                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                                    ) {
                                        Icon(
                                            imageVector = Icons.Filled.Place,
                                            contentDescription = null,
                                            tint = ForestGreen,
                                            modifier = Modifier.size(18.dp)
                                        )
                                        Text(
                                            text = SiteStore.nameFor(selectedSiteId),
                                            fontSize = 14.sp,
                                            fontWeight = FontWeight.Medium,
                                            color = if (isDark) TextPrimaryDark else TextPrimary
                                        )
                                    }

                                    Icon(
                                        imageVector = if (isSiteDropdownExpanded) Icons.Filled.ArrowDropUp else Icons.Filled.ArrowDropDown,
                                        contentDescription = "Toggle site dropdown",
                                        tint = if (isDark) TextSecondaryDark else TextSecondary,
                                        modifier = Modifier.size(24.dp)
                                    )
                                }
                            }

                            MaterialTheme(
                                shapes = MaterialTheme.shapes.copy(
                                    extraSmall = RoundedCornerShape(14.dp)
                                )
                            ) {
                                DropdownMenu(
                                    expanded = isSiteDropdownExpanded,
                                    onDismissRequest = { isSiteDropdownExpanded = false },
                                    modifier = (if (triggerWidthDp > 0.dp) Modifier.width(triggerWidthDp) else Modifier.fillMaxWidth())
                                        .clip(RoundedCornerShape(14.dp))
                                        .background(if (isDark) SurfaceDark else SurfaceWhite)
                                        .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
                                ) {
                                    SiteStore.sites.forEach { site ->
                                        val isSelected = site.id == selectedSiteId
                                        DropdownMenuItem(
                                            text = {
                                                Row(
                                                    modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
                                                    verticalAlignment = Alignment.CenterVertically,
                                                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                                                ) {
                                                    Icon(
                                                        imageVector = Icons.Filled.Place,
                                                        contentDescription = null,
                                                        tint = if (isSelected) ForestGreen else (if (isDark) TextSecondaryDark else TextSecondary),
                                                        modifier = Modifier.size(18.dp)
                                                    )
                                                    Text(
                                                        text = site.name,
                                                        fontSize = 14.sp,
                                                        fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                                        color = if (isSelected) ForestGreen else if (isDark) TextPrimaryDark else TextPrimary
                                                    )
                                                }
                                            },
                                            onClick = {
                                                selectedSiteId = site.id
                                                isSiteDropdownExpanded = false
                                            }
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

                // Action buttons
                val isValid = email.trim().contains("@") && email.trim().contains(".")
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedButton(
                        onClick = onDismiss,
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
                            val siteId = selectedSiteId
                            val trimmedEmail = email.trim()
                            if (siteId != null && trimmedEmail.isNotEmpty()) {
                                onInvite(siteId, trimmedEmail)
                            }
                        },
                        enabled = isValid,
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
                        Text(
                            text = "Send Invite",
                            fontWeight = FontWeight.Bold,
                            color = Color.White
                        )
                    }
                }
            }
        }
    }
}
