package com.example.smarthydro.screens

import android.Manifest
import android.content.ActivityNotFoundException
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.AddPhotoSheet
import com.example.smarthydro.components.CaptureCard
import com.example.smarthydro.components.CaptureViewerDialog
import com.example.smarthydro.components.CustomCalendarDialog
import com.example.smarthydro.components.CustomFilterDropdown
import com.example.smarthydro.components.FilterDropdownChip
import com.example.smarthydro.components.FilterOption
import com.example.smarthydro.components.GalleryCalendar
import com.example.smarthydro.components.SavePhotoSheet
import com.example.smarthydro.components.StructureSelectDropdown
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.components.smartHydroChipColors
import com.example.smarthydro.data.GalleryCapture
import com.example.smarthydro.data.GalleryStore
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import com.example.smarthydro.util.ImageUtils
import com.example.smarthydro.util.PreparedPhoto
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

// Gallery of the photos stored for every structure the user can see
private enum class DateRange(val label: String) {
    ANY("Any date"),
    TODAY("Today"),
    YESTERDAY("Yesterday"),
    LAST_7("Last 7 days"),
    LAST_30("Last 30 days")
}

// Everything the search panel and calendar can narrow the photos down by
private data class GalleryFilters(
    val search: String = "",
    val dateRange: DateRange = DateRange.ANY,
    val day: LocalDate? = null,
    val structureId: String? = null,
    val plant: String? = null,
    val favouritesOnly: Boolean = false
) {
    val isActive: Boolean
        get() = search.isNotBlank() || dateRange != DateRange.ANY || day != null ||
                structureId != null || plant != null || favouritesOnly

    fun matches(capture: GalleryCapture, today: LocalDate): Boolean {
        val takenOn = capture.takenAt.toLocalDate()
        val dateMatches = when {
            day != null -> takenOn == day
            dateRange == DateRange.TODAY -> takenOn == today
            dateRange == DateRange.YESTERDAY -> takenOn == today.minusDays(1)
            dateRange == DateRange.LAST_7 -> !takenOn.isBefore(today.minusDays(6))
            dateRange == DateRange.LAST_30 -> !takenOn.isBefore(today.minusDays(29))
            else -> true
        }
        val query = search.trim().lowercase()
        val searchMatches = query.isEmpty() ||
                listOf(capture.plant, capture.label, capture.camera, capture.structureName)
                    .any { it.lowercase().contains(query) }

        return dateMatches && searchMatches &&
                (structureId == null || capture.structureId == structureId) &&
                (plant == null || capture.plant.equals(plant, ignoreCase = true)) &&
                (!favouritesOnly || capture.isFavourite)
    }
}

// The add chooser, the new photo form or the edit form
private enum class AddStep { NONE, CHOOSE, SAVE, EDIT }

