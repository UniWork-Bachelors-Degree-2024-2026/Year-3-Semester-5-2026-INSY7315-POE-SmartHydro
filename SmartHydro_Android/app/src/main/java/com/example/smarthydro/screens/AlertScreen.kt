package com.example.smarthydro.screens

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.DoneAll
import androidx.compose.material.icons.filled.FilterAltOff
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.NotificationsOff
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material.icons.filled.WifiOff
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.components.AlertCard
import com.example.smarthydro.components.AlertFilterSheet
import com.example.smarthydro.components.AlertStatCard
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.AlertStore
import com.example.smarthydro.models.AlertItem
import com.example.smarthydro.models.AlertSeverity
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

private const val PAGE_SIZE = 5

// Which alerts the tabs show
private enum class AlertStatusFilter(val label: String) {
    ALL("All Alerts"),
    ACTIVE("Active"),
    RESOLVED("Resolved")
}

// Everything the tabs, stat cards and Filter sheet can narrow the list by
private data class AlertFilters(
    val status: AlertStatusFilter = AlertStatusFilter.ALL,
    val severity: AlertSeverity? = null,
    val location: String? = null
) {
    val extraFilterCount: Int
        get() = listOfNotNull(severity, location).size

    fun matches(alert: AlertItem): Boolean {
        val statusMatches = when (status) {
            AlertStatusFilter.ALL -> true
            AlertStatusFilter.ACTIVE -> !alert.isResolved
            AlertStatusFilter.RESOLVED -> alert.isResolved
        }
        return statusMatches &&
                (severity == null || alert.severity == severity) &&
                (location == null || alert.location == location)
    }
}

