package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.api.BoxApiClient
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.LatestPhotoSheet
import com.example.smarthydro.components.ModeControlSection
import com.example.smarthydro.components.NoReadingCard
import com.example.smarthydro.components.NoReadingsHeader
import com.example.smarthydro.components.SensorCard
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.StructureSelectDropdown
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.BoxControl
import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.data.BoxTelemetryMapper
import com.example.smarthydro.data.SensorDetailMapper
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.data.StructureDataCache
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.data.TelemetryMapper
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.models.StructureDashboardSummaryDto
import com.example.smarthydro.models.ThresholdResponseDto
import com.example.smarthydro.models.BadgeType
import com.example.smarthydro.models.StructureType
import com.example.smarthydro.transport.BoxLink
import com.example.smarthydro.transport.DirectBox
import com.example.smarthydro.transport.BoxLinkSelector
import com.example.smarthydro.transport.BoxMode
import com.example.smarthydro.transport.BoxModeInfo
import com.example.smarthydro.transport.KeepBoxConnected
import com.example.smarthydro.transport.ModeControl
import com.example.smarthydro.transport.rememberBoxSession
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.theme.*
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@Composable
fun HomeScreen(
    activeStructureId: String,
    onNavigateToSensorDetail: (structureId: String, sensorId: String) -> Unit,
    onSwitchStructure: (String) -> Unit = {},
    onPairWithBox: (String) -> Unit = {},
    onOpenGallery: () -> Unit = {},
    onNavigateBack: (() -> Unit)? = null,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    // Falls back to whatever structure is available, since this screen only opens once one
    // has been chosen; if the list is still empty (loading, or nothing left), there's nothing
    // to show yet, so the caller is asked to leave this screen.
    val activeStructure = StructureStore.structures.find { it.id == activeStructureId } ?: StructureStore.structures.firstOrNull()
    if (activeStructure == null) {
        LaunchedEffect(Unit) { onNavigateBack?.invoke() }
        return
    }

    val deviceMac = activeStructure.macAddress
    // A real, signed-in hydroponic structure gets live sensor cards; a fodder structure or a
    // farmer's locally paired structure keeps the mock list, since fodder telemetry has no
    // field yet that stands in for tray moisture, and a farmer's structure has no cloud id.
    val structureId = activeStructure.id.toIntOrNull()
    val isRealHydroponicStructure = activeStructure.structureType == StructureType.HYDROPONIC &&
            ApiClient.isSignedIn() && structureId != null

    var dashboardSummary by remember(activeStructure.id) { mutableStateOf<StructureDashboardSummaryDto?>(null) }
    var thresholds by remember(activeStructure.id) { mutableStateOf<Map<String, ThresholdResponseDto>>(emptyMap()) }
    var thresholdsLoaded by remember(activeStructure.id) { mutableStateOf(false) }

    // True once the first fetch has finished, successfully or not, so a sensor is only called
    // "no readings" after we have actually asked, and not while the page is still loading
    var dashboardLoaded by remember(activeStructure.id) { mutableStateOf(false) }

    // Fetches the latest readings for a real structure and keeps them for next time
    suspend fun loadDashboard() {
        if (!isRealHydroponicStructure || structureId == null) return
        try {
            val response = ApiClient.service.getStructureDashboardSummary(structureId)
            if (response.isSuccessful) {
                response.body()?.let {
                    dashboardSummary = it
                    StructureDataCache.putSummary(structureId, it)
                }
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // Leaves the cards on their "--" placeholders rather than guessing a reading
        }
    }

    LaunchedEffect(activeStructure.id) {
        // What was fetched a moment ago shows straight away while the fresh answer is on its way
        val cached = if (isRealHydroponicStructure && structureId != null) StructureDataCache.summary(structureId) else null
        dashboardSummary = cached
        dashboardLoaded = cached != null

        loadDashboard()
        dashboardLoaded = true
    }

    // Fetches the structure's optimal ranges from the API and keeps them for next time
    suspend fun loadThresholds() {
        if (!isRealHydroponicStructure || structureId == null) return
        try {
            val response = ApiClient.service.getThresholdsByStructure(structureId)
            if (response.isSuccessful) {
                val byType = response.body()?.thresholds?.associateBy { it.sensorType.uppercase() }.orEmpty()
                thresholds = byType
                StructureDataCache.putThresholds(structureId, byType)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // Cards still show without an optimal-range badge
        }
        thresholdsLoaded = true
    }

    LaunchedEffect(activeStructure.id) {
        if (!isRealHydroponicStructure || structureId == null) return@LaunchedEffect

        val cached = StructureDataCache.thresholds(structureId)
        if (cached != null) {
            thresholds = cached
            thresholdsLoaded = true
            return@LaunchedEffect
        }

        loadThresholds()
    }

    // A farmer with a paired box reads it directly, even
    // though they are not signed in to the cloud at all
    val isFarmerBoxCandidate = !ApiClient.isSignedIn() && !activeStructure.macAddress.isNullOrBlank()

    // A signed-in person who has paired the box over Bluetooth can also reach it directly, which keeps working
    // and is quicker when the cloud is slow
    val signedIn = ApiClient.isSignedIn()
    val hasBoxMac = !activeStructure.macAddress.isNullOrBlank()
    val hasSavedBluetooth = !activeStructure.boxBluetoothAddress.isNullOrBlank()
    val boxSessionWanted = DirectBox.sessionWanted(signedIn, hasBoxMac, hasSavedBluetooth)
    var boxTelemetry by remember(activeStructure.id) { mutableStateOf<BoxTelemetryDto?>(null) }

    // True once the box has answered or been given up on, so cards are not judged before that
    var boxChecked by remember(activeStructure.id) { mutableStateOf(false) }

    // Goes up when a switch change failed, so the switch snaps back to what is really on
    var toggleResetKey by remember(activeStructure.id) { mutableStateOf(0) }

    // Goes up on each pull to refresh
    var boxRefreshKey by remember(activeStructure.id) { mutableStateOf(0) }

    // The way the box is being reached right now, or null when it can't be
    var boxLink by remember(activeStructure.id) { mutableStateOf<BoxLink?>(null) }

    // What the box says about its connectivity mode, the mode the person just asked for and the one waiting to be confirmed
    var boxModeInfo by remember(activeStructure.id) { mutableStateOf<BoxModeInfo?>(null) }
    var requestedMode by remember(activeStructure.id) { mutableStateOf<BoxMode?>(null) }
    var pendingModeConfirm by remember(activeStructure.id) { mutableStateOf<BoxMode?>(null) }

    // A box paired over Bluetooth is connected again while this screen is open, after a restart or a drop
    KeepBoxConnected(
        structureId = activeStructure.id,
        savedAddress = activeStructure.boxBluetoothAddress,
        enabled = boxSessionWanted
    )

    // Reads the box once a minute while this screen is open and keeps each reading on the phone
    val boxSession = rememberBoxSession(
        key = activeStructure.id,
        refreshKey = boxRefreshKey,
        boxIpAddress = activeStructure.boxIpAddress,
        deviceMac = activeStructure.macAddress.orEmpty(),
        enabled = boxSessionWanted
    )

    // boxTelemetry already starts empty for each structure
    LaunchedEffect(boxSession) {
        if (boxSession == null) return@LaunchedEffect

        // One state carries the link, the reading and whether the box has been checked, so they change together
        boxSession.state.collect {
            boxTelemetry = it.reading
            boxLink = it.link
            boxModeInfo = it.modeInfo
            if (it.checked) boxChecked = true
        }
    }

    // Whether the readings and switches below come from the box itself rather than the cloud
    val useBox = isFarmerBoxCandidate || (boxSessionWanted && DirectBox.inUse(signedIn, boxLink, boxTelemetry != null))

    // The buttons say the box is switching only until it gets there, or for a while, and then show what it really does
    LaunchedEffect(boxModeInfo, requestedMode) {
        if (requestedMode != null && boxModeInfo?.mode == requestedMode) requestedMode = null
    }
    LaunchedEffect(requestedMode) {
        if (requestedMode != null) {
            delay(ModeControl.SWITCHING_SHOWN_MS)
            requestedMode = null
        }
    }

    // Pull to refresh
    var isRefreshing by remember { mutableStateOf(false) }
    fun refresh() {
        if (isRefreshing) return
        isRefreshing = true
        scope.launch {
            try {
                if (isRealHydroponicStructure) {
                    loadDashboard()
                    loadThresholds()
                }
                if (boxSessionWanted) {
                    boxRefreshKey++
                    delay(600)
                }
            } finally {
                isRefreshing = false
            }
        }
    }

    val sensors = when {
        useBox -> BoxTelemetryMapper.sensorNodes(boxTelemetry)
        isRealHydroponicStructure -> TelemetryMapper.hydroponicSensorNodes(
            telemetry = dashboardSummary?.latestTelemetry,
            actuators = dashboardSummary?.actuatorState,
            thresholds = thresholds,
            thresholdsLoaded = thresholdsLoaded
        )
        // Everything else is built from what the box said, or as "no reading" when it has said nothing
        else -> BoxTelemetryMapper.sensorNodes(boxTelemetry)
    }

    // Sends a single relay on/off command to the structure's controller board, then updates
    // the actuator state from the response so the switch reflects what the device actually
    // accepted rather than what was tapped
    fun sendActuatorToggle(sensorId: String, turnOn: Boolean) {
        val mac = dashboardSummary?.actuatorState?.deviceMac ?: deviceMac
        val command = mac?.let { TelemetryMapper.continuousCommandFor(sensorId, it, turnOn) } ?: return

        scope.launch {
            try {
                val response = ApiClient.service.sendContinuousCommand(command)
                if (response.isSuccessful) {
                    dashboardSummary = dashboardSummary?.copy(actuatorState = response.body())
                    structureId?.let { id -> dashboardSummary?.let { StructureDataCache.putSummary(id, it) } }
                } else {
                    toggleResetKey++
                    Toast.makeText(context, "Couldn't change that. Please try again.", Toast.LENGTH_SHORT).show()
                }
            } catch (e: Exception) {
                toggleResetKey++
                Toast.makeText(context, "Couldn't reach the server. Check your connection.", Toast.LENGTH_SHORT).show()
            }
        }
    }

    // Switches one relay directly on the box, through whichever link reaches it. When the box cannot be
    // reached the person is told so plainly and the switch goes back to where it was
    fun sendBoxToggle(sensorId: String, turnOn: Boolean) {
        val actuator = BoxControl.actuatorFor(sensorId) ?: return
        val transport = boxSession?.transport?.value

        // No reading has come back from the box, or no link reaches it, so there is nobody to send this to
        if (boxTelemetry == null || transport == null) {
            Toast.makeText(context, if (boxChecked) BoxControl.OFFLINE_MESSAGE else BoxControl.SEARCHING_MESSAGE, Toast.LENGTH_SHORT).show()
            toggleResetKey++
            return
        }

        scope.launch {
            when (val result = transport.setRelay(actuator, turnOn)) {
                is BoxControlResult.Done -> {
                    // What the box says is on now, or what was asked for when its link does not say, until the next reading
                    boxTelemetry = boxTelemetry?.copy(
                        relays = result.relays ?: BoxControl.withRelay(boxTelemetry?.relays, actuator, turnOn)
                    )
                }
                BoxControlResult.Offline -> {
                    Toast.makeText(context, BoxControl.OFFLINE_MESSAGE, Toast.LENGTH_SHORT).show()
                    toggleResetKey++
                }
                is BoxControlResult.Refused -> {
                    Toast.makeText(context, result.message, Toast.LENGTH_LONG).show()
                    toggleResetKey++
                }
            }
        }
    }

    // Asks the box to change its connectivity mode, over Bluetooth. The box answers that it took the command, and what
    // it is really using shows in its next readings, so the buttons say it is switching until they agree
    fun sendMode(mode: BoxMode) {
        val transport = boxSession?.transport?.value
        if (transport == null) {
            Toast.makeText(context, BoxControl.OFFLINE_MESSAGE, Toast.LENGTH_SHORT).show()
            return
        }

        scope.launch {
            when (val result = transport.setMode(mode)) {
                is BoxControlResult.Done -> requestedMode = mode
                BoxControlResult.Offline -> Toast.makeText(context, BoxControl.OFFLINE_MESSAGE, Toast.LENGTH_SHORT).show()
                is BoxControlResult.Refused -> Toast.makeText(context, result.message, Toast.LENGTH_LONG).show()
            }
        }
    }

    // A mode that changes how the box reaches the internet is asked about first
    fun chooseMode(mode: BoxMode) {
        if (ModeControl.confirmation(mode) != null) pendingModeConfirm = mode else sendMode(mode)
    }

    pendingModeConfirm?.let { mode ->
        ModeControl.confirmation(mode)?.let { ask ->
            ConfirmationDialog(
                title = ask.title,
                message = ask.message,
                confirmButtonText = ask.confirmLabel,
                icon = Icons.Filled.Check,
                onConfirm = {
                    pendingModeConfirm = null
                    sendMode(mode)
                },
                onDismiss = { pendingModeConfirm = null }
            )
        }
    }

    // While the readings are still on their way, every card stays in the main list on its placeholder
    val sorted = when {
        isRealHydroponicStructure -> dashboardLoaded
        isFarmerBoxCandidate -> boxChecked
        else -> true
    }
    val readingSensors = if (sorted) sensors.filter { it.hasReading } else sensors
    val silentSensors = if (sorted) sensors.filterNot { it.hasReading } else emptyList()
    var noReadingsExpanded by rememberSaveable { mutableStateOf(true) }
    val outOfRange = readingSensors.count { it.statusBadgeType == BadgeType.CAUTION || it.statusBadgeType == BadgeType.CRITICAL }

    val statusSubtitle = when {
        !sorted -> "Checking for readings…"
        silentSensors.isNotEmpty() ->
            "${silentSensors.size} sensor${if (silentSensors.size == 1) "" else "s"} with no readings."
        outOfRange > 0 ->
            "$outOfRange reading${if (outOfRange == 1) "" else "s"} outside the optimal range."
        activeStructure.structureType == StructureType.FODDER -> "All fodder sensors reporting normally."
        else -> "All sensors reporting normally."
    }

    // The structure's camera
    val cameraMac = activeStructure.cameraMac ?: dashboardSummary?.cameraMac
    var showLatestPhoto by remember { mutableStateOf(false) }

    if (showLatestPhoto && cameraMac != null) {
        LatestPhotoSheet(
            structureName = activeStructure.title,
            cameraMac = cameraMac,
            onDismiss = { showLatestPhoto = false },
            onOpenGallery = {
                showLatestPhoto = false
                onOpenGallery()
            }
        )
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
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
                contentPadding = PaddingValues(top = 16.dp, bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                // Header row with back button and centered structure dropdown
                item {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(bottom = 6.dp),
                        horizontalAlignment = Alignment.CenterHorizontally
                    ) {
                        Box(
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            if (onNavigateBack != null) {
                                IconButton(
                                    onClick = onNavigateBack,
                                    modifier = Modifier
                                        .align(Alignment.CenterStart)
                                        .size(44.dp)
                                        .semantics { contentDescription = "Back" }
                                ) {
                                    Icon(
                                        imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                                        contentDescription = "Back",
                                        tint = if (isDark) TextPrimaryDark else ForestGreenDark,
                                        modifier = Modifier.size(28.dp)
                                    )
                                }
                            }

                            // Centered structure dropdown
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = if (onNavigateBack != null) 48.dp else 0.dp),
                                contentAlignment = Alignment.Center
                            ) {
                                StructureSelectDropdown(
                                    structures = StructureStore.structures,
                                    selectedStructureId = activeStructureId,
                                    onStructureSelected = { id -> id?.let { onSwitchStructure(it) } },
                                    titleSuffix = " Status"
                                )
                            }
                        }

                        Spacer(modifier = Modifier.height(6.dp))

                        // Centered subtitle
                        Text(
                            text = if (useBox && boxTelemetry != null) BoxLinkSelector.explanation(boxLink) else statusSubtitle,
                            fontSize = 13.sp,
                            color = if (isDark) TextSecondaryDark else TextSecondary,
                            textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 16.dp)
                        )

                        // Only offered while there's a box to pair with and it isn't already
                        // answering; a farmer whose cards have no readings can jump straight to fixing that
                        if (isFarmerBoxCandidate && boxTelemetry == null) {
                            Text(
                                text = "Not connected · Pair with your box",
                                fontSize = 13.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = ForestGreen,
                                textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 16.dp, vertical = 2.dp)
                                    .clickable { onPairWithBox(activeStructure.id) }
                            )
                        }

                        // A signed-in person is offered Bluetooth until they have paired the box
                        if (DirectBox.pairingOffered(signedIn, hasBoxMac, hasSavedBluetooth)) {
                            Text(
                                text = "Cloud slow? Pair with your box over Bluetooth",
                                fontSize = 13.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = ForestGreen,
                                textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 16.dp, vertical = 2.dp)
                                    .clickable { onPairWithBox(activeStructure.id) }
                            )
                        }

                        // The mode buttons, shown over Bluetooth to a box that reports its mode
                        if (useBox) {
                            ModeControl.content(boxModeInfo, boxLink, requestedMode)?.let { content ->
                                Spacer(modifier = Modifier.height(10.dp))
                                ModeControlSection(content = content, onChoose = { chooseMode(it) })
                            }
                        }

                        // Only shown when this structure has a camera linked to it
                        if (ApiClient.isSignedIn() && !cameraMac.isNullOrBlank()) {
                            Spacer(modifier = Modifier.height(8.dp))
                            OutlinedButton(
                                onClick = { showLatestPhoto = true },
                                shape = RoundedCornerShape(20.dp),
                                border = BorderStroke(1.dp, ForestGreen.copy(alpha = 0.6f)),
                                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 6.dp)
                            ) {
                                Icon(
                                    painter = painterResource(id = R.drawable.ic_camera),
                                    contentDescription = null,
                                    tint = ForestGreen,
                                    modifier = Modifier.size(16.dp)
                                )
                                Spacer(modifier = Modifier.width(8.dp))
                                Text(text = "Latest photo", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = ForestGreen)
                            }
                        }
                    }
                }

                // Sensors that are giving a reading
                items(readingSensors) { sensor ->
                    SensorCard(
                        sensor = sensor,
                        controlEnabled = isRealHydroponicStructure || useBox,
                        resetKey = toggleResetKey,
                        onClick = {
                            onNavigateToSensorDetail(activeStructure.id, sensor.id)
                        },
                        onToggle = { turnOn ->
                            if (useBox) {
                                sendBoxToggle(sensor.id, turnOn)
                            } else if (isRealHydroponicStructure) {
                                sendActuatorToggle(sensor.id, turnOn)
                            }
                        }
                    )
                }

                // Sensors that are not connected or not reporting, kept apart from the ones that are
                if (silentSensors.isNotEmpty()) {
                    item {
                        NoReadingsHeader(
                            count = silentSensors.size,
                            expanded = noReadingsExpanded,
                            onToggle = { noReadingsExpanded = !noReadingsExpanded }
                        )
                    }

                    if (noReadingsExpanded) {
                        items(silentSensors) { sensor ->
                            NoReadingCard(
                                sensor = sensor,
                                controlEnabled = isRealHydroponicStructure || useBox,
                                resetKey = toggleResetKey,
                                // Water has no detail page of its own, so only the sensors that do can be opened
                                onClick = if (sensor.id in SensorDetailMapper.supportedSensorIds) {
                                    { onNavigateToSensorDetail(activeStructure.id, sensor.id) }
                                } else {
                                    null
                                },
                                onToggle = { turnOn ->
                                    if (useBox) {
                                        sendBoxToggle(sensor.id, turnOn)
                                    } else if (isRealHydroponicStructure) {
                                        sendActuatorToggle(sensor.id, turnOn)
                                    }
                                }
                            )
                        }
                    }
                }
            }
        }
    }
}