@Composable
fun GalleryScreen(
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    val signedIn = ApiClient.isSignedIn()
    val canDelete = ApiClient.getSignedInRole() != "Worker"

    var filters by remember { mutableStateOf(GalleryFilters()) }
    var viewingId by remember { mutableStateOf<Long?>(null) }
    var editingId by remember { mutableStateOf<Long?>(null) }
    var addStep by remember { mutableStateOf(AddStep.NONE) }
    var newPhoto by remember { mutableStateOf<PreparedPhoto?>(null) }
    var isSaving by remember { mutableStateOf(false) }
    var saveError by remember { mutableStateOf<String?>(null) }

    fun showMessage(message: String) {
        scope.launch { snackbarHostState.showSnackbar(message) }
    }

    LaunchedEffect(signedIn) {
        // Structure names are needed for the filter and the save form
        if (StructureStore.structures.isEmpty()) StructureStore.refresh()
        GalleryStore.refresh()
    }

    // Pull to refresh
    var isRefreshing by remember { mutableStateOf(false) }
    fun refresh() {
        if (isRefreshing) return
        isRefreshing = true
        scope.launch {
            try {
                GalleryStore.refresh()
            } finally {
                isRefreshing = false
            }
        }
    }

    // A photo from the camera or library
    fun useChosenPhoto(photo: PreparedPhoto) {
        newPhoto = photo
        saveError = null
        if (addStep != AddStep.EDIT) addStep = AddStep.SAVE
    }

    val takePhotoLauncher = rememberLauncherForActivityResult(ActivityResultContracts.TakePicturePreview()) { bitmap ->
        if (bitmap != null) useChosenPhoto(ImageUtils.fromCamera(bitmap))
    }

    val pickPhotoLauncher = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri != null) {
            scope.launch {
                val prepared = withContext(Dispatchers.IO) { ImageUtils.fromUri(context, uri) }
                if (prepared != null) useChosenPhoto(prepared)
                else showMessage("That photo couldn't be opened. Try a different one.")
            }
        }
    }

    val cameraPermissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) takePhotoLauncher.launch(null)
        else showMessage("Camera permission is needed to take a photo.")
    }

    fun startCamera() {
        if (addStep == AddStep.CHOOSE) addStep = AddStep.NONE
        try {
            takePhotoLauncher.launch(null)
        } catch (e: SecurityException) {
            cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
        } catch (e: ActivityNotFoundException) {
            showMessage("No camera app was found on this phone.")
        }
    }

    fun startPicker() {
        if (addStep == AddStep.CHOOSE) addStep = AddStep.NONE
        pickPhotoLauncher.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
    }

    // Photo actions

    fun save(photo: PreparedPhoto, structureId: String, plant: String, takenOn: LocalDate, favourite: Boolean) {
        isSaving = true
        saveError = null
        scope.launch {
            val error = GalleryStore.upload(photo.jpeg, structureId, plant, takenOn, favourite, photo.fromCamera)
            isSaving = false
            if (error == null) {
                addStep = AddStep.NONE
                newPhoto = null
                showMessage(if (favourite) "Photo added to the gallery and your favourites." else "Photo added to the gallery.")
            } else {
                saveError = error
            }
        }
    }

    fun startEditing(capture: GalleryCapture) {
        viewingId = null
        editingId = capture.id
        newPhoto = null
        saveError = null
        addStep = AddStep.EDIT
    }

    // Goes back to the photo viewer
    fun stopEditing() {
        viewingId = editingId
        editingId = null
        newPhoto = null
        saveError = null
        addStep = AddStep.NONE
    }

    fun saveEdit(id: Long, structureId: String, plant: String, takenOn: LocalDate, favourite: Boolean) {
        val replacement = newPhoto
        isSaving = true
        saveError = null
        scope.launch {
            val error = GalleryStore.update(
                id = id,
                newJpeg = replacement?.jpeg,
                newFromCamera = replacement?.fromCamera ?: false,
                structureId = structureId,
                plant = plant,
                takenOn = takenOn,
                favourite = favourite
            )
            isSaving = false
            if (error == null) {
                stopEditing()
                showMessage("Photo updated.")
            } else {
                saveError = error
            }
        }
    }

    fun toggleFavourite(capture: GalleryCapture) {
        scope.launch {
            if (!GalleryStore.toggleFavourite(capture.id)) {
                showMessage("That photo could not be updated. It may have been deleted.")
            }
        }
    }

    fun delete(capture: GalleryCapture) {
        scope.launch {
            if (!GalleryStore.delete(capture.id)) {
                showMessage("That photo could not be deleted. It may already have been deleted or you may not be allowed to remove it.")
                return@launch
            }
            viewingId = null
            val result = snackbarHostState.showSnackbar(
                message = "Photo moved to deleted items.",
                actionLabel = "Undo",
                duration = SnackbarDuration.Long
            )
            if (result == SnackbarResult.ActionPerformed) {
                showMessage(if (GalleryStore.restore(capture.id)) "Photo restored." else "That photo could not be restored.")
            }
        }
    }

    // Layout
    val captures = GalleryStore.captures
    val today = LocalDate.now()
    val filtered = captures.filter { filters.matches(it, today) }

    Box(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            TopHeaderBar()

            PullToRefresh(
                isRefreshing = isRefreshing,
                onRefresh = { refresh() }
            ) {
                LazyColumn(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(horizontal = 16.dp),
                    contentPadding = PaddingValues(top = 16.dp, bottom = 96.dp),
                    verticalArrangement = Arrangement.spacedBy(14.dp)
                ) {
                    item { GalleryHeader(isOffline = !signedIn, onAddPhoto = { addStep = AddStep.CHOOSE }) }
                    item { SearchPanel(filters = filters, captures = captures, onChange = { filters = it }) }
                    item { BrowseByDatePanel(filters = filters, captures = captures, onChange = { filters = it }) }
                    item {
                        ResultsHeader(
                            title = if (filters.isActive) "Results" else "Recent captures",
                            count = filtered.size.takeIf { !GalleryStore.isLoading && GalleryStore.loadError == null }
                        )
                    }

                    when {
                        GalleryStore.isLoading && captures.isEmpty() -> item {
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 40.dp),
                                contentAlignment = Alignment.Center
                            ) {
                                CircularProgressIndicator(color = ForestGreen)
                            }
                        }

                        GalleryStore.loadError != null -> item {
                            GalleryMessage(
                                title = "Photos didn't load",
                                text = GalleryStore.loadError ?: "",
                                actionLabel = "Try again",
                                onAction = { scope.launch { GalleryStore.refresh() } }
                            )
                        }

                        captures.isEmpty() -> item {
                            GalleryMessage(
                                title = "No photos yet",
                                text = "Use Add photo to take one with your camera or upload one from your phone.",
                                actionLabel = "Add photo",
                                onAction = { addStep = AddStep.CHOOSE }
                            )
                        }

                        filtered.isEmpty() -> item {
                            GalleryMessage(
                                title = "No matches",
                                text = "No captures match your search. Try another word or clear the filters.",
                                actionLabel = "Clear filters",
                                onAction = { filters = GalleryFilters() }
                            )
                        }

                        // Two photos per row
                        else -> items(filtered.chunked(2), key = { row -> row.first().id }) { row ->
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(12.dp)
                            ) {
                                row.forEach { capture ->
                                    CaptureCard(
                                        capture = capture,
                                        onClick = { viewingId = capture.id },
                                        modifier = Modifier.weight(1f)
                                    )
                                }
                                if (row.size == 1) Spacer(modifier = Modifier.weight(1f))
                            }
                        }
                    }
                }
            }
        }

        SnackbarHost(
            hostState = snackbarHostState,
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(16.dp)
        )
    }

    viewingId?.let { GalleryStore.find(it) }?.let { capture ->
        CaptureViewerDialog(
            capture = capture,
            canDelete = canDelete,
            onDismiss = { viewingId = null },
            onToggleFavourite = { toggleFavourite(capture) },
            onEdit = { startEditing(capture) },
            onDelete = { delete(capture) }
        )
    }

    if (addStep == AddStep.CHOOSE) {
        AddPhotoSheet(
            onDismiss = { addStep = AddStep.NONE },
            onTakePhoto = { startCamera() },
            onChoosePhoto = { startPicker() }
        )
    }

    val photo = newPhoto
    if (addStep == AddStep.SAVE && photo != null) {
        SavePhotoSheet(
            photo = photo,
            // Picks the structure being filtered to
            defaultStructureId = filters.structureId
                ?: StructureStore.structures.singleOrNull()?.id,
            isSaving = isSaving,
            error = saveError,
            onDismiss = {
                if (!isSaving) {
                    addStep = AddStep.NONE
                    newPhoto = null
                    saveError = null
                }
            },
            onSave = { structureId, plant, takenOn, favourite -> save(photo, structureId, plant, takenOn, favourite) }
        )
    }

    val editing = editingId?.let { GalleryStore.find(it) }
    if (addStep == AddStep.EDIT && editing != null) {
        SavePhotoSheet(
            photo = newPhoto,
            existing = editing,
            defaultStructureId = editing.structureId,
            isSaving = isSaving,
            error = saveError,
            onDismiss = { if (!isSaving) stopEditing() },
            onSave = { structureId, plant, takenOn, favourite -> saveEdit(editing.id, structureId, plant, takenOn, favourite) },
            onReplaceWithCamera = { startCamera() },
            onReplaceWithUpload = { startPicker() }
        )
    }
}