@Composable
fun AlertsScreen(
    onNavigateToSensorDetail: (structureId: String, sensorId: String) -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var filters by remember { mutableStateOf(AlertFilters()) }
    var shownCount by remember { mutableStateOf(PAGE_SIZE) }
    var showFilterSheet by remember { mutableStateOf(false) }
    var showClearDialog by remember { mutableStateOf(false) }
    var isClearing by remember { mutableStateOf(false) }

    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(30_000)
            now = Instant.now()
        }
    }

    LaunchedEffect(Unit) { AlertStore.refresh() }

    // Pull to refresh does the same as the refresh button in the header
    var isRefreshing by remember { mutableStateOf(false) }
    fun refresh() {
        if (isRefreshing) return
        isRefreshing = true
        scope.launch {
            try {
                AlertStore.refresh()
            } finally {
                isRefreshing = false
            }
        }
    }

    fun showMessage(message: String) {
        scope.launch { snackbarHostState.showSnackbar(message) }
    }

    // Changing filters starts the list from the top again
    fun applyFilters(newFilters: AlertFilters) {
        filters = newFilters
        shownCount = PAGE_SIZE
    }

    fun acknowledge(alert: AlertItem) {
        scope.launch {
            showMessage(
                if (AlertStore.acknowledge(alert.id)) "Alert acknowledged and moved to Resolved."
                else "Couldn't acknowledge the alert. Check your connection and try again."
            )
        }
    }

    fun clearAll() {
        isClearing = true
        scope.launch {
            val result = AlertStore.clearAll()
            isClearing = false
            showClearDialog = false
            showMessage(describeCleared(result.cleared, result.failed))
        }
    }

    val allAlerts = AlertStore.alerts
    val matches = allAlerts.filter { filters.matches(it) }
    val shown = matches.take(shownCount)
    val locations = allAlerts.map { it.location }.distinct().sorted()

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
                    item {
                        AlertsHeader(
                            isOnline = AlertStore.isOnline,
                            lastSyncText = describeSync(AlertStore.lastSyncedAt, now),
                            isLoading = AlertStore.isLoading,
                            onRefresh = { refresh() }
                        )
                    }

                    item {
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            // Only active alerts can be cleared
                            OutlinedButton(
                                onClick = { showClearDialog = true },
                                enabled = AlertStore.activeCount > 0,
                                modifier = Modifier
                                    .weight(1f)
                                    .height(44.dp),
                                shape = RoundedCornerShape(12.dp),
                                border = BorderStroke(1.dp, if (isDark) CardBorderStrongDark else CardBorder),
                                colors = smartHydroOutlinedButtonColors(isDark = isDark)
                            ) {
                                Icon(imageVector = Icons.Filled.DoneAll, contentDescription = null, modifier = Modifier.size(17.dp))
                                Spacer(modifier = Modifier.width(6.dp))
                                Text(text = "Clear All", fontWeight = FontWeight.SemiBold)
                            }

                            Button(
                                onClick = { showFilterSheet = true },
                                modifier = Modifier
                                    .weight(1f)
                                    .height(44.dp),
                                shape = RoundedCornerShape(12.dp),
                                colors = smartHydroButtonColors(
                                    containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                                    isDark = isDark
                                )
                            ) {
                                Icon(imageVector = Icons.Filled.Tune, contentDescription = null, modifier = Modifier.size(17.dp))
                                Spacer(modifier = Modifier.width(6.dp))
                                Text(text = "Filter", fontWeight = FontWeight.SemiBold)
                                if (filters.extraFilterCount > 0) {
                                    Spacer(modifier = Modifier.width(6.dp))
                                    CountBadge(
                                        count = filters.extraFilterCount,
                                        background = if (isDark) AccentGreenDark else Color.White,
                                        text = if (isDark) Color.White else ForestGreenDark
                                    )
                                }
                            }
                        }
                    }

                    AlertStore.loadError?.let { error ->
                        item { LoadErrorBanner(message = error) }
                    }

                    item {
                        StatCards(
                            onShowAlerts = { applyFilters(AlertFilters(AlertStatusFilter.ACTIVE, AlertSeverity.ALERT)) },
                            onShowCautions = { applyFilters(AlertFilters(AlertStatusFilter.ACTIVE, AlertSeverity.CAUTION)) },
                            onShowResolved = { applyFilters(AlertFilters(AlertStatusFilter.RESOLVED)) }
                        )
                    }

                    item {
                        StatusTabs(
                            selected = filters.status,
                            // The tabs keep any severity / location filter, like MVC
                            onSelect = { status -> applyFilters(filters.copy(status = status)) }
                        )
                    }

                    if (matches.isNotEmpty()) {
                        item {
                            Text(
                                text = "Showing ${shown.size} of ${matches.size} ${if (matches.size == 1) "alert" else "alerts"}",
                                fontSize = 13.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }
                    }

                    when {
                        AlertStore.isLoading && allAlerts.isEmpty() -> item {
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 40.dp),
                                contentAlignment = Alignment.Center
                            ) {
                                CircularProgressIndicator(color = ForestGreen)
                            }
                        }

                        matches.isEmpty() -> item {
                            EmptyAlerts(
                                filters = filters,
                                onResetFilters = { applyFilters(AlertFilters(status = filters.status)) }
                            )
                        }

                        else -> {
                            items(shown, key = { it.id }) { alert ->
                                AlertCard(
                                    alert = alert,
                                    onClick = {
                                        // Not every alert has a matching sensor screen, so there is simply nothing to open for those
                                        alert.targetSensorId?.let { onNavigateToSensorDetail(alert.structureId, it) }
                                    },
                                    onAcknowledge = { acknowledge(alert) }
                                )
                            }

                            if (shown.size < matches.size) {
                                item {
                                    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                                        OutlinedButton(
                                            onClick = { shownCount += PAGE_SIZE },
                                            shape = RoundedCornerShape(12.dp),
                                            border = BorderStroke(1.dp, if (isDark) CardBorderStrongDark else CardBorder),
                                            colors = smartHydroOutlinedButtonColors(isDark = isDark)
                                        ) {
                                            Text(
                                                text = "Load More Alerts",
                                                fontWeight = FontWeight.SemiBold,
                                                color = if (isDark) TextPrimaryDark else ForestGreenDark
                                            )
                                            Spacer(modifier = Modifier.width(4.dp))
                                            Icon(
                                                imageVector = Icons.Filled.KeyboardArrowDown,
                                                contentDescription = null,
                                                tint = if (isDark) TextPrimaryDark else ForestGreenDark
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

        SnackbarHost(
            hostState = snackbarHostState,
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(16.dp)
        )
    }

    if (showFilterSheet) {
        AlertFilterSheet(
            severity = filters.severity,
            location = filters.location,
            locations = locations,
            onApply = { severity, location ->
                applyFilters(filters.copy(severity = severity, location = location))
                showFilterSheet = false
            },
            onDismiss = { showFilterSheet = false }
        )
    }

    // Clear All pop-up
    if (showClearDialog) {
        val active = AlertStore.activeCount
        ConfirmationDialog(
            title = "Clear all active alerts?",
            message = if (active == 1) {
                "The active alert will be marked as cleared by the operator and moved to Resolved."
            } else {
                "All $active active alerts will be marked as cleared by the operator and moved to Resolved."
            },
            confirmButtonText = "Clear All",
            icon = Icons.Filled.DoneAll,
            isLoading = isClearing,
            onConfirm = { clearAll() },
            onDismiss = { showClearDialog = false }
        )
    }
}

// Title, ONLINE / OFFLINE pill and last sync time
@Composable
private fun AlertsHeader(isOnline: Boolean, lastSyncText: String, isLoading: Boolean, onRefresh: () -> Unit) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = "System Alerts",
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )
            Spacer(modifier = Modifier.height(4.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                ConnectionPill(isOnline = isOnline)
                Spacer(modifier = Modifier.width(8.dp))
                Text(text = "Last sync: $lastSyncText", fontSize = 13.sp, color = secondaryText)
            }
        }

        Box(modifier = Modifier.size(40.dp), contentAlignment = Alignment.Center) {
            if (isLoading) {
                CircularProgressIndicator(modifier = Modifier.size(20.dp), color = ForestGreen, strokeWidth = 2.dp)
            } else {
                IconButton(onClick = onRefresh) {
                    Icon(imageVector = Icons.Filled.Refresh, contentDescription = "Refresh alerts", tint = ForestGreen)
                }
            }
        }
    }
}

