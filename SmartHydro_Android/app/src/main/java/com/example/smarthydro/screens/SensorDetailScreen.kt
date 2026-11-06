package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.BarChartView
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.GaugeArc
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.ThresholdEditSheet
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.components.WaterStatusCard
import com.example.smarthydro.data.BoxControl
import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.data.DoseOption
import com.example.smarthydro.data.DosingCatalog
import com.example.smarthydro.data.HistoryLogic
import com.example.smarthydro.data.SensorDetailMapper
import com.example.smarthydro.data.StructureDataCache
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.data.TelemetryHistoryStore
import com.example.smarthydro.data.TelemetryMapper
import com.example.smarthydro.data.ThresholdCatalog
import com.example.smarthydro.data.ThresholdEditor
import com.example.smarthydro.data.TrendMapper
import com.example.smarthydro.transport.KeepBoxConnected
import com.example.smarthydro.transport.rememberBoxSession
import com.example.smarthydro.models.ActuatorHistoryDto
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.models.DosingCommandRequestDto
import com.example.smarthydro.models.StructureDashboardSummaryDto
import com.example.smarthydro.models.TelemetryDailyHistoryDto
import com.example.smarthydro.models.ThresholdResponseDto
import com.example.smarthydro.models.BadgeType
import com.example.smarthydro.models.MetricTileData
import com.example.smarthydro.models.SensorDetailData
import com.example.smarthydro.ui.readAsOne
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.switchLabel
import com.example.smarthydro.ui.theme.*
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch

@Composable
fun SensorDetailScreen(
    sensorId: String,
    structureId: String = "",
    onNavigateBack: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    // A real hydroponic structure with a server id gets a live Details page for the five
    // sensors that map cleanly onto real telemetry; everything else is shown as "no reading",
    // same as the readings list
    val canLoadLiveData = ApiClient.isSignedIn() && structureId.toIntOrNull() != null && sensorId in SensorDetailMapper.supportedSensorIds

    var summary by remember(structureId) { mutableStateOf<StructureDashboardSummaryDto?>(null) }
    var history by remember(structureId) { mutableStateOf<List<TelemetryDailyHistoryDto>>(emptyList()) }
    var actuatorHistory by remember(structureId) { mutableStateOf<List<ActuatorHistoryDto>>(emptyList()) }
    var thresholds by remember { mutableStateOf<Map<String, ThresholdResponseDto>>(emptyMap()) }

    // True once the first fetch (or the first answer from the box) is in, so "no readings" is
    // only claimed after we have actually asked, and not while the page is still loading
    var summaryLoaded by remember(structureId) { mutableStateOf(false) }

    // Pressing "Check again" bumps the count, which restarts the loading below, and isChecking
    // shows the button as busy until an answer comes back
    var refreshCount by remember(structureId) { mutableIntStateOf(0) }
    var isChecking by remember(structureId) { mutableStateOf(false) }
    var boxChecked by remember(structureId) { mutableStateOf(false) }

    // The history and the recent activity load separately, so each says "loading" until its own answer is in
    var historyLoaded by remember(structureId) { mutableStateOf(false) }
    var activityLoaded by remember(structureId) { mutableStateOf(false) }

    LaunchedEffect(structureId, sensorId, refreshCount) {
        if (!canLoadLiveData) return@LaunchedEffect

        val id = structureId.toInt()

        // What was fetched a moment ago, such as on the readings screen, shows straight away
        StructureDataCache.summary(id)?.let {
            summary = it
            summaryLoaded = true
        }
        StructureDataCache.thresholds(id)?.let { thresholds = it }

        coroutineScope {
            // The history and the activity both need the controller's MAC, so they start together as soon as it is known
            fun loadHistoryAndActivity(mac: String) {
                launch {
                    try {
                        val response = ApiClient.service.getTelemetryHistory(mac, days = 7)
                        if (response.isSuccessful) {
                            history = response.body() ?: emptyList()
                        }
                    } catch (e: CancellationException) {
                        throw e
                    } catch (e: Exception) {
                        // The trend chart is simply empty rather than showing a guessed history
                    }
                    historyLoaded = true
                }

                launch {
                    try {
                        val response = ApiClient.service.getActuatorHistory(mac, page = 1, pageSize = 20)
                        if (response.isSuccessful) {
                            actuatorHistory = response.body()?.items ?: emptyList()
                        }
                    } catch (e: CancellationException) {
                        throw e
                    } catch (e: Exception) {
                        // Recent activity is simply empty rather than showing a guessed log
                    }
                    activityLoaded = true
                }
            }

            val cachedMac = summary?.actuatorState?.deviceMac
            val startedEarly = !cachedMac.isNullOrBlank()
            if (startedEarly) loadHistoryAndActivity(cachedMac!!)

            val summaryJob = async {
                try {
                    val response = ApiClient.service.getStructureDashboardSummary(id)
                    if (response.isSuccessful) {
                        response.body()?.let {
                            summary = it
                            StructureDataCache.putSummary(id, it)
                        }
                    }
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    // The page still renders with placeholders rather than guessing a reading
                }
                summaryLoaded = true
            }

            val thresholdsJob = async {
                if (StructureDataCache.thresholds(id) == null) {
                    try {
                        val response = ApiClient.service.getThresholdsByStructure(id)
                        if (response.isSuccessful) {
                            val byType = response.body()?.thresholds?.associateBy { it.sensorType.uppercase() }.orEmpty()
                            thresholds = byType
                            StructureDataCache.putThresholds(id, byType)
                        }
                    } catch (e: CancellationException) {
                        throw e
                    } catch (e: Exception) {
                        // The page still renders without an optimal-range badge
                    }
                }
            }

            summaryJob.await()
            if (!startedEarly) {
                val mac = summary?.actuatorState?.deviceMac
                if (!mac.isNullOrBlank()) {
                    loadHistoryAndActivity(mac)
                } else {
                    historyLoaded = true
                    activityLoaded = true
                }
            }
            thresholdsJob.await()
        }

        isChecking = false
    }

    // A farmer who is not signed in but has paired a box reads it directly, with no cloud involved
    val boxStructure = remember(structureId) { StructureStore.find(structureId) }
    val isFarmerBox = !ApiClient.isSignedIn() && !boxStructure?.macAddress.isNullOrBlank() && sensorId in SensorDetailMapper.supportedSensorIds

    var boxReading by remember(structureId) { mutableStateOf<BoxTelemetryDto?>(null) }
    var localDays by remember(structureId) { mutableStateOf<List<HistoryLogic.DayPoint>>(emptyList()) }

    // A box paired over Bluetooth is connected again while this screen is open, after a restart or a drop
    KeepBoxConnected(
        structureId = structureId,
        savedAddress = boxStructure?.boxBluetoothAddress,
        enabled = isFarmerBox
    )

    // Reads the box through whichever link reaches it, and keeps each reading on the phone
    val boxSession = rememberBoxSession(
        key = structureId,
        refreshKey = refreshCount,
        boxIpAddress = boxStructure?.boxIpAddress.orEmpty(),
        deviceMac = boxStructure?.macAddress.orEmpty(),
        enabled = isFarmerBox && boxStructure != null
    )

    LaunchedEffect(structureId, isFarmerBox, refreshCount) {
        if (!isFarmerBox || boxStructure == null) return@LaunchedEffect

        // What was already recorded shows straight away, before the box has even answered
        localDays = TelemetryHistoryStore.dailyHistory(boxStructure.macAddress.orEmpty())
        historyLoaded = true
        activityLoaded = true
    }

    LaunchedEffect(boxSession) {
        if (boxSession == null || boxStructure == null) return@LaunchedEffect
        val mac = boxStructure.macAddress.orEmpty()
        var historyLoadedAtMs = 0L

        boxSession.state.collect {
            boxReading = it.reading
            if (it.checked) {
                boxChecked = true
                isChecking = false
            }

            // The chart takes in what was kept on the phone, at the pace the box was once polled at, since a
            // link like Bluetooth reports far more often than that
            val now = System.currentTimeMillis()
            if (it.reading != null && now - historyLoadedAtMs >= HistoryLogic.BOX_POLL_INTERVAL_MS) {
                historyLoadedAtMs = now
                localDays = TelemetryHistoryStore.dailyHistory(mac)
            }
        }
    }

    val built: SensorDetailData? = when {
        canLoadLiveData -> SensorDetailMapper.build(
            sensorId = sensorId,
            telemetry = summary?.latestTelemetry,
            actuators = summary?.actuatorState,
            actuatorHistory = actuatorHistory,
            thresholds = thresholds
        )
        isFarmerBox -> SensorDetailMapper.buildFromBox(sensorId, boxReading)
        // Not signed in and no box paired: the page is shown as "no reading", never with sample numbers
        else -> SensorDetailMapper.build(sensorId, null, null, emptyList(), emptyMap())
    }

    // A sensor with no details page has nothing to show here, so the person is taken back
    if (built == null) {
        LaunchedEffect(Unit) { onNavigateBack() }
        return
    }
    val builtData: SensorDetailData = built

    // While a real page is still loading, it does not claim "no readings" yet
    val settled = when {
        canLoadLiveData -> summaryLoaded
        isFarmerBox -> boxChecked
        else -> true
    }
    val data = if (settled) builtData else builtData.copy(hasReading = true)

    // Only real history is ever charted, so a page with no source gets no chart numbers at all
    val trend = when {
        canLoadLiveData -> TrendMapper.fromApi(sensorId, history)
        isFarmerBox -> TrendMapper.fromLocal(sensorId, localDays)
        else -> emptyList()
    }

    var isSwitchOn by remember(data) { mutableStateOf(data.toggleOn) }

    // The switch works for a signed-in structure whose controller is known, or directly on a paired box. Anywhere
    // else it is shown switched off and disabled, with the reason, instead of pretending to work
    val controlNote: String? = when {
        canLoadLiveData && summary?.actuatorState?.deviceMac != null -> null
        canLoadLiveData -> if (summaryLoaded) "No controller is connected to this structure yet" else "Waiting for the box's state"
        isFarmerBox -> null
        else -> "Not connected yet"
    }

    // Switches this sensor's relay directly on the paired box, through whichever link reaches it.
    // With no box to reach, the person is told so plainly and the switch goes back to where it was
    fun sendBoxToggle(turnOn: Boolean) {
        val actuator = BoxControl.actuatorFor(sensorId) ?: return
        val transport = boxSession?.transport?.value

        if (boxReading == null || transport == null) {
            Toast.makeText(context, if (boxChecked) BoxControl.OFFLINE_MESSAGE else BoxControl.SEARCHING_MESSAGE, Toast.LENGTH_SHORT).show()
            isSwitchOn = !turnOn
            return
        }

        scope.launch {
            when (val result = transport.setRelay(actuator, turnOn)) {
                is BoxControlResult.Done -> boxReading = boxReading?.copy(
                    relays = result.relays ?: BoxControl.withRelay(boxReading?.relays, actuator, turnOn)
                )
                BoxControlResult.Offline -> {
                    Toast.makeText(context, BoxControl.OFFLINE_MESSAGE, Toast.LENGTH_SHORT).show()
                    isSwitchOn = !turnOn
                }
                is BoxControlResult.Refused -> {
                    Toast.makeText(context, result.message, Toast.LENGTH_LONG).show()
                    isSwitchOn = !turnOn
                }
            }
        }
    }

    // Sends the relay command for this sensor's control, if it has a real one to send
    fun sendActuatorToggle(turnOn: Boolean) {
        if (isFarmerBox) {
            sendBoxToggle(turnOn)
            return
        }

        val mac = summary?.actuatorState?.deviceMac
        val command = if (canLoadLiveData && mac != null) TelemetryMapper.continuousCommandFor(sensorId, mac, turnOn) else null

        // The switch is disabled whenever there is no real command to send
        if (command == null) return

        scope.launch {
            try {
                val response = ApiClient.service.sendContinuousCommand(command)
                if (response.isSuccessful) {
                    summary = summary?.copy(actuatorState = response.body())
                    summary?.let { StructureDataCache.putSummary(structureId.toInt(), it) }
                } else {
                    isSwitchOn = !turnOn
                    Toast.makeText(context, "Couldn't change that. Please try again.", Toast.LENGTH_SHORT).show()
                }
            } catch (e: Exception) {
                isSwitchOn = !turnOn
                Toast.makeText(context, "Couldn't reach the server. Check your connection.", Toast.LENGTH_SHORT).show()
            }
        }
    }

    // The doses behind the "-" and "+" buttons of this page, and the box they would go to
    val minusDose = if (data.hasMinusButton) DosingCatalog.minusFor(sensorId) else null
    val plusDose = if (data.hasPlusButton) DosingCatalog.plusFor(sensorId) else null
    val doseMac = summary?.actuatorState?.deviceMac

    // A dose waits here until the person confirms it, because it runs a real pump
    var pendingDose by remember { mutableStateOf<DoseOption?>(null) }
    var isDosing by remember { mutableStateOf(false) }

    // Why the dose buttons cannot be used right now, or null when they can
    val doseNote: String? = when {
        !data.isDosingControl -> null
        isFarmerBox -> null
        !canLoadLiveData -> "Dosing needs you to be signed in"
        !DosingCatalog.canDose(ApiClient.getSignedInRole()) -> "Only operators and admins can dose"
        doseMac == null -> if (summaryLoaded) "No controller is connected to this structure yet" else "Waiting for the box's state"
        else -> null
    }

    // Runs the dose directly on the paired box, through whichever link reaches it
    fun sendBoxDose(dose: DoseOption) {
        val transport = boxSession?.transport?.value
        if (transport == null) {
            Toast.makeText(context, BoxControl.OFFLINE_MESSAGE, Toast.LENGTH_SHORT).show()
            return
        }

        isDosing = true
        scope.launch {
            val message = when (val result = transport.dose(dose.type, dose.volumeMl)) {
                is BoxControlResult.Done -> "${dose.label} (${dose.volumeText} mL) started." +
                    (result.durationMs?.let { " The pump runs for about ${it / 1000} seconds." } ?: "")
                BoxControlResult.Offline -> BoxControl.OFFLINE_MESSAGE
                is BoxControlResult.Refused -> result.message
            }
            Toast.makeText(context, message, Toast.LENGTH_LONG).show()
            isDosing = false
        }
    }

    // Asks the person to confirm a dose. On a paired box that cannot be reached it says so right away instead
    fun requestDose(dose: DoseOption) {
        if (isFarmerBox && boxReading == null) {
            Toast.makeText(context, if (boxChecked) BoxControl.OFFLINE_MESSAGE else BoxControl.SEARCHING_MESSAGE, Toast.LENGTH_SHORT).show()
            return
        }

        pendingDose = dose
    }

    // Queues the dose with the API. The box picks it up with its next update and runs the pump
    fun sendDose(dose: DoseOption) {
        if (isFarmerBox) {
            sendBoxDose(dose)
            return
        }

        val mac = doseMac ?: return

        isDosing = true
        scope.launch {
            try {
                val response = ApiClient.service.enqueueDosingCommand(
                    DosingCommandRequestDto(deviceMac = mac, dosingType = dose.type, targetVolumeMl = dose.volumeMl)
                )
                val message = when {
                    response.isSuccessful -> "${dose.label} (${dose.volumeText} mL) queued. The box runs it on its next update."
                    response.code() == 403 -> "Only operators and admins can dose."
                    else -> extractAuthErrorMessage(response.errorBody()?.string()) ?: "Couldn't send that dose. Please try again."
                }
                Toast.makeText(context, message, Toast.LENGTH_LONG).show()
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Toast.makeText(context, "Couldn't reach the server. Check your connection.", Toast.LENGTH_SHORT).show()
            } finally {
                isDosing = false
            }
        }
    }

    pendingDose?.let { dose ->
        ConfirmationDialog(
            title = dose.label,
            message = "This runs a real pump and adds about ${dose.volumeText} mL. Continue?",
            confirmButtonText = "Dose",
            icon = Icons.Filled.Check,
            onConfirm = {
                pendingDose = null
                sendDose(dose)
            },
            onDismiss = { pendingDose = null }
        )
    }

    // Operators and admins can change this reading's optimal range from here
    val rangeSensor = ThresholdCatalog.forSensorId(sensorId)
    val canEditRange = canLoadLiveData && ThresholdEditor.canEdit() && rangeSensor != null && thresholds[rangeSensor.key] != null
    var showRangeSheet by remember { mutableStateOf(false) }

    if (showRangeSheet && canEditRange && rangeSensor != null) {
        ThresholdEditSheet(
            structureId = structureId.toInt(),
            structureName = boxStructure?.title ?: "This structure",
            sensor = rangeSensor,
            thresholds = thresholds,
            onDismiss = { showRangeSheet = false },
            onChanged = { updated, message ->
                thresholds = updated
                showRangeSheet = false
                Toast.makeText(context, message, Toast.LENGTH_SHORT).show()
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
            isRefreshing = isChecking && (canLoadLiveData || isFarmerBox),
            onRefresh = {
                if (!isChecking && (canLoadLiveData || isFarmerBox)) {
                    isChecking = true
                    refreshCount++
                }
            }
        ) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 16.dp),
                contentPadding = PaddingValues(top = 12.dp, bottom = 28.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                // Subheader Row: < Back and Title
                item {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 4.dp)
                    ) {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            modifier = Modifier
                                .align(Alignment.CenterStart)
                                .clip(RoundedCornerShape(8.dp))
                                .clickable { onNavigateBack() }
                                .padding(horizontal = 4.dp, vertical = 4.dp)
                        ) {
                            Icon(
                                painter = painterResource(id = R.drawable.ic_chevron_left),
                                contentDescription = "Back",
                                tint = if (isDark) TextSecondaryDark else TextSecondary,
                                modifier = Modifier.size(20.dp)
                            )
                            Text(
                                text = "Back",
                                fontSize = 15.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }

                        Text(
                            text = data.title,
                            fontSize = 24.sp,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.onSurface,
                            modifier = Modifier.screenHeading().align(Alignment.Center)
                        )
                    }
                }

                // Top Actuator Toggle Banner
                item {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(16.dp))
                            .background(if (isDark) SurfaceDarkVariant else MintBannerBg)
                            .border(1.dp, if (isDark) CardBorderDark else Color(0xFFC7E5CB), RoundedCornerShape(16.dp))
                            .padding(horizontal = 16.dp, vertical = 14.dp)
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = data.toggleLabel,
                                    fontSize = 18.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = controlNote ?: if (isSwitchOn) "On · tap to switch" else "Off · tap to switch",
                                    fontSize = 13.sp,
                                    fontWeight = FontWeight.Normal,
                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }

                            Switch(
                                checked = isSwitchOn && controlNote == null,
                                enabled = controlNote == null,
                                modifier = Modifier.switchLabel(data.toggleLabel),
                                onCheckedChange = {
                                    isSwitchOn = it
                                    sendActuatorToggle(it)
                                },
                                colors = SwitchDefaults.colors(
                                    checkedThumbColor = SwitchThumb,
                                    checkedTrackColor = ForestGreen,
                                    uncheckedThumbColor = SwitchThumb,
                                    uncheckedTrackColor = SwitchTrackInactive,
                                    uncheckedBorderColor = Color.Transparent
                                )
                            )
                        }
                    }
                }

                // Metric Section: Gauge Arc OR Multi-Metric 2x2 Grid
                if (data.isMultiMetric) {
                    item {
                        Column(
                            modifier = Modifier.fillMaxWidth(),
                            verticalArrangement = Arrangement.spacedBy(12.dp)
                        ) {
                            val tile0 = data.multiMetricTiles.getOrNull(0)
                            val tile1 = data.multiMetricTiles.getOrNull(1)
                            if (tile0 != null && tile1 != null) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                                ) {
                                    MultiMetricTile(tile = tile0, modifier = Modifier.weight(1f))
                                    MultiMetricTile(tile = tile1, modifier = Modifier.weight(1f))
                                }
                            }

                            val tile2 = data.multiMetricTiles.getOrNull(2)
                            val tile3 = data.multiMetricTiles.getOrNull(3)
                            if (tile2 != null && tile3 != null) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                                ) {
                                    MultiMetricTile(tile = tile2, modifier = Modifier.weight(1f))
                                    MultiMetricTile(tile = tile3, modifier = Modifier.weight(1f))
                                }
                            }
                        }
                    }
                } else if (data.sensorId == "water") {
                    item {
                        WaterStatusCard(data = data, isLoading = !settled)
                    }
                } else {
                    item {
                        GaugeArc(
                            data = data,
                            isLoading = !settled,
                            // A button is live only when this page has that dose, nothing blocks it and no dose is being sent
                            onDoseMinus = if (doseNote == null && minusDose != null && !isDosing) ({ requestDose(minusDose) }) else null,
                            onDosePlus = if (doseNote == null && plusDose != null && !isDosing) ({ requestDose(plusDose) }) else null,
                            minusLabel = minusDose?.let { "${it.label}, ${it.volumeText} mL" } ?: "Decrease dosing",
                            plusLabel = plusDose?.let { "${it.label}, ${it.volumeText} mL" } ?: "Increase dosing",
                            doseNote = doseNote
                        )
                    }
                }

                // Shortcut to change this reading's optimal range
                if (canEditRange) {
                    item {
                        Box(
                            modifier = Modifier.fillMaxWidth(),
                            contentAlignment = Alignment.Center
                        ) {
                            TextButton(onClick = { showRangeSheet = true }) {
                                Icon(
                                    imageVector = Icons.Filled.Edit,
                                    contentDescription = null,
                                    tint = if (isDark) AccentGreenDark else ForestGreen,
                                    modifier = Modifier.size(16.dp)
                                )
                                Spacer(modifier = Modifier.width(6.dp))
                                Text(
                                    text = "Edit optimal range",
                                    fontSize = 14.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    color = if (isDark) AccentGreenDark else ForestGreen
                                )
                            }
                        }
                    }
                }

                // With no reading yet there is nothing to look at but the history, so the person can ask again
                if (!data.hasReading && (canLoadLiveData || isFarmerBox)) {
                    item {
                        OutlinedButton(
                            onClick = {
                                isChecking = true
                                refreshCount++
                            },
                            enabled = !isChecking,
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(48.dp),
                            shape = RoundedCornerShape(12.dp),
                            border = BorderStroke(1.dp, if (isDark) CardBorderStrongDark else CardBorder),
                            colors = smartHydroOutlinedButtonColors(isDark = isDark)
                        ) {
                            if (isChecking) {
                                CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
                            } else {
                                Icon(imageVector = Icons.Filled.Refresh, contentDescription = null, modifier = Modifier.size(18.dp))
                            }
                            Spacer(modifier = Modifier.width(8.dp))
                            Text(text = if (isChecking) "Checking…" else "Check again", fontWeight = FontWeight.SemiBold)
                        }
                    }
                }

                // 7-Day Trend Chart (not shown for sensors that only report a schedule, like the sprayer)
                if (data.showTrend) {
                    item {
                        BarChartView(
                            points = trend,
                            barColor = data.accentColor,
                            subject = data.title,
                            unit = data.unit,
                            isLoading = !historyLoaded && (canLoadLiveData || isFarmerBox),
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                }

                // Recent Activity Timeline Card
                item {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 16.dp, vertical = 6.dp)
                    ) {
                        Text(
                            text = "Recent activity",
                            fontSize = 16.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (isDark) TextPrimaryDark else ForestGreenDark,
                            modifier = Modifier.padding(bottom = 8.dp)
                        )

                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(12.dp))
                                .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(12.dp))
                                .background(MaterialTheme.colorScheme.surface)
                                .padding(horizontal = 16.dp, vertical = 8.dp)
                        ) {
                            Column(modifier = Modifier.fillMaxWidth()) {
                                if (data.recentActivities.isEmpty()) {
                                    Text(
                                        text = if (!activityLoaded && (canLoadLiveData || isFarmerBox)) "Loading…" else "No recent activity",
                                        fontSize = 13.sp,
                                        color = if (isDark) TextSecondaryDark else TextSecondary,
                                        modifier = Modifier.padding(vertical = 12.dp)
                                    )
                                }

                                data.recentActivities.forEachIndexed { index, activity ->
                                    Row(
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .readAsOne()
                                            .padding(vertical = 12.dp),
                                        horizontalArrangement = Arrangement.SpaceBetween,
                                        verticalAlignment = Alignment.CenterVertically
                                    ) {
                                        Text(
                                            text = activity.time,
                                            fontSize = 14.sp,
                                            fontWeight = FontWeight.Bold,
                                            color = MaterialTheme.colorScheme.onSurface
                                        )
                                        Text(
                                            text = activity.description,
                                            fontSize = 13.sp,
                                            fontWeight = FontWeight.Normal,
                                            color = if (isDark) TextSecondaryDark else TextSecondary,
                                            textAlign = TextAlign.End
                                        )
                                    }

                                    if (index < data.recentActivities.size - 1) {
                                        HorizontalDivider(
                                            color = if (isDark) CardDividerDark else CardDivider,
                                            thickness = 1.dp
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun MultiMetricTile(
    tile: MetricTileData,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current

    Box(
        modifier = modifier
            .readAsOne()
            .clip(RoundedCornerShape(14.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp))
            .background(MaterialTheme.colorScheme.surface)
            .padding(14.dp)
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(
                text = tile.label,
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface
            )

            Spacer(modifier = Modifier.height(6.dp))

            Text(
                text = buildAnnotatedString {
                    withStyle(SpanStyle(fontSize = 28.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)) {
                        append(tile.value)
                    }
                    if (tile.unit != null) {
                        withStyle(SpanStyle(fontSize = 14.sp, fontWeight = FontWeight.Normal, color = if (isDark) TextSecondaryDark else TextSecondary)) {
                            append(" ${tile.unit}")
                        }
                    }
                }
            )

            Spacer(modifier = Modifier.height(8.dp))

            val (bg, textColor) = when (tile.badgeType) {
                BadgeType.GOOD -> if (isDark) StatusGoodBgDark to StatusGoodTextDark else StatusGoodBg to StatusGoodText
                BadgeType.CAUTION, BadgeType.CRITICAL -> if (isDark) StatusCautionBgDark to StatusCautionTextDark else StatusCautionBg to StatusCautionText
                BadgeType.NEUTRAL -> if (isDark) StatusNeutralBgDark to StatusNeutralTextDark else StatusNeutralBg to StatusNeutralText
            }

            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(12.dp))
                    .background(bg)
                    .padding(horizontal = 10.dp, vertical = 2.dp)
            ) {
                Text(
                    text = tile.badgeText,
                    color = textColor,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.SemiBold
                )
            }
        }
    }
}