// Sections of the page
@Composable
private fun GalleryHeader(isOffline: Boolean, onAddPhoto: () -> Unit) {
    val isDark = LocalThemeIsDark.current
    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = "Gallery",
                modifier = Modifier.screenHeading(),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )
            Text(
                text = if (isOffline) "Saved on this phone" else "Photos from your structures",
                fontSize = 13.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary
            )
        }
        Button(
            onClick = onAddPhoto,
            shape = RoundedCornerShape(12.dp),
            contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp),
            colors = ButtonDefaults.buttonColors(
                containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                contentColor = Color.White
            )
        ) {
            Icon(
                painter = painterResource(id = R.drawable.ic_camera),
                contentDescription = null,
                tint = Color.White,
                modifier = Modifier.size(16.dp)
            )
            Spacer(modifier = Modifier.width(8.dp))
            Text(text = "Add photo", fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
        }
    }
}

@Composable
private fun SearchPanel(
    filters: GalleryFilters,
    captures: List<GalleryCapture>,
    onChange: (GalleryFilters) -> Unit
) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val structures = StructureStore.structures
    val plants = captures.map { it.plant }.filter { it.isNotBlank() }.distinct().sortedBy { it.lowercase() }

    GalleryCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            PanelTitle(
                title = "Search captures",
                subtitle = "Find photos by date, plant, structure or keyword.",
                modifier = Modifier.weight(1f)
            )
            if (filters.isActive) {
                TextButton(onClick = { onChange(GalleryFilters()) }) {
                    Text(text = "Clear", color = if (isDark) AccentGreenDark else ForestGreen, fontWeight = FontWeight.SemiBold)
                }
            }
        }

        Spacer(modifier = Modifier.height(12.dp))

        OutlinedTextField(
            value = filters.search,
            onValueChange = { onChange(filters.copy(search = it)) },
            placeholder = { Text("Search photos…") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            trailingIcon = {
                if (filters.search.isNotEmpty()) {
                    TextButton(onClick = { onChange(filters.copy(search = "")) }) {
                        Text(text = "✕", modifier = Modifier.semantics { contentDescription = "Clear search" }, color = secondaryText)
                    }
                }
            },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(12.dp),
            colors = authFieldColors(isDark)
        )

        Spacer(modifier = Modifier.height(12.dp))

        var showCustomCalendar by remember { mutableStateOf(false) }

        Row(
            modifier = Modifier.horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            // Date filter dropdown
            val dateLabel = filters.day?.format(DateTimeFormatter.ofPattern("d MMM", Locale.ENGLISH))
                ?: if (filters.dateRange == DateRange.ANY) "Date" else filters.dateRange.label
            val dateOptions = DateRange.values().map { range ->
                FilterOption(
                    value = range.name,
                    label = range.label
                )
            } + listOf(
                FilterOption(
                    value = "CUSTOM_CALENDAR",
                    label = "Custom Date…",
                    subtitle = "Pick from calendar"
                )
            )

            CustomFilterDropdown(
                label = dateLabel,
                selectedOption = if (filters.day != null) "CUSTOM_CALENDAR" else if (filters.dateRange != DateRange.ANY) filters.dateRange.name else null,
                options = dateOptions,
                onOptionSelected = { selected ->
                    if (selected == "CUSTOM_CALENDAR") {
                        showCustomCalendar = true
                    } else if (selected != null) {
                        val range = DateRange.valueOf(selected)
                        onChange(filters.copy(dateRange = range, day = null))
                    } else {
                        onChange(filters.copy(dateRange = DateRange.ANY, day = null))
                    }
                }
            )

            // Structure filter dropdown
            val structureOptions = listOf(
                FilterOption<String?>(
                    value = null,
                    label = "All structures",
                    subtitle = "${structures.size} total structures",
                    iconType = "sensors",
                    iconColor = ForestGreen
                )
            ) + structures.map { structure ->
                FilterOption<String?>(
                    value = structure.id,
                    label = structure.title,
                    subtitle = if (ApiClient.isSignedIn()) com.example.smarthydro.data.SiteStore.nameFor(structure.siteId) else null,
                    iconType = structure.iconType,
                    iconColor = structure.themeColor
                )
            }

            CustomFilterDropdown(
                label = structures.find { it.id == filters.structureId }?.title ?: "Structure",
                selectedOption = filters.structureId,
                options = structureOptions,
                onOptionSelected = { selectedId ->
                    onChange(filters.copy(structureId = selectedId))
                }
            )

            // Plant filter dropdown
            val plantOptions = listOf(
                FilterOption<String?>(
                    value = null,
                    label = "All plants",
                    subtitle = "${plants.size} plant types",
                    iconType = "carrot",
                    iconColor = ForestGreen
                )
            ) + plants.map { plantName ->
                val iconType = when {
                    plantName.contains("strawberr", ignoreCase = true) -> "strawberry"
                    plantName.contains("carrot", ignoreCase = true) || plantName.contains("fodder", ignoreCase = true) -> "carrot"
                    plantName.contains("flower", ignoreCase = true) -> "flower"
                    else -> "carrot"
                }
                FilterOption<String?>(
                    value = plantName,
                    label = plantName,
                    iconType = iconType,
                    iconColor = ForestGreen
                )
            }

            CustomFilterDropdown(
                label = filters.plant ?: "Plant type",
                selectedOption = filters.plant,
                options = plantOptions,
                onOptionSelected = { selectedPlant ->
                    onChange(filters.copy(plant = selectedPlant))
                }
            )

            // Favourites filter chip
            FilterChip(
                selected = filters.favouritesOnly,
                onClick = { onChange(filters.copy(favouritesOnly = !filters.favouritesOnly)) },
                label = { Text(if (filters.favouritesOnly) "★ Favourites" else "☆ Favourites") },
                shape = RoundedCornerShape(20.dp),
                colors = smartHydroChipColors(isDark)
            )
        }

        if (showCustomCalendar) {
            CustomCalendarDialog(
                initialDate = filters.day ?: LocalDate.now(),
                onDateSelected = { pickedDate ->
                    onChange(filters.copy(day = pickedDate, dateRange = DateRange.ANY))
                },
                onDismiss = { showCustomCalendar = false },
                title = "Select Filter Date"
            )
        }
    }
}