// Green ONLINE, grey OFFLINE
@Composable
private fun ConnectionPill(isOnline: Boolean) {
    val isDark = LocalThemeIsDark.current
    val background = if (isOnline) (if (isDark) StatusGoodBgDark else StatusGoodBg) else (if (isDark) SurfaceDarkVariant else BackgroundLight)
    val text = if (isOnline) (if (isDark) StatusGoodTextDark else StatusGoodText) else (if (isDark) TextSecondaryDark else TextSecondary)

    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(8.dp))
            .background(background)
            .border(1.dp, text.copy(alpha = 0.35f), RoundedCornerShape(8.dp))
            .padding(horizontal = 8.dp, vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(
            imageVector = if (isOnline) Icons.Filled.Wifi else Icons.Filled.WifiOff,
            contentDescription = null,
            tint = text,
            modifier = Modifier.size(13.dp)
        )
        Spacer(modifier = Modifier.width(4.dp))
        Text(
            text = if (isOnline) "ONLINE" else "OFFLINE",
            fontSize = 11.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 0.5.sp,
            color = text
        )
    }
}

@Composable
private fun LoadErrorBanner(message: String) {
    val isDark = LocalThemeIsDark.current
    val text = if (isDark) TextSecondaryDark else TextSecondary
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(12.dp))
            .padding(horizontal = 14.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(imageVector = Icons.Filled.CloudOff, contentDescription = null, tint = text, modifier = Modifier.size(18.dp))
        Spacer(modifier = Modifier.width(10.dp))
        Text(text = message, fontSize = 13.sp, lineHeight = 18.sp, color = text)
    }
}

// The three summary cards
@Composable
private fun StatCards(onShowAlerts: () -> Unit, onShowCautions: () -> Unit, onShowResolved: () -> Unit) {
    val isDark = LocalThemeIsDark.current
    val alertCount = AlertStore.alertCount
    val cautionCount = AlertStore.cautionCount

    Row(
        modifier = Modifier.height(IntrinsicSize.Min),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        AlertStatCard(
            icon = Icons.Filled.Warning,
            iconTint = if (isDark) StatusCautionTextDark else StatusCautionText,
            iconBackground = if (isDark) StatusCautionBgDark else StatusCautionBg,
            count = alertCount,
            label = if (alertCount == 1) "Alert" else "Alerts",
            caption = "Need action now",
            onClick = onShowAlerts,
            modifier = Modifier.weight(1f).fillMaxHeight()
        )
        AlertStatCard(
            icon = Icons.Filled.Info,
            iconTint = if (isDark) StatusWarnTextDark else StatusWarnText,
            iconBackground = if (isDark) StatusWarnBgDark else StatusWarnBg,
            count = cautionCount,
            label = if (cautionCount == 1) "Caution" else "Cautions",
            caption = "Keep an eye on",
            onClick = onShowCautions,
            modifier = Modifier.weight(1f).fillMaxHeight()
        )
        AlertStatCard(
            icon = Icons.Filled.CheckCircle,
            iconTint = if (isDark) StatusGoodTextDark else StatusGoodText,
            iconBackground = if (isDark) StatusGoodBgDark else StatusGoodBg,
            count = AlertStore.resolvedCount,
            label = "Resolved",
            caption = "Cleared so far",
            onClick = onShowResolved,
            modifier = Modifier.weight(1f).fillMaxHeight()
        )
    }
}

