package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.data.GalleryCapture
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.screens.authFieldColors
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.errorAnnouncement
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.switchRow
import com.example.smarthydro.ui.theme.*
import com.example.smarthydro.util.PreparedPhoto
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Locale

private const val MAX_PLANT_LENGTH = 60

// Photo details form with date taken, structure, plant and favourite.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SavePhotoSheet(
    photo: PreparedPhoto?,
    defaultStructureId: String?,
    isSaving: Boolean,
    error: String?,
    onDismiss: () -> Unit,
    onSave: (structureId: String, plant: String, takenOn: LocalDate, favourite: Boolean) -> Unit,
    existing: GalleryCapture? = null,
    onReplaceWithCamera: (() -> Unit)? = null,
    onReplaceWithUpload: (() -> Unit)? = null
) {
    val isDark = LocalThemeIsDark.current
    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText
    val isEditing = existing != null

    // Every structure, including offline ones with ids
    val structures = StructureStore.structures
    var structureId by remember { mutableStateOf(defaultStructureId) }
    var plant by remember { mutableStateOf(existing?.plant ?: "") }
    var takenOn by remember { mutableStateOf(existing?.takenAt?.toLocalDate() ?: LocalDate.now()) }
    var favourite by remember { mutableStateOf(existing?.isFavourite ?: false) }
    var showDatePicker by remember { mutableStateOf(false) }
    var structureMenuOpen by remember { mutableStateOf(false) }

    val previewBitmap = remember(photo) { photo?.preview?.asImageBitmap() }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .imePadding()
                .padding(horizontal = 20.dp)
                .padding(bottom = 28.dp)
        ) {
            Text(
                text = if (isEditing) "Edit photo" else "Save this photo",
                modifier = Modifier.screenHeading(),
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = primaryText
            )
            Text(
                text = if (isEditing) "Change the photo or update its details." else "Add a few details before it goes into the gallery.",
                fontSize = 13.sp,
                color = secondaryText
            )

            Spacer(modifier = Modifier.height(16.dp))

            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .aspectRatio(4f / 3f)
                    .clip(RoundedCornerShape(14.dp))
                    .background(ForestGreen.copy(alpha = if (isDark) 0.12f else 0.06f))
            ) {
                if (previewBitmap != null) {
                    Image(
                        bitmap = previewBitmap,
                        contentDescription = "Preview of the photo",
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize()
                    )
                } else if (existing != null) {
                    CaptureImage(capture = existing, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                }

                // Shows the image will be replaced when the changes are saved
                if (isEditing && photo != null) {
                    Text(
                        text = "New photo",
                        fontSize = 12.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color.White,
                        modifier = Modifier
                            .align(Alignment.TopStart)
                            .padding(10.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(ForestGreen)
                            .padding(horizontal = 10.dp, vertical = 4.dp)
                    )
                }
            }

            if (onReplaceWithCamera != null && onReplaceWithUpload != null) {
                Spacer(modifier = Modifier.height(10.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    ReplaceButton(text = "Take new photo", enabled = !isSaving, onClick = onReplaceWithCamera, modifier = Modifier.weight(1f))
                    ReplaceButton(text = "Upload new photo", enabled = !isSaving, onClick = onReplaceWithUpload, modifier = Modifier.weight(1f))
                }
            }

            Spacer(modifier = Modifier.height(18.dp))

            FieldLabel("Date taken")
            FieldBox(onClick = { showDatePicker = true }) {
                Text(
                    text = takenOn.format(DateTimeFormatter.ofPattern("EEE d MMMM yyyy", Locale.ENGLISH)),
                    fontSize = 15.sp,
                    color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.weight(1f)
                )
                Text(text = "Change", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = ForestGreen)
            }

            Spacer(modifier = Modifier.height(14.dp))

            FieldLabel("Structure")
            if (structures.isEmpty()) {
                Text(
                    text = "You don't have any structures yet. Add one first, then come back to save photos to it.",
                    fontSize = 13.sp,
                    color = cautionText
                )
            } else {
                val chosen = structures.find { it.id == structureId }
                Surface(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(56.dp),
                    shape = RoundedCornerShape(12.dp),
                    color = MaterialTheme.colorScheme.surface,
                    border = BorderStroke(1.dp, if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.5f))
                ) {
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .padding(horizontal = 8.dp),
                        contentAlignment = Alignment.CenterStart
                    ) {
                        StructureSelectDropdown(
                            structures = structures,
                            selectedStructureId = structureId,
                            onStructureSelected = { id -> structureId = id },
                            placeholder = "Choose a structure",
                            isHeadingStyle = false
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(14.dp))

            FieldLabel("Plant (optional)")
            OutlinedTextField(
                value = plant,
                onValueChange = { if (it.length <= MAX_PLANT_LENGTH) plant = it },
                placeholder = { Text("e.g. Strawberries") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(12.dp),
                colors = authFieldColors(isDark)
            )

            Spacer(modifier = Modifier.height(14.dp))

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(14.dp))
                    .border(1.dp, if (isDark) CardBorderDark else CardDivider, RoundedCornerShape(14.dp))
                    .switchRow(favourite) { favourite = it }
                    .padding(horizontal = 14.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = if (favourite) "★" else "☆",
                    modifier = Modifier.decorative(),
                    fontSize = 20.sp,
                    color = if (favourite) FavouriteGold else secondaryText
                )
                Spacer(modifier = Modifier.width(12.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(text = "Add to favourites", fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = primaryText)
                    Text(text = "Find it quickly with the Favourites filter", fontSize = 12.sp, color = secondaryText)
                }
                Switch(
                    checked = favourite,
                    onCheckedChange = null,
                    colors = SwitchDefaults.colors(checkedThumbColor = Color.White, checkedTrackColor = ForestGreen)
                )
            }

            error?.let { message ->
                Spacer(modifier = Modifier.height(12.dp))
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(10.dp))
                        .errorAnnouncement()
                        .background(cautionText.copy(alpha = 0.10f))
                        .padding(horizontal = 14.dp, vertical = 10.dp)
                ) {
                    Text(text = message, fontSize = 13.sp, lineHeight = 18.sp, color = cautionText)
                }
            }

            Spacer(modifier = Modifier.height(18.dp))

            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedButton(
                    onClick = onDismiss,
                    enabled = !isSaving,
                    modifier = Modifier
                        .weight(1f)
                        .height(50.dp),
                    shape = RoundedCornerShape(12.dp),
                    border = BorderStroke(1.dp, if (isDark) OutlinedButtonBorderDark else ForestGreen.copy(alpha = 0.5f)),
                    colors = ButtonDefaults.outlinedButtonColors(
                        contentColor = if (isDark) TextPrimaryDark else primaryText
                    )
                ) {
                    Text(text = if (isEditing) "Cancel" else "Discard", fontWeight = FontWeight.SemiBold)
                }
                // Stays greyed out until a structure is chosen, like the other forms
                Button(
                    onClick = { structureId?.let { onSave(it, plant, takenOn, favourite) } },
                    enabled = structureId != null && !isSaving,
                    modifier = Modifier
                        .weight(1f)
                        .height(50.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                        contentColor = Color.White,
                        disabledContainerColor = if (isDark) PrimaryButtonDisabledBgDark else ForestGreen.copy(alpha = 0.4f),
                        disabledContentColor = if (isDark) PrimaryButtonDisabledTextDark else Color.White.copy(alpha = 0.8f)
                    )
                ) {
                    if (isSaving) {
                        CircularProgressIndicator(modifier = Modifier.size(20.dp), color = Color.White, strokeWidth = 2.dp)
                    } else {
                        Text(text = if (isEditing) "Save changes" else "Save photo", fontWeight = FontWeight.Bold)
                    }
                }
            }
        }
    }

    if (showDatePicker) {
        CustomCalendarDialog(
            initialDate = takenOn,
            onDateSelected = { takenOn = it },
            onDismiss = { showDatePicker = false },
            title = "Date Taken"
        )
    }
}