// Collapsible so it doesn't push the photos down
@Composable
private fun BrowseByDatePanel(
    filters: GalleryFilters,
    captures: List<GalleryCapture>,
    onChange: (GalleryFilters) -> Unit
) {
    var open by remember { mutableStateOf(false) }
    var month by remember { mutableStateOf(YearMonth.now()) }
    val daysWithPhotos = captures.groupingBy { it.takenAt.toLocalDate() }.eachCount()

    GalleryCard {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clickable { open = !open },
            verticalAlignment = Alignment.CenterVertically
        ) {
            PanelTitle(
                title = "Browse by date",
                subtitle = "Highlighted dates show available captures.",
                modifier = Modifier.weight(1f)
            )
            Text(
                text = if (open) "▴" else "▾",
                fontSize = 18.sp,
                color = if (LocalThemeIsDark.current) TextSecondaryDark else TextSecondary
            )
        }

        if (open) {
            Spacer(modifier = Modifier.height(12.dp))
            GalleryCalendar(
                month = month,
                daysWithPhotos = daysWithPhotos,
                selectedDay = filters.day,
                onSelectDay = { day ->
                    // Tapping the selected day again clears it
                    onChange(filters.copy(day = if (filters.day == day) null else day, dateRange = DateRange.ANY))
                },
                onMonthChange = { month = it }
            )
        }
    }
}