@Composable
private fun StatusTabs(selected: AlertStatusFilter, onSelect: (AlertStatusFilter) -> Unit) {
    val isDark = LocalThemeIsDark.current

    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        AlertStatusFilter.values().forEach { tab ->
            val isSelected = tab == selected
            val count = when (tab) {
                AlertStatusFilter.ALL -> AlertStore.alerts.size
                AlertStatusFilter.ACTIVE -> AlertStore.activeCount
                AlertStatusFilter.RESOLVED -> AlertStore.resolvedCount
            }

            Row(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(20.dp))
                    .background(if (isSelected) (if (isDark) PrimaryButtonBgDark else ForestGreenDark) else MaterialTheme.colorScheme.surface)
                    .border(
                        1.dp,
                        if (isSelected) (if (isDark) PrimaryButtonBgDark else ForestGreenDark) else (if (isDark) CardBorderDark else CardBorder),
                        RoundedCornerShape(20.dp)
                    )
                    .clickable { onSelect(tab) }
                    .padding(vertical = 9.dp),
                horizontalArrangement = Arrangement.Center,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = tab.label,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = if (isSelected) Color.White else MaterialTheme.colorScheme.onSurface,
                    maxLines = 1
                )
                Spacer(modifier = Modifier.width(6.dp))
                CountBadge(
                    count = count,
                    background = if (isSelected) Color.White.copy(alpha = 0.2f) else (if (isDark) SurfaceDarkVariant else BackgroundLight),
                    text = if (isSelected) Color.White else (if (isDark) TextSecondaryDark else TextSecondary)
                )
            }
        }
    }
}

@Composable
private fun CountBadge(count: Int, background: Color, text: Color) {
    Box(
        modifier = Modifier
            .clip(CircleShape)
            .background(background)
            .padding(horizontal = 7.dp, vertical = 1.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(text = count.toString(), fontSize = 11.sp, fontWeight = FontWeight.Bold, color = text)
    }
}

@Composable
private fun EmptyAlerts(filters: AlertFilters, onResetFilters: () -> Unit) {
    val isDark = LocalThemeIsDark.current

    val (icon, title, text) = when {
        filters.extraFilterCount > 0 -> Triple(
            Icons.Filled.FilterAltOff, "No alerts match these filters", "Try removing a filter to see more of your alerts."
        )
        filters.status == AlertStatusFilter.ACTIVE -> Triple(
            Icons.Filled.CheckCircle, "All clear", "There are no active alerts right now."
        )
        filters.status == AlertStatusFilter.RESOLVED -> Triple(
            Icons.Filled.NotificationsOff, "No resolved alerts", "Alerts you acknowledge or that clear themselves will be listed here."
        )
        else -> Triple(
            Icons.Filled.NotificationsOff, "Nothing to show yet", "Alerts from your sensors and devices will appear here."
        )
    }

    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, if (isDark) CardBorderDark else CardBorder)
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 36.dp, horizontal = 20.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Box(
                modifier = Modifier
                    .size(64.dp)
                    .clip(CircleShape)
                    .background(if (isDark) StatusGoodBgDark else StatusGoodBg)
                    .border(1.dp, ForestGreen.copy(alpha = 0.3f), CircleShape),
                contentAlignment = Alignment.Center
            ) {
                Icon(imageVector = icon, contentDescription = null, tint = ForestGreen, modifier = Modifier.size(28.dp))
            }
            Spacer(modifier = Modifier.height(14.dp))
            Text(
                text = title,
                fontSize = 17.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else TextPrimary,
                textAlign = TextAlign.Center
            )
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = text,
                fontSize = 13.sp,
                lineHeight = 18.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                textAlign = TextAlign.Center
            )
            if (filters.extraFilterCount > 0) {
                Spacer(modifier = Modifier.height(12.dp))
                OutlinedButton(
                    onClick = onResetFilters,
                    shape = RoundedCornerShape(12.dp),
                    border = BorderStroke(1.dp, if (isDark) AccentGreenDark else ForestGreen),
                    colors = smartHydroOutlinedButtonColors(isDark = isDark)
                ) {
                    Text(text = "Reset filters", color = if (isDark) TextPrimaryDark else ForestGreenDark, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

private fun describeSync(syncedAt: Instant?, now: Instant): String {
    if (syncedAt == null) return "Not synced yet"
    val elapsed = Duration.between(syncedAt, now)
    return when {
        elapsed.seconds < 60 -> "Just now"
        elapsed.toMinutes() < 60 -> "${elapsed.toMinutes()} min ago"
        elapsed.toHours() < 24 -> "${elapsed.toHours()} hr ago"
        else -> syncedAt.atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("d MMM, h:mm a", Locale.ENGLISH))
    }
}

private fun describeCleared(cleared: Int, failed: Int): String {
    val message = when (cleared) {
        0 -> "There were no active alerts to clear."
        1 -> "1 active alert was cleared and moved to Resolved."
        else -> "$cleared active alerts were cleared and moved to Resolved."
    }
    return if (failed > 0) "$message $failed could not be cleared. Please try again." else message
}