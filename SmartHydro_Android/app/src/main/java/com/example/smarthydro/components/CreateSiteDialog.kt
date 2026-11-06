package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Place
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.smarthydro.ui.theme.*

/**
 * Clean modal dialog for creating or editing a cultivation site.
 */
@Composable
fun CreateSiteDialog(
    isDark: Boolean,
    isLoading: Boolean,
    initialSiteName: String = "",
    isEdit: Boolean = false,
    serverErrorMessage: String? = null,
    onDismiss: () -> Unit,
    onCreateSite: (name: String) -> Unit = {},
    onSaveSite: (name: String) -> Unit = onCreateSite
) {
    var siteName by remember(initialSiteName) { mutableStateOf(initialSiteName) }
    var localError by remember { mutableStateOf<String?>(null) }
    val displayError = localError ?: serverErrorMessage

    Dialog(onDismissRequest = { if (!isLoading) onDismiss() }) {
        Card(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 16.dp),
            shape = RoundedCornerShape(24.dp),
            colors = CardDefaults.cardColors(
                containerColor = if (isDark) SurfaceDark else SurfaceWhite
            ),
            border = BorderStroke(
                1.dp,
                if (isDark) CardBorderDark else CardBorder
            ),
            elevation = CardDefaults.cardElevation(defaultElevation = 8.dp)
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(22.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Header
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
                                imageVector = Icons.Filled.Place,
                                contentDescription = null,
                                tint = if (isDark) StatusGoodTextDark else ForestGreen,
                                modifier = Modifier.size(22.dp)
                            )
                        }

                        Column {
                            Text(
                                text = if (isEdit) "Edit Site" else "Create New Site",
                                fontWeight = FontWeight.Bold,
                                fontSize = 18.sp,
                                color = if (isDark) TextPrimaryDark else ForestGreenDark
                            )
                            Text(
                                text = if (isEdit) "Update this site's name" else "Add a new site to your organisation",
                                fontSize = 12.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }
                    }

                    IconButton(
                        onClick = onDismiss,
                        enabled = !isLoading,
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

                // Site Name Input
                OutlinedTextField(
                    value = siteName,
                    onValueChange = {
                        siteName = it
                        if (localError != null) localError = null
                    },
                    label = { Text("Site Name *") },
                    placeholder = { Text("e.g. GreenHouse Alpha, North Tunnel") },
                    singleLine = true,
                    enabled = !isLoading,
                    isError = displayError != null,
                    shape = RoundedCornerShape(12.dp),
                    modifier = Modifier.fillMaxWidth(),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = ForestGreen,
                        unfocusedBorderColor = if (isDark) CardBorderDark else CardBorder,
                        errorBorderColor = if (isDark) StatusCautionTextDark else StatusCautionText,
                        focusedLabelColor = ForestGreen,
                        cursorColor = ForestGreen,
                        focusedTextColor = if (isDark) TextPrimaryDark else TextPrimary,
                        unfocusedTextColor = if (isDark) TextPrimaryDark else TextPrimary
                    )
                )

                if (displayError != null) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(10.dp))
                            .background(if (isDark) StatusCautionBgDark else StatusCautionBg)
                            .padding(horizontal = 12.dp, vertical = 9.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Filled.ErrorOutline,
                            contentDescription = null,
                            tint = if (isDark) StatusCautionTextDark else StatusCautionText,
                            modifier = Modifier.size(16.dp)
                        )
                        Text(
                            text = displayError,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Medium,
                            color = if (isDark) StatusCautionTextDark else StatusCautionText
                        )
                    }
                }

                // Action Buttons
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedButton(
                        onClick = onDismiss,
                        enabled = !isLoading,
                        shape = RoundedCornerShape(12.dp),
                        contentPadding = PaddingValues(horizontal = 14.dp),
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
                            val trimmed = siteName.trim()
                            if (trimmed.isBlank()) {
                                localError = "Site name is required."
                            } else if (com.example.smarthydro.data.SiteStore.sites.any {
                                    it.name.trim().equals(trimmed, ignoreCase = true) && (!isEdit || !it.name.trim().equals(initialSiteName.trim(), ignoreCase = true))
                                }) {
                                localError = "A site named \"$trimmed\" already exists."
                            } else {
                                localError = null
                                onSaveSite(trimmed)
                            }
                        },
                        enabled = !isLoading,
                        shape = RoundedCornerShape(12.dp),
                        contentPadding = PaddingValues(horizontal = 14.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                            contentColor = Color.White
                        ),
                        modifier = Modifier
                            .weight(1.3f)
                            .height(48.dp)
                    ) {
                        if (isLoading) {
                            CircularProgressIndicator(
                                strokeWidth = 2.dp,
                                color = Color.White,
                                modifier = Modifier.size(20.dp)
                            )
                        } else {
                            Text(
                                text = if (isEdit) "Save Changes" else "Create Site",
                                fontWeight = FontWeight.Bold
                            )
                        }
                    }
                }
            }
        }
    }
}