// Count is null while loading or after an error
@Composable
private fun ResultsHeader(title: String, count: Int?) {
    val isDark = LocalThemeIsDark.current
    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(
            text = title,
            fontSize = 16.sp,
            fontWeight = FontWeight.Bold,
            color = if (isDark) TextPrimaryDark else ForestGreenDark,
            modifier = Modifier.weight(1f)
        )
        if (count != null) {
            Text(
                text = "$count capture${if (count == 1) "" else "s"}",
                fontSize = 13.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary
            )
        }
    }
}

// Small building blocks for this page

@Composable
private fun GalleryCard(content: @Composable ColumnScope.() -> Unit) {
    val isDark = LocalThemeIsDark.current
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, if (isDark) CardBorderDark else CardDivider),
        shadowElevation = if (isDark) 0.dp else 1.dp
    ) {
        Column(modifier = Modifier.padding(16.dp), content = content)
    }
}

@Composable
private fun PanelTitle(title: String, subtitle: String, modifier: Modifier = Modifier) {
    val isDark = LocalThemeIsDark.current
    Column(modifier = modifier) {
        Text(
            text = title,
            fontSize = 16.sp,
            fontWeight = FontWeight.Bold,
            color = if (isDark) TextPrimaryDark else ForestGreenDark
        )
        Text(
            text = subtitle,
            fontSize = 12.sp,
            color = if (isDark) TextSecondaryDark else TextSecondary
        )
    }
}

// Loading error, empty gallery and no-matches states
@Composable
private fun GalleryMessage(title: String, text: String, actionLabel: String, onAction: () -> Unit) {
    val isDark = LocalThemeIsDark.current
    GalleryCard {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 12.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                text = title,
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = text,
                fontSize = 13.sp,
                lineHeight = 18.sp,
                textAlign = TextAlign.Center,
                color = if (isDark) TextSecondaryDark else TextSecondary
            )
            Spacer(modifier = Modifier.height(10.dp))
            TextButton(onClick = onAction) {
                Text(text = actionLabel, color = ForestGreen, fontWeight = FontWeight.SemiBold)
            }
        }
    }
}