package com.example.smarthydro.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.RestartAlt
import androidx.compose.material.icons.filled.Save
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.SiteSelectDropdown
import com.example.smarthydro.components.ThresholdRangeRow
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.data.ThresholdCatalog
import com.example.smarthydro.data.ThresholdCheck
import com.example.smarthydro.data.ThresholdEditor
import com.example.smarthydro.data.ThresholdSensor
import com.example.smarthydro.models.StructureThresholdsDto
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.launch

// The safe range of each reading, one card per structure
@Composable
fun ThresholdsScreen(
    onNavigateBack: () -> Unit,
    modifier: Modifier = Modifier
) {
    if (!ThresholdEditor.canEdit()) {
        LaunchedEffect(Unit) { onNavigateBack() }
        return
    }

    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var selectedSiteId by remember { mutableStateOf<String?>(null) }
    val site = SiteStore.sites.find { it.id == selectedSiteId } ?: SiteStore.sites.firstOrNull()

    val structures = remember { mutableStateListOf<StructureThresholdsDto>() }
    var isLoading by remember { mutableStateOf(false) }
    var loadFailed by remember { mutableStateOf(false) }
    var isRefreshing by remember { mutableStateOf(false) }

    // Fetches every structure of the site with its ranges
    suspend fun load(siteId: String) {
        try {
            val response = ApiClient.service.getThresholdsBySite(siteId)
            val body = response.body()
            if (response.isSuccessful && body != null) {
                structures.clear()
                structures.addAll(body.filter { ThresholdCatalog.forApiType(it.structureType).isNotEmpty() })
                body.forEach { ThresholdEditor.remember(it.structureId, it.thresholds) }
                loadFailed = false
            } else {
                loadFailed = true
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            loadFailed = true
        }
    }

    LaunchedEffect(Unit) {
        SiteStore.loadFromCache()
        SiteStore.refresh()
    }

    LaunchedEffect(site?.id) {
        val id = site?.id ?: return@LaunchedEffect
        structures.clear()
        isLoading = true
        load(id)
        isLoading = false
    }

    // Pull to refresh
    fun refresh() {
        val id = site?.id ?: return
        if (isRefreshing) return
        isRefreshing = true
        scope.launch {
            try {
                load(id)
            } finally {
                isRefreshing = false
            }
        }
    }

    fun showMessage(message: String) {
        scope.launch { snackbarHostState.showSnackbar(message) }
    }

    Box(modifier = modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .background(MaterialTheme.colorScheme.background)
                .navigationBarsPadding()
                .imePadding()
        ) {
            TopHeaderBar()

            PullToRefresh(
                isRefreshing = isRefreshing,
                onRefresh = { refresh() }
            ) {
                LazyColumn(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(horizontal = 16.dp),
                    contentPadding = PaddingValues(top = 16.dp, bottom = 28.dp),
                    verticalArrangement = Arrangement.spacedBy(16.dp)
                ) {
                    // Header row with back button
                    item {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            IconButton(onClick = onNavigateBack, modifier = Modifier.size(44.dp)) {
                                Icon(
                                    imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                                    contentDescription = "Back",
                                    tint = if (isDark) TextPrimaryDark else ForestGreenDark,
                                    modifier = Modifier.size(26.dp)
                                )
                            }
                            Spacer(modifier = Modifier.width(4.dp))
                            Column {
                                Text(
                                    text = "Thresholds",
                                    modifier = Modifier.screenHeading(),
                                    fontSize = 20.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                                )
                                Text(
                                    text = "An alert is raised when a reading goes outside its range.",
                                    fontSize = 12.sp,
                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }
                        }
                    }

                    // Site picker, since each site has its own structures
                    if (SiteStore.sites.isNotEmpty()) {
                        item {
                            SiteSelectDropdown(
                                sites = SiteStore.sites,
                                selectedSiteId = site?.id,
                                onSiteSelected = { selectedSiteId = it }
                            )
                        }
                    }

                    // How the page works
                    item {
                        ThresholdNote(
                            text = when {
                                site == null -> "You don't have a site yet. Add one on the Structures screen to set its thresholds."
                                !isLoading && !loadFailed && structures.isEmpty() -> "This site has no structures yet. Add a structure to set its thresholds."
                                else -> "Each structure has its own ranges and starts on a healthy default until you change it."
                            }
                        )
                    }

                    if (loadFailed) {
                        item {
                            ThresholdBanner(text = "The saved ranges could not be loaded. Pull down to try again.")
                        }
                    }

                    if (isLoading) {
                        item {
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 40.dp),
                                contentAlignment = Alignment.Center
                            ) {
                                CircularProgressIndicator(color = ForestGreen)
                            }
                        }
                    } else {
                        items(structures, key = { it.structureId }) { structure ->
                            ThresholdStructureCard(
                                structure = structure,
                                siteName = site?.name.orEmpty(),
                                onUpdated = { updated, message ->
                                    val index = structures.indexOfFirst { it.structureId == updated.structureId }
                                    if (index >= 0) structures[index] = updated
                                    showMessage(message)
                                },
                                onMessage = { showMessage(it) }
                            )
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
}

// One structure's ranges with its own Save button
@Composable
private fun ThresholdStructureCard(
    structure: StructureThresholdsDto,
    siteName: String,
    onUpdated: (StructureThresholdsDto, String) -> Unit,
    onMessage: (String) -> Unit
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    val sensors = ThresholdCatalog.forApiType(structure.structureType)
    val saved = structure.thresholds.associateBy { it.sensorType.uppercase() }

    val minTexts = remember(structure) {
        mutableStateMapOf<String, String>().apply {
            sensors.forEach { sensor -> put(sensor.key, saved[sensor.key]?.let { ThresholdEditor.shownMin(it, sensor) }.orEmpty()) }
        }
    }
    val maxTexts = remember(structure) {
        mutableStateMapOf<String, String>().apply {
            sensors.forEach { sensor -> put(sensor.key, saved[sensor.key]?.let { ThresholdEditor.shownMax(it, sensor) }.orEmpty()) }
        }
    }

    // A reason the API refused a range
    val serverErrors = remember(structure) { mutableStateMapOf<String, String>() }
    var sectionMessage by remember(structure) { mutableStateOf<String?>(null) }
    var isSaving by remember { mutableStateOf(false) }
    var resetTarget by remember { mutableStateOf<ThresholdSensor?>(null) }
    var isResetting by remember { mutableStateOf(false) }

    val checks = sensors.associate { it.key to ThresholdEditor.check(it, minTexts[it.key].orEmpty(), maxTexts[it.key].orEmpty()) }
    val changed = sensors.filter { ThresholdEditor.isChange(checks.getValue(it.key), saved[it.key]) }
    val allValid = checks.values.none { it is ThresholdCheck.Invalid }
    val canSave = !isSaving && !isResetting && allValid && changed.isNotEmpty()

    fun save() {
        if (!canSave) return
        isSaving = true
        sectionMessage = null
        scope.launch {
            val current = structure.thresholds.toMutableList()
            var savedCount = 0
            var failed = false

            for (sensor in changed) {
                val range = checks.getValue(sensor.key) as ThresholdCheck.Valid
                val result = ThresholdEditor.save(structure.structureId, sensor, range.min, range.max)

                if (!result.ok || result.saved == null) {
                    val reason = result.message ?: "Couldn't save. Please try again."
                    serverErrors[sensor.key] = reason
                    sectionMessage = "Could not save ${sensor.label}: $reason" +
                            if (savedCount > 0) " The readings above it were saved." else ""
                    failed = true
                    break
                }

                val index = current.indexOfFirst { it.sensorType.equals(sensor.key, ignoreCase = true) }
                if (index >= 0) current[index] = result.saved else current.add(result.saved)
                savedCount++
            }

            ThresholdEditor.remember(structure.structureId, current)
            isSaving = false

            if (!failed) {
                onUpdated(structure.copy(thresholds = current), "${structure.structureName} thresholds saved.")
            }
        }
    }

    resetTarget?.let { sensor ->
        ConfirmationDialog(
            title = "Reset ${sensor.label}?",
            message = "${structure.structureName} will go back to the healthy default range for ${sensor.label}.",
            confirmButtonText = "Reset",
            dismissButtonText = "Cancel",
            icon = Icons.Filled.RestartAlt,
            isLoading = isResetting,
            onDismiss = { if (!isResetting) resetTarget = null },
            onConfirm = {
                val thresholdId = saved[sensor.key]?.id
                if (thresholdId == null) {
                    resetTarget = null
                } else {
                    isResetting = true
                    scope.launch {
                        val result = ThresholdEditor.reset(structure.structureId, thresholdId)
                        isResetting = false
                        resetTarget = null
                        if (result.ok && result.structure != null) {
                            onUpdated(result.structure, "${sensor.label} on ${structure.structureName} is back to its default range.")
                        } else {
                            onMessage(result.message ?: "Couldn't reset. Please try again.")
                        }
                    }
                }
            }
        )
    }

    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp)),
        color = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            Column {
                Text(
                    text = structure.structureName,
                    fontSize = 17.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (isDark) TextPrimaryDark else TextPrimary
                )
                Text(
                    text = ThresholdCatalog.titleFor(structure.structureType) + if (siteName.isNotBlank()) " in $siteName" else "",
                    fontSize = 12.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary
                )
            }

            sectionMessage?.let { ThresholdBanner(text = it) }

            sensors.forEachIndexed { index, sensor ->
                val savedRange = saved[sensor.key]
                val check = checks.getValue(sensor.key)

                if (index > 0) {
                    HorizontalDivider(
                        color = if (isDark) CardDividerDark else CardDivider,
                        modifier = Modifier.padding(bottom = 2.dp)
                    )
                }

                ThresholdRangeRow(
                    sensor = sensor,
                    minText = minTexts[sensor.key].orEmpty(),
                    maxText = maxTexts[sensor.key].orEmpty(),
                    onMinChange = {
                        minTexts[sensor.key] = it
                        serverErrors.remove(sensor.key)
                    },
                    onMaxChange = {
                        maxTexts[sensor.key] = it
                        serverErrors.remove(sensor.key)
                    },
                    isDefault = savedRange?.isDefault == true,
                    error = (check as? ThresholdCheck.Invalid)?.message ?: serverErrors[sensor.key],
                    enabled = !isSaving && !isResetting,
                    onReset = if (savedRange != null && !savedRange.isDefault && savedRange.id != 0) {
                        { resetTarget = sensor }
                    } else {
                        null
                    }
                )
            }

            Button(
                onClick = { save() },
                enabled = canSave,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(48.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                    contentColor = Color.White
                )
            ) {
                if (isSaving) {
                    CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp, color = Color.White)
                } else {
                    Icon(imageVector = Icons.Filled.Save, contentDescription = null, modifier = Modifier.size(18.dp))
                }
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    text = if (isSaving) "Saving…" else "Save ${possessive(structure.structureName)} Thresholds",
                    fontWeight = FontWeight.Bold
                )
            }

            if (!isSaving && changed.isEmpty() && allValid) {
                Text(
                    text = "Change a value to save.",
                    fontSize = 12.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary,
                    modifier = Modifier
                        .fillMaxWidth()
                        .wrapContentWidth(Alignment.CenterHorizontally)
                )
            }
        }
    }
}

private fun possessive(name: String): String {
    val trimmed = name.trim()
    return if (trimmed.endsWith("s", ignoreCase = true)) "$trimmed'" else "$trimmed's"
}

@Composable
private fun ThresholdNote(text: String) {
    val isDark = LocalThemeIsDark.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(if (isDark) SurfaceDarkVariant else MintBannerBg)
            .padding(horizontal = 14.dp, vertical = 10.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Icon(
            imageVector = Icons.Filled.Info,
            contentDescription = null,
            tint = ForestGreen,
            modifier = Modifier.size(18.dp)
        )
        Text(
            text = text,
            fontSize = 12.sp,
            color = if (isDark) TextSecondaryDark else TextSecondary
        )
    }
}

@Composable
private fun ThresholdBanner(text: String) {
    val isDark = LocalThemeIsDark.current
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(10.dp))
            .background(cautionText.copy(alpha = 0.10f))
            .padding(horizontal = 14.dp, vertical = 10.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(
            imageVector = Icons.Filled.ErrorOutline,
            contentDescription = null,
            tint = cautionText,
            modifier = Modifier.size(18.dp)
        )
        Text(text = text, fontSize = 13.sp, color = cautionText)
    }
}