package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.ArrowDropUp
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.SensorIcon
import com.example.smarthydro.components.SimpleSensorCard
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.*
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.models.DosingCommandRequestDto
import com.example.smarthydro.models.StructureDashboardSummaryDto
import com.example.smarthydro.models.StructureType
import com.example.smarthydro.models.ThresholdResponseDto
import com.example.smarthydro.transport.KeepBoxConnected
import com.example.smarthydro.transport.rememberBoxSession
import com.example.smarthydro.ui.SimpleModeConnect
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.statusAnnouncement
import com.example.smarthydro.ui.theme.*
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Designed for Workers and accessible Offline Mode:
 * - Single-screen view with no bottom navigation bar
 * - Header with access to top bar and Settings
 * - Dropdown to switch structures if more than one structure exists;
 *   otherwise directly shows the cards for that structure
 * - Remembers the active structure across app sessions
 * - Streamlined, accessible sensor cards with large icons and prominent on/off toggles
 * - pH and EC cards have dedicated Up and Down dosing buttons (auto-dosing is always on)
 */
@Composable
fun SimpleModeScreen(
    onOpenSettings: () -> Unit = {},
    onPairWithBox: (String) -> Unit = {},
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    val structures = StructureStore.structures
    var selectedStructureId by remember(structures.map { it.id }) {
        val saved = ApiClient.getSimpleModeStructureId()
        val initialId = if (!saved.isNullOrBlank() && structures.any { it.id == saved }) {
            saved
        } else {
            structures.firstOrNull()?.id.orEmpty()
        }
        mutableStateOf(initialId)
    }

    val activeStructure = structures.find { it.id == selectedStructureId } ?: structures.firstOrNull()
    var isDropdownExpanded by remember { mutableStateOf(false) }

    // Persist active structure so it is remembered when reopening the app
    LaunchedEffect(activeStructure?.id) {
        val id = activeStructure?.id
        if (!id.isNullOrBlank()) {
            ApiClient.setSimpleModeStructureId(id)
        }
    }

    // Synchronize structures on launch
    LaunchedEffect(Unit) {
        StructureStore.loadFromCache()
        StructureStore.refresh()
        SiteStore.loadFromCache()
        SiteStore.refresh()
    }

    // Telemetry and actuator state
    val structureId = activeStructure?.id?.toIntOrNull()
    val isRealHydroponicStructure = activeStructure?.structureType == StructureType.HYDROPONIC &&
            ApiClient.isSignedIn() && structureId != null

    var dashboardSummary by remember(activeStructure?.id) { mutableStateOf<StructureDashboardSummaryDto?>(null) }
    var thresholds by remember(activeStructure?.id) { mutableStateOf<Map<String, ThresholdResponseDto>>(emptyMap()) }
    var thresholdsLoaded by remember(activeStructure?.id) { mutableStateOf(false) }
    var dashboardLoaded by remember(activeStructure?.id) { mutableStateOf(false) }
    var toggleResetKey by remember(activeStructure?.id) { mutableStateOf(0) }

    // Offline direct box support
    val isFarmerBoxCandidate = !ApiClient.isSignedIn() && !activeStructure?.macAddress.isNullOrBlank()
    var boxTelemetry by remember(activeStructure?.id) { mutableStateOf<BoxTelemetryDto?>(null) }
    var boxChecked by remember(activeStructure?.id) { mutableStateOf(false) }
    var boxRefreshKey by remember(activeStructure?.id) { mutableStateOf(0) }

    // Dosing state
    var pendingDose by remember { mutableStateOf<DoseOption?>(null) }
    var isDosing by remember { mutableStateOf(false) }

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
        } catch (_: Exception) {
        }
    }

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
        } catch (_: Exception) {
        }
        thresholdsLoaded = true
    }

    LaunchedEffect(activeStructure?.id) {
        if (activeStructure == null) return@LaunchedEffect
        val cached = if (isRealHydroponicStructure && structureId != null) StructureDataCache.summary(structureId) else null
        dashboardSummary = cached
        dashboardLoaded = cached != null
        loadDashboard()
        dashboardLoaded = true
    }

    LaunchedEffect(activeStructure?.id) {
        if (activeStructure == null || !isRealHydroponicStructure || structureId == null) return@LaunchedEffect
        val cached = StructureDataCache.thresholds(structureId)
        if (cached != null) {
            thresholds = cached
            thresholdsLoaded = true
            return@LaunchedEffect
        }
        loadThresholds()
    }

    // A box paired over Bluetooth is connected again while this screen is open, after a restart or a drop
    KeepBoxConnected(
        structureId = activeStructure?.id.orEmpty(),
        savedAddress = activeStructure?.boxBluetoothAddress,
        enabled = isFarmerBoxCandidate
    )

    // Reads the box through whichever link reaches it, and keeps each reading on the phone
    val boxSession = rememberBoxSession(
        key = activeStructure?.id.orEmpty(),
        refreshKey = boxRefreshKey,
        boxIpAddress = activeStructure?.boxIpAddress.orEmpty(),
        deviceMac = activeStructure?.macAddress.orEmpty(),
        enabled = isFarmerBoxCandidate
    )

    LaunchedEffect(boxSession) {
        if (boxSession == null) return@LaunchedEffect

        // The reading and whether the box has been checked change together, so the cards are never judged too early
        boxSession.state.collect {
            boxTelemetry = it.reading
            if (it.checked) boxChecked = true
        }
    }

    var isRefreshing by remember { mutableStateOf(false) }
    fun refresh() {
        if (isRefreshing) return
        isRefreshing = true
        scope.launch {
            try {
                StructureStore.refresh()
                if (isRealHydroponicStructure) {
                    loadDashboard()
                    loadThresholds()
                } else if (isFarmerBoxCandidate) {
                    boxRefreshKey++
                    delay(600)
                }
            } finally {
                isRefreshing = false
            }
        }
    }

    // Toggle continuous cloud actuator
    fun sendActuatorToggle(sensorId: String, turnOn: Boolean) {
        val mac = dashboardSummary?.actuatorState?.deviceMac ?: activeStructure?.macAddress
        val command = mac?.let { TelemetryMapper.continuousCommandFor(sensorId, it, turnOn) } ?: return

        scope.launch {
            try {
                val response = ApiClient.service.sendContinuousCommand(command)
                if (response.isSuccessful) {
                    dashboardSummary = dashboardSummary?.copy(actuatorState = response.body())
                    structureId?.let { id -> dashboardSummary?.let { StructureDataCache.putSummary(id, it) } }
                } else {
                    toggleResetKey++
                    Toast.makeText(context, "Command failed. Please try again.", Toast.LENGTH_SHORT).show()
                }
            } catch (_: Exception) {
                toggleResetKey++
                Toast.makeText(context, "Network error. Check connection.", Toast.LENGTH_SHORT).show()
            }
        }
    }

    // Toggle direct box actuator, through whichever link reaches the box
    fun sendBoxToggle(sensorId: String, turnOn: Boolean) {
        val actuator = BoxControl.actuatorFor(sensorId) ?: return
        val transport = boxSession?.transport?.value
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

    // Runs a dose directly on the paired box, through whichever link reaches it
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

    // Queues a dose with the API
    fun sendDose(dose: DoseOption) {
        if (isFarmerBoxCandidate) {
            sendBoxDose(dose)
            return
        }

        val mac = dashboardSummary?.actuatorState?.deviceMac ?: activeStructure?.macAddress
        if (mac == null) {
            Toast.makeText(context, "No controller is connected to this structure yet.", Toast.LENGTH_SHORT).show()
            return
        }

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
            } catch (_: Exception) {
                Toast.makeText(context, "Couldn't reach the server. Check your connection.", Toast.LENGTH_SHORT).show()
            } finally {
                isDosing = false
            }
        }
    }

    // Asks the person to confirm a dose
    fun requestDose(dose: DoseOption) {
        if (isFarmerBoxCandidate && boxTelemetry == null) {
            Toast.makeText(context, if (boxChecked) BoxControl.OFFLINE_MESSAGE else BoxControl.SEARCHING_MESSAGE, Toast.LENGTH_SHORT).show()
            return
        }
        pendingDose = dose
    }

    val sensors = when {
        isRealHydroponicStructure -> TelemetryMapper.hydroponicSensorNodes(
            telemetry = dashboardSummary?.latestTelemetry,
            actuators = dashboardSummary?.actuatorState,
            thresholds = thresholds,
            thresholdsLoaded = thresholdsLoaded
        )
        else -> BoxTelemetryMapper.sensorNodes(boxTelemetry)
    }

    // Modal Confirmation Dialog for Dosing
    pendingDose?.let { dose ->
        ConfirmationDialog(
            title = dose.label,
            message = "This runs a real pump and adds about ${dose.volumeText} mL. Continue?",
            confirmButtonText = "Dose",
            icon = Icons.Filled.Check,
            onConfirm = {
                val toSend = dose
                pendingDose = null
                sendDose(toSend)
            },
            onDismiss = { pendingDose = null }
        )
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .navigationBarsPadding()
            .background(MaterialTheme.colorScheme.background)
    ) {
        // Top Header Bar with Site Name and Settings gear icon
        TopHeaderBar()

        PullToRefresh(
            isRefreshing = isRefreshing,
            onRefresh = { refresh() }
        ) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 16.dp),
                contentPadding = PaddingValues(top = 16.dp, bottom = 48.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Header section: Structure Dropdown (if > 1) or Structure Heading (if <= 1)
                item {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(bottom = 4.dp),
                        horizontalAlignment = Alignment.CenterHorizontally
                    ) {
                        if (structures.size > 1) {
                            // Accessible Structure Selection Dropdown for multiple structures
                            Box(
                                modifier = Modifier.fillMaxWidth(),
                                contentAlignment = Alignment.Center
                            ) {
                                Surface(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .clip(RoundedCornerShape(16.dp))
                                        .border(
                                            width = 1.5.dp,
                                            color = if (isDark) ForestGreenLight.copy(alpha = 0.4f) else CardBorder,
                                            shape = RoundedCornerShape(16.dp)
                                        )
                                        .clickable(role = Role.DropdownList) {
                                            isDropdownExpanded = !isDropdownExpanded
                                        },
                                    color = if (isDark) SurfaceDarkVariant else MintBannerBg
                                ) {
                                    Row(
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .padding(horizontal = 16.dp, vertical = 14.dp),
                                        verticalAlignment = Alignment.CenterVertically,
                                        horizontalArrangement = Arrangement.SpaceBetween
                                    ) {
                                        Row(
                                            verticalAlignment = Alignment.CenterVertically,
                                            horizontalArrangement = Arrangement.spacedBy(12.dp),
                                            modifier = Modifier.weight(1f)
                                        ) {
                                            activeStructure?.let { struct ->
                                                Box(
                                                    modifier = Modifier
                                                        .size(36.dp)
                                                        .clip(RoundedCornerShape(10.dp))
                                                        .background(struct.themeColor),
                                                    contentAlignment = Alignment.Center
                                                ) {
                                                    SensorIcon(
                                                        iconType = struct.iconType,
                                                        tint = Color.White,
                                                        size = 20.dp
                                                    )
                                                }
                                            }

                                            Column {
                                                Text(
                                                    text = "Select Structure",
                                                    fontSize = 12.sp,
                                                    fontWeight = FontWeight.Medium,
                                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                                )
                                                Text(
                                                    text = activeStructure?.title ?: "Choose a Structure",
                                                    fontSize = 18.sp,
                                                    fontWeight = FontWeight.Bold,
                                                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                                                )
                                            }
                                        }

                                        Icon(
                                            imageVector = if (isDropdownExpanded) Icons.Filled.ArrowDropUp else Icons.Filled.ArrowDropDown,
                                            contentDescription = "Toggle Structure Dropdown",
                                            tint = if (isDark) TextPrimaryDark else ForestGreenDark,
                                            modifier = Modifier.size(28.dp)
                                        )
                                    }
                                }

                                DropdownMenu(
                                    expanded = isDropdownExpanded,
                                    onDismissRequest = { isDropdownExpanded = false },
                                    modifier = Modifier
                                        .fillMaxWidth(0.9f)
                                        .background(MaterialTheme.colorScheme.surface)
                                ) {
                                    structures.forEach { struct ->
                                        DropdownMenuItem(
                                            text = {
                                                Row(
                                                    verticalAlignment = Alignment.CenterVertically,
                                                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                                                ) {
                                                    Box(
                                                        modifier = Modifier
                                                            .size(30.dp)
                                                            .clip(RoundedCornerShape(8.dp))
                                                            .background(struct.themeColor),
                                                        contentAlignment = Alignment.Center
                                                    ) {
                                                        SensorIcon(
                                                            iconType = struct.iconType,
                                                            tint = Color.White,
                                                            size = 16.dp
                                                        )
                                                    }
                                                    Text(
                                                        text = struct.title,
                                                        fontSize = 16.sp,
                                                        fontWeight = if (struct.id == selectedStructureId) FontWeight.Bold else FontWeight.Normal,
                                                        color = MaterialTheme.colorScheme.onSurface
                                                    )
                                                }
                                            },
                                            trailingIcon = if (struct.id == selectedStructureId) {
                                                {
                                                    Icon(
                                                        imageVector = Icons.Filled.Check,
                                                        contentDescription = "Selected",
                                                        tint = ForestGreen,
                                                        modifier = Modifier.size(20.dp)
                                                    )
                                                }
                                            } else null,
                                            onClick = {
                                                selectedStructureId = struct.id
                                                ApiClient.setSimpleModeStructureId(struct.id)
                                                isDropdownExpanded = false
                                            }
                                        )
                                    }
                                }
                            }
                        } else if (structures.size == 1) {
                            // If only 1 structure, just show its title directly (no dropdown)
                            val single = structures.first()
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.Center,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 6.dp)
                            ) {
                                Box(
                                    modifier = Modifier
                                        .size(36.dp)
                                        .clip(RoundedCornerShape(10.dp))
                                        .background(single.themeColor),
                                    contentAlignment = Alignment.Center
                                ) {
                                    SensorIcon(
                                        iconType = single.iconType,
                                        tint = Color.White,
                                        size = 20.dp
                                    )
                                }
                                Spacer(modifier = Modifier.width(10.dp))
                                Text(
                                    text = single.title,
                                    fontSize = 21.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                                )
                            }
                        } else {
                            // Empty state: no structures
                            Text(
                                text = "No structures available",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = if (isDark) TextSecondaryDark else TextSecondary,
                                modifier = Modifier.padding(vertical = 12.dp)
                            )
                        }
                    }
                }

                // One large button to connect the box, only while the box is not answering
                if (activeStructure != null && SimpleModeConnect.shouldOffer(isFarmerBoxCandidate, boxChecked, boxTelemetry != null)) {
                    item {
                        Column(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            Text(
                                text = SimpleModeConnect.MESSAGE,
                                modifier = Modifier.statusAnnouncement(),
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = if (isDark) TextPrimaryDark else ForestGreenDark,
                                textAlign = TextAlign.Center
                            )
                            Button(
                                onClick = { onPairWithBox(activeStructure.id) },
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(64.dp),
                                shape = RoundedCornerShape(16.dp),
                                colors = ButtonDefaults.buttonColors(
                                    containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                                    contentColor = Color.White
                                )
                            ) {
                                Text(text = SimpleModeConnect.BUTTON, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }

                // Sensor Cards
                if (sensors.isNotEmpty()) {
                    items(sensors) { sensor ->
                        SimpleSensorCard(
                            sensor = sensor,
                            controlEnabled = (isRealHydroponicStructure || isFarmerBoxCandidate) && !isDosing,
                            resetKey = toggleResetKey,
                            onToggle = { turnOn ->
                                if (isRealHydroponicStructure) {
                                    sendActuatorToggle(sensor.id, turnOn)
                                } else if (isFarmerBoxCandidate) {
                                    sendBoxToggle(sensor.id, turnOn)
                                }
                            },
                            onDoseDown = {
                                val dose = DosingCatalog.minusFor(sensor.id)
                                if (dose != null) {
                                    requestDose(dose)
                                }
                            },
                            onDoseUp = {
                                val dose = DosingCatalog.plusFor(sensor.id)
                                if (dose != null) {
                                    requestDose(dose)
                                }
                            }
                        )
                    }
                } else if (activeStructure != null) {
                    item {
                        Surface(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(16.dp)),
                            color = MaterialTheme.colorScheme.surface
                        ) {
                            Column(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(32.dp),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.Filled.Refresh,
                                    contentDescription = null,
                                    tint = if (isDark) TextSecondaryDark else TextSecondary,
                                    modifier = Modifier.size(36.dp)
                                )
                                Text(
                                    text = "Loading sensor readings…",
                                    fontSize = 16.sp,
                                    fontWeight = FontWeight.Medium,
                                    color = if (isDark) TextSecondaryDark else TextSecondary,
                                    textAlign = TextAlign.Center
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
