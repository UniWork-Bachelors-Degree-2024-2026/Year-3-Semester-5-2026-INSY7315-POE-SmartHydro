package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.example.smarthydro.data.GalleryCapture
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.theme.*
import java.time.format.DateTimeFormatter
import java.util.Locale

// A photo opened from the gallery with the full image, its details, favourite, edit and delete
@Composable
fun CaptureViewerDialog(
    capture: GalleryCapture,
    canDelete: Boolean,
    onDismiss: () -> Unit,
    onToggleFavourite: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit
) {
    val isDark = LocalThemeIsDark.current
    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText

    var confirmDelete by remember { mutableStateOf(false) }

    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false)
    ) {
        Surface(
            modifier = Modifier
                .fillMaxWidth(0.94f)
                .padding(vertical = 24.dp),
            shape = RoundedCornerShape(20.dp),
            color = MaterialTheme.colorScheme.surface
        ) {
            Column(modifier = Modifier.verticalScroll(rememberScrollState())) {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .aspectRatio(4f / 3f)
                        .background(Color.Black)
                ) {
                    CaptureImage(
                        capture = capture,
                        contentScale = ContentScale.Fit,
                        modifier = Modifier.fillMaxSize()
                    )

                    Box(
                        modifier = Modifier
                            .align(Alignment.TopEnd)
                            .padding(10.dp)
                            .size(48.dp)
                            .clip(CircleShape)
                            .background(Color.Black.copy(alpha = 0.55f))
                            .clickable(onClick = onDismiss)
                            .semantics { contentDescription = "Close" },
                        contentAlignment = Alignment.Center
                    ) {
                        Text(text = "✕", modifier = Modifier.decorative(), color = Color.White, fontSize = 15.sp)
                    }
                }

                Column(modifier = Modifier.padding(20.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = capture.title,
                                fontSize = 18.sp,
                                fontWeight = FontWeight.Bold,
                                color = primaryText
                            )
                            Text(
                                text = capture.takenAt.format(
                                    DateTimeFormatter.ofPattern("EEEE d MMMM yyyy, HH:mm", Locale.ENGLISH)
                                ),
                                fontSize = 12.sp,
                                color = secondaryText
                            )
                        }

                        val starColour = if (capture.isFavourite) FavouriteGold else ForestGreen
                        OutlinedButton(
                            onClick = onToggleFavourite,
                            shape = RoundedCornerShape(20.dp),
                            border = BorderStroke(1.dp, starColour),
                            contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp)
                        ) {
                            Text(
                                text = if (capture.isFavourite) "★ Favourite" else "☆ Favourite",
                                fontSize = 13.sp,
                                color = starColour
                            )
                        }
                    }

                    Spacer(modifier = Modifier.height(16.dp))

                    DetailRow(label = "Plant", value = capture.plant.ifBlank { "Not set" })
                    DetailRow(label = "Structure", value = capture.structureName)
                    DetailRow(label = "Camera", value = capture.camera.ifBlank { "Unknown" })

                    Spacer(modifier = Modifier.height(16.dp))

                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        if (canDelete) {
                            OutlinedButton(
                                onClick = { confirmDelete = true },
                                modifier = Modifier
                                    .weight(1f)
                                    .height(46.dp),
                                shape = RoundedCornerShape(12.dp),
                                border = BorderStroke(1.dp, cautionText)
                            ) {
                                Text(text = "Delete photo", color = cautionText, fontWeight = FontWeight.SemiBold)
                            }
                        }
                        // Closing is done with the x on the photo
                        Button(
                            onClick = onEdit,
                            modifier = Modifier
                                .weight(1f)
                                .height(46.dp),
                            shape = RoundedCornerShape(12.dp),
                            colors = ButtonDefaults.buttonColors(
                                containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                                contentColor = Color.White
                            )
                        ) {
                            Text(text = "Edit photo", fontWeight = FontWeight.SemiBold)
                        }
                    }
                }
            }
        }
    }

    // Same wording as MVC
    if (confirmDelete) {
        ConfirmationDialog(
            title = "Delete this photo?",
            annotatedMessage = buildAnnotatedString {
                withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(capture.title) }
                append(" will be moved to deleted items. You can undo this straight after.")
            },
            confirmButtonText = "Yes, delete",
            isDestructive = true,
            onConfirm = {
                confirmDelete = false
                onDelete()
            },
            onDismiss = { confirmDelete = false }
        )
    }
}

@Composable
private fun DetailRow(label: String, value: String) {
    val isDark = LocalThemeIsDark.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = label,
            fontSize = 13.sp,
            color = if (isDark) TextSecondaryDark else TextSecondary,
            modifier = Modifier.width(96.dp)
        )
        Text(
            text = value,
            fontSize = 14.sp,
            fontWeight = FontWeight.Medium,
            color = MaterialTheme.colorScheme.onSurface
        )
    }
    HorizontalDivider(color = if (isDark) CardDividerDark else CardDivider)
}