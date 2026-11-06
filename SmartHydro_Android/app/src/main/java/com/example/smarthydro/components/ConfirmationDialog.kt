package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.smarthydro.ui.theme.*

/**
 * Reusable modal confirmation dialog with sleek SmartHydro card styling,
 * icon badge, optional close button, customizable confirm/dismiss buttons,
 * and support for destructive actions (e.g., delete, remove, clear).
 */
@Composable
fun ConfirmationDialog(
    title: String,
    message: String? = null,
    annotatedMessage: AnnotatedString? = null,
    confirmButtonText: String = "Confirm",
    dismissButtonText: String = "Cancel",
    isDestructive: Boolean = false,
    isLoading: Boolean = false,
    icon: ImageVector = if (isDestructive) Icons.Filled.DeleteOutline else Icons.Filled.Close,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current

    val iconBgColor = if (isDestructive) {
        if (isDark) StatusCautionBgDark else StatusCautionBg
    } else {
        if (isDark) StatusGoodBgDark else MintBannerBg
    }

    val iconTintColor = if (isDestructive) {
        if (isDark) StatusCautionTextDark else StatusCautionText
    } else {
        if (isDark) StatusGoodTextDark else ForestGreen
    }

    val confirmContainerColor = if (isDestructive) {
        if (isDark) StatusCautionBgDark else StatusCautionBg
    } else {
        ForestGreen
    }

    val confirmTextColor = if (isDestructive) {
        if (isDark) StatusCautionTextDark else StatusCautionText
    } else {
        Color.White
    }

    Dialog(onDismissRequest = { if (!isLoading) onDismiss() }) {
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
                                .background(iconBgColor),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = icon,
                                contentDescription = null,
                                tint = iconTintColor,
                                modifier = Modifier.size(22.dp)
                            )
                        }

                        Text(
                            text = title,
                            fontWeight = FontWeight.Bold,
                            fontSize = 18.sp,
                            color = if (isDark) TextPrimaryDark else TextPrimary
                        )
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

                // Message body
                if (annotatedMessage != null) {
                    Text(
                        text = annotatedMessage,
                        fontSize = 13.sp,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        lineHeight = 18.sp
                    )
                } else if (message != null) {
                    Text(
                        text = message,
                        fontSize = 13.sp,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        lineHeight = 18.sp
                    )
                }

                HorizontalDivider(
                    color = if (isDark) CardBorderDark else CardBorder,
                    thickness = 1.dp
                )

                // Action buttons: Cancel and Confirm
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedButton(
                        onClick = onDismiss,
                        enabled = !isLoading,
                        shape = RoundedCornerShape(12.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp),
                        modifier = Modifier
                            .weight(1f)
                            .height(48.dp)
                    ) {
                        Text(
                            text = dismissButtonText,
                            fontWeight = FontWeight.SemiBold,
                            color = if (isDark) TextPrimaryDark else TextPrimary
                        )
                    }

                    Button(
                        onClick = onConfirm,
                        enabled = !isLoading,
                        shape = RoundedCornerShape(12.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = confirmContainerColor,
                            contentColor = confirmTextColor
                        ),
                        modifier = Modifier
                            .weight(1f)
                            .height(48.dp)
                    ) {
                        if (isLoading) {
                            CircularProgressIndicator(
                                modifier = Modifier.size(20.dp),
                                color = confirmTextColor,
                                strokeWidth = 2.dp
                            )
                        } else {
                            Text(
                                text = confirmButtonText,
                                fontWeight = FontWeight.Bold,
                                color = confirmTextColor
                            )
                        }
                    }
                }
            }
        }
    }
}