@Composable
private fun ReplaceButton(text: String, enabled: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    OutlinedButton(
        onClick = onClick,
        enabled = enabled,
        modifier = modifier.height(44.dp),
        shape = RoundedCornerShape(12.dp),
        border = BorderStroke(1.dp, if (LocalThemeIsDark.current) OutlinedButtonBorderDark else ForestGreen),
        contentPadding = PaddingValues(horizontal = 8.dp)
    ) {
        Text(
            text = text,
            fontSize = 13.sp,
            fontWeight = FontWeight.SemiBold,
            color = if (LocalThemeIsDark.current) TextPrimaryDark else ForestGreenDark
        )
    }
}

@Composable
private fun FieldLabel(text: String) {
    Text(
        text = text,
        fontSize = 13.sp,
        fontWeight = FontWeight.SemiBold,
        color = if (LocalThemeIsDark.current) TextPrimaryDark else ForestGreenDark,
        modifier = Modifier.padding(bottom = 6.dp)
    )
}

// A tappable box styled like the text fields
@Composable
private fun FieldBox(onClick: () -> Unit, content: @Composable RowScope.() -> Unit) {
    val isDark = LocalThemeIsDark.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(54.dp)
            .clip(RoundedCornerShape(12.dp))
            .border(1.dp, if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.5f), RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.surface)
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
        content = content
    )
}