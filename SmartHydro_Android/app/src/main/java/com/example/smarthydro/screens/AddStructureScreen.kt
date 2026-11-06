package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.OffsetMapping
import androidx.compose.ui.text.input.TransformedText
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.SiteSelectDropdown
import com.example.smarthydro.components.StructureTypeDropdown
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.Site
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.data.StructureLabelColours
import com.example.smarthydro.data.StructureSaveOutcome
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.models.StructureType
import com.example.smarthydro.ui.errorAnnouncement
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import com.example.smarthydro.util.MacAddressFormat
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

@Composable
fun AddStructureScreen(
    onNavigateBack: () -> Unit,
    onStructureAdded: () -> Unit,
    structureId: String? = null,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    // The structure being edited, or null when this screen is adding a new one
    val existing = remember(structureId) { structureId?.let { StructureStore.find(it) } }
    val isEdit = existing != null

    // Only asked when there's a choice to make; a single site is picked automatically
    var selectedSiteId by remember { mutableStateOf(existing?.siteId ?: SiteStore.sites.firstOrNull()?.id) }
    var isSaving by remember { mutableStateOf(false) }

    val colorOptions = listOf(
        LabelGreen,
        LabelRed,
        LabelAmber,
        LabelBlue,
        LabelBrown
    )

    val structureTypes = listOf(
        "Hydroponic Structure",
        "Fodder Structure"
    )

    var structureName by remember { mutableStateOf(existing?.title ?: "") }
    var selectedType by remember {
        mutableStateOf(if (existing?.structureType == StructureType.FODDER) "Fodder Structure" else "Hydroponic Structure")
    }
    var selectedColorIndex by remember { mutableStateOf(existing?.let { colorOptions.indexOf(it.themeColor).coerceAtLeast(0) } ?: 0) }
    var macAddress by remember {
        mutableStateOf(existing?.macAddress?.filter { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' } ?: "")
    }
    var macError by remember { mutableStateOf<String?>(null) }

    // The camera that photographs this structure
    var cameraMac by remember {
        mutableStateOf(existing?.cameraMac?.filter { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' } ?: "")
    }
    var cameraError by remember { mutableStateOf<String?>(null) }
    var nameError by remember { mutableStateOf<String?>(null) }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        TopHeaderBar()

        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 16.dp),
            contentPadding = PaddingValues(
                top = 12.dp,
                bottom = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding() + 32.dp
            ),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Header Row: Centered Title with Back button on the left
            item {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 4.dp)
                ) {
                    IconButton(
                        onClick = onNavigateBack,
                        modifier = Modifier
                            .align(Alignment.CenterStart)
                            .size(40.dp)
                    ) {
                        Icon(
                            painter = painterResource(id = R.drawable.ic_chevron_left),
                            contentDescription = "Back",
                            tint = if (isDark) TextPrimaryDark else ForestGreenDark,
                            modifier = Modifier.size(24.dp)
                        )
                    }

                    Text(
                        text = if (isEdit) "Edit Structure" else "Add a Structure",
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold,
                        color = if (isDark) TextPrimaryDark else ForestGreenDark,
                        modifier = Modifier.screenHeading().align(Alignment.Center)
                    )
                }
            }

            // MAC Address Input
            item {
                Column(modifier = Modifier.fillMaxWidth()) {
                    FieldLabel(text = "Controller MAC Address")

                    OutlinedTextField(
                        value = macAddress,
                        onValueChange = { input ->
                            // Clean input: keep only hex digits up to 12 chars
                            val hex = input.uppercase().filter { it in '0'..'9' || it in 'A'..'F' }.take(12)
                            macAddress = hex
                            macError = null
                        },
                        visualTransformation = remember { MacVisualTransformation() },
                        placeholder = {
                            Text(text = "A4:CF:12:9B:3E:01", color = if (isDark) TextMutedDark else TextMuted, fontSize = 15.sp)
                        },
                        modifier = Modifier.fillMaxWidth().height(60.dp),
                        shape = RoundedCornerShape(12.dp),
                        isError = macError != null,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = ForestGreen,
                            unfocusedBorderColor = if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.7f),
                            errorBorderColor = if (isDark) StatusCautionTextDark else StatusCautionText,
                            focusedContainerColor = MaterialTheme.colorScheme.surface,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surface,
                            errorContainerColor = MaterialTheme.colorScheme.surface,
                            focusedTextColor = MaterialTheme.colorScheme.onSurface,
                            unfocusedTextColor = MaterialTheme.colorScheme.onSurface,
                            errorTextColor = MaterialTheme.colorScheme.onSurface
                        ),
                        keyboardOptions = KeyboardOptions(
                            capitalization = KeyboardCapitalization.Characters,
                            keyboardType = KeyboardType.Ascii, imeAction = ImeAction.Next),
                        singleLine = true
                    )

                    Text(
                        text = macError ?: "Enter standard MAC with colons or 12 hex digits (e.g. A4:CF:12:9B:3E:01).",
                        fontSize = 13.sp,
                        color = when {
                            macError == null -> if (isDark) TextSecondaryDark else TextSecondary
                            isDark -> StatusCautionTextDark
                            else -> StatusCautionText
                        },
                        modifier = Modifier.padding(top = 6.dp)
                    )
                }
            }

            // Camera MAC Address Input
            if (ApiClient.isSignedIn()) {
                item {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        FieldLabel(text = "Camera MAC Address (optional)")

                        OutlinedTextField(
                            value = cameraMac,
                            onValueChange = { input ->
                                val hex = input.uppercase().filter { it in '0'..'9' || it in 'A'..'F' }.take(12)
                                cameraMac = hex
                                cameraError = null
                            },
                            visualTransformation = remember { MacVisualTransformation() },
                            placeholder = {
                                Text(text = "A4:CF:12:9B:3E:02", color = if (isDark) TextMutedDark else TextMuted, fontSize = 15.sp)
                            },
                            trailingIcon = if (cameraMac.isNotEmpty()) {
                                {
                                    IconButton(onClick = {
                                        cameraMac = ""
                                        cameraError = null
                                    }) {
                                        Icon(
                                            imageVector = Icons.Filled.Close,
                                            contentDescription = "Clear camera MAC address",
                                            tint = if (isDark) TextSecondaryDark else TextSecondary
                                        )
                                    }
                                }
                            } else {
                                null
                            },
                            modifier = Modifier.fillMaxWidth().height(60.dp),
                            shape = RoundedCornerShape(12.dp),
                            isError = cameraError != null,
                            colors = OutlinedTextFieldDefaults.colors(
                                focusedBorderColor = ForestGreen,
                                unfocusedBorderColor = if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.7f),
                                errorBorderColor = if (isDark) StatusCautionTextDark else StatusCautionText,
                                focusedContainerColor = MaterialTheme.colorScheme.surface,
                                unfocusedContainerColor = MaterialTheme.colorScheme.surface,
                                errorContainerColor = MaterialTheme.colorScheme.surface,
                                focusedTextColor = MaterialTheme.colorScheme.onSurface,
                                unfocusedTextColor = MaterialTheme.colorScheme.onSurface,
                                errorTextColor = MaterialTheme.colorScheme.onSurface
                            ),
                            keyboardOptions = KeyboardOptions(
                                capitalization = KeyboardCapitalization.Characters,
                                keyboardType = KeyboardType.Ascii, imeAction = ImeAction.Next),
                            singleLine = true
                        )

                        Text(
                            text = cameraError ?: "The camera that takes photos of this structure. Leave empty if it has no camera.",
                            fontSize = 13.sp,
                            color = when {
                                cameraError == null -> if (isDark) TextSecondaryDark else TextSecondary
                                isDark -> StatusCautionTextDark
                                else -> StatusCautionText
                            },
                            modifier = Modifier.padding(top = 6.dp)
                        )
                    }
                }
            }

            // Structure Name Input
            item {
                Column(modifier = Modifier.fillMaxWidth()) {
                    FieldLabel(text = "Structure Name")

                    OutlinedTextField(
                        value = structureName,
                        onValueChange = {
                            structureName = it.take(60)
                            nameError = null
                        },
                        placeholder = {
                            Text(text = "EMERIS NMB Fodder Structure",color = if (isDark) TextMutedDark else TextMuted, fontSize = 16.sp)
                        },
                        modifier = Modifier.fillMaxWidth().height(60.dp),
                        shape = RoundedCornerShape(12.dp),
                        isError = nameError != null,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = ForestGreen,
                            unfocusedBorderColor = if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.7f),
                            errorBorderColor = if (isDark) StatusCautionTextDark else StatusCautionText,
                            focusedContainerColor = MaterialTheme.colorScheme.surface,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surface,
                            errorContainerColor = MaterialTheme.colorScheme.surface,
                            focusedTextColor = MaterialTheme.colorScheme.onSurface,
                            unfocusedTextColor = MaterialTheme.colorScheme.onSurface,
                            errorTextColor = MaterialTheme.colorScheme.onSurface
                        ),
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done)
                    )

                    nameError?.let { message ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(top = 8.dp)
                                .errorAnnouncement()
                                .clip(RoundedCornerShape(8.dp))
                                .background(if (isDark) StatusCautionBgDark else StatusCautionBg)
                                .padding(horizontal = 10.dp, vertical = 7.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(6.dp)
                        ) {
                            Icon(
                                imageVector = Icons.Filled.ErrorOutline,
                                contentDescription = null,
                                tint = if (isDark) StatusCautionTextDark else StatusCautionText,
                                modifier = Modifier.size(16.dp)
                            )
                            Text(
                                text = message,
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Medium,
                                color = if (isDark) StatusCautionTextDark else StatusCautionText
                            )
                        }
                    }
                }
            }

            // Custom Structure Type Selector
            item {
                Column(modifier = Modifier.fillMaxWidth()) {
                    FieldLabel(text = "Structure Type")
                    StructureTypeDropdown(
                        selectedType = selectedType,
                        options = structureTypes,
                        onTypeSelected = { selectedType = it }
                    )
                }
            }

            // Site Dropdown: custom styled dropdown displaying location icon, site name, and structure count
            if (ApiClient.isSignedIn() && SiteStore.sites.isNotEmpty()) {
                item {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        FieldLabel(text = "Site")

                        SiteSelectDropdown(
                            sites = SiteStore.sites,
                            selectedSiteId = selectedSiteId,
                            onSiteSelected = { selectedSiteId = it }
                        )
                    }
                }
            }

            // Date Card: today's date when adding, the creation date when editing (hidden if it has none)
            if (!isEdit || existing?.createdOn != null) {
                item {
                    val today = remember { LocalDate.now().format(DateTimeFormatter.ofPattern("d MMMM yyyy", Locale.ENGLISH)) }

                    Column(modifier = Modifier.fillMaxWidth()) {
                        FieldLabel(text = "Date")

                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(58.dp)
                                .clip(RoundedCornerShape(12.dp))
                                .border(1.5.dp, if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.7f), RoundedCornerShape(12.dp))
                                .background(MaterialTheme.colorScheme.surface)
                                .padding(horizontal = 16.dp),
                            contentAlignment = Alignment.CenterStart
                        ) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    text = if (isEdit) "Created on" else "Set automatically",
                                    fontSize = 16.sp,
                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                )
                                Text(
                                    text = existing?.createdOn ?: today,
                                    fontSize = 16.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                            }
                        }
                    }
                }
            }

            // Label Color Section (5 Swatches)
            item {
                Column(modifier = Modifier.fillMaxWidth()) {
                    Text(
                        text = "Label Colour",
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface,
                        modifier = Modifier.padding(bottom = 12.dp)
                    )

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(16.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        colorOptions.forEachIndexed { index, color ->
                            val isSelected = selectedColorIndex == index
                            val iconRes = StructureLabelColours.drawableResFor(color)

                            Box(
                                modifier = Modifier
                                    .size(48.dp)
                                    .clip(CircleShape)
                                    .background(color)
                                    .then(
                                        if (isSelected) {
                                            Modifier.border(2.5.dp, if (isDark) Color.White else ForestGreenDark, CircleShape)
                                        } else {
                                            Modifier.border(1.dp, Color.White.copy(alpha = 0.25f), CircleShape)
                                        }
                                    )
                                    .selectable(selected = isSelected, role = Role.RadioButton, onClick = { selectedColorIndex = index })
                                    .semantics { contentDescription = StructureLabelColours.nameFor(color) },
                                contentAlignment = Alignment.Center
                            ) {
                                Icon(
                                    painter = painterResource(id = iconRes),
                                    contentDescription = null,
                                    tint = Color.White,
                                    modifier = Modifier.size(24.dp)
                                )

                                if (isSelected) {
                                    Box(
                                        modifier = Modifier
                                            .align(Alignment.BottomEnd)
                                            .size(16.dp)
                                            .clip(CircleShape)
                                            .background(if (isDark) Color.White else ForestGreenDark),
                                        contentAlignment = Alignment.Center
                                    ) {
                                        Icon(
                                            painter = painterResource(id = R.drawable.ic_check),
                                            contentDescription = "Selected",
                                            tint = if (isDark) ForestGreenDark else Color.White,
                                            modifier = Modifier.size(11.dp)
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Add a Structure Action Button
            item {
                Spacer(modifier = Modifier.height(10.dp))
                Button(
                    onClick = {
                        val mac = MacAddressFormat.normalise(macAddress)
                        macError = when {
                            macAddress.isBlank() -> "Please enter the MAC address of the structure's controller."
                            mac == null -> "Enter a valid MAC address, e.g. A4:CF:12:9B:3E:01."
                            StructureStore.macExists(mac, existing?.id) -> "This MAC address is already linked to another structure."
                            else -> null
                        }
                        // The camera is optional
                        val camera = if (cameraMac.isBlank()) null else MacAddressFormat.normalise(cameraMac)
                        cameraError = when {
                            cameraMac.isBlank() -> null
                            camera == null -> "Enter a valid camera MAC address, e.g. A4:CF:12:9B:3E:02."
                            camera == mac -> "The camera and the controller can't have the same MAC address."
                            StructureStore.macExists(camera, existing?.id) -> "This camera is already linked to another structure."
                            else -> null
                        }
                        val trimmedName = structureName.trim()
                        nameError = when {
                            trimmedName.isEmpty() -> "Please enter a name for the structure."
                            trimmedName.length < 2 -> "The name must be between 2 and 60 characters."
                            StructureStore.nameExists(trimmedName, existing?.id) -> "A structure with this name already exists."
                            else -> null
                        }

                        // Show every error at once, and only save when there are none
                        if (macError == null && nameError == null && cameraError == null && mac != null && !isSaving) {
                            val structureType = if (selectedType == "Fodder Structure") StructureType.FODDER else StructureType.HYDROPONIC
                            isSaving = true
                            scope.launch {
                                val result = if (existing != null) {
                                    StructureStore.update(existing.id, trimmedName, mac, structureType, colorOptions[selectedColorIndex], selectedSiteId, camera)
                                } else {
                                    StructureStore.add(trimmedName, mac, structureType, colorOptions[selectedColorIndex], selectedSiteId, camera)
                                }
                                isSaving = false

                                if (result.outcome == StructureSaveOutcome.SAVED) {
                                    val message = if (isEdit) "\"$trimmedName\" was updated." else "\"$trimmedName\" was added to your site."
                                    Toast.makeText(context, message, Toast.LENGTH_SHORT).show()
                                    onStructureAdded()
                                } else if (result.outcome == StructureSaveOutcome.CONFLICT) {
                                    // The API names the field that clashed, so the error shows under the right box
                                    val field = result.field.orEmpty()
                                    when {
                                        field.contains("camera", ignoreCase = true) -> cameraError = result.message
                                        field.contains("name", ignoreCase = true) -> nameError = result.message
                                        else -> macError = result.message
                                    }
                                } else {
                                    Toast.makeText(context, result.message ?: "Something went wrong. Please try again.", Toast.LENGTH_SHORT).show()
                                }
                            }
                        }
                    },
                    modifier = Modifier.fillMaxWidth().height(54.dp),
                    shape = RoundedCornerShape(12.dp),
                    enabled = !isSaving,
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                        contentColor = Color.White
                    )
                ) {
                    Text(
                        text = when {
                            isSaving -> "Saving…"
                            isEdit -> "Save Changes"
                            else -> "Add a Structure"
                        },
                        fontSize = 18.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            }
        }
    }

}

// Bold label shown above a form field
@Composable
private fun FieldLabel(text: String) {
    Text(
        text = text,
        fontSize = 16.sp,
        fontWeight = FontWeight.Bold,
        color = MaterialTheme.colorScheme.onSurface,
        modifier = Modifier.padding(bottom = 8.dp)
    )
}

/**
 * Transforms raw hex digits (e.g. "A4CF12") to visually formatted MAC address ("A4:CF:12")
 * with accurate bidirectional cursor navigation and no keystroke latency.
 */
private class MacVisualTransformation : VisualTransformation {
    override fun filter(text: AnnotatedString): TransformedText {
        val raw = text.text
        val out = buildString {
            for (i in raw.indices) {
                append(raw[i])
                if (i % 2 == 1 && i < 11 && i < raw.length - 1) {
                    append(':')
                }
            }
        }

        val offsetMapping = object : OffsetMapping {
            override fun originalToTransformed(offset: Int): Int {
                if (offset <= 0) return 0
                val colonsBefore = (offset - 1) / 2
                return (offset + colonsBefore).coerceAtMost(out.length)
            }

            override fun transformedToOriginal(offset: Int): Int {
                if (offset <= 0) return 0
                val colonsBefore = offset / 3
                return (offset - colonsBefore).coerceAtMost(raw.length)
            }
        }

        return TransformedText(AnnotatedString(out), offsetMapping)
    }
}