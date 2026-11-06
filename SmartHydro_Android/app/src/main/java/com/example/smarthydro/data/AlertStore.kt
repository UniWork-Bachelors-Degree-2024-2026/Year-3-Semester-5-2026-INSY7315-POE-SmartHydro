package com.example.smarthydro.data

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.models.ResolveAlertDto
import com.example.smarthydro.models.AlertItem
import com.example.smarthydro.models.AlertSeverity
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import java.time.Instant

// How a Clear All went. how many alerts were moved to Resolved and how many couldn't be
data class ClearAllResult(val cleared: Int, val failed: Int)

// Alerts shared by the Alerts screen and the Today's Alerts panel, backed by the real Alerts API
object AlertStore {

    private const val PAGE_SIZE = 100
    private const val MAX_PAGES = 50
    private const val CLEAR_BATCH_SIZE = 10

    // Most urgent first. active alerts, then active cautions, then resolved, newest first in each
    val alerts = mutableStateListOf<AlertItem>()

    var isLoading by mutableStateOf(false)
        private set

    var loadError by mutableStateOf<String?>(null)
        private set

    // False when the last refresh couldn't reach the server at all
    var isOnline by mutableStateOf(true)
        private set

    // When alerts last loaded successfully, for the "Last sync" text
    var lastSyncedAt by mutableStateOf<Instant?>(null)
        private set

    val activeCount: Int get() = alerts.count { !it.isResolved }
    val resolvedCount: Int get() = alerts.count { it.isResolved }
    val alertCount: Int get() = alerts.count { !it.isResolved && it.severity == AlertSeverity.ALERT }
    val cautionCount: Int get() = alerts.count { !it.isResolved && it.severity == AlertSeverity.CAUTION }

    // Loads every page of alerts, resolved and unresolved
    suspend fun refresh() {
        if (!ApiClient.isSignedIn()) {
            alerts.clear()
            loadError = null
            return
        }

        isLoading = true
        loadError = null
        try {
            val loaded = mutableListOf<AlertItem>()
            for (page in 1..MAX_PAGES) {
                val response = ApiClient.service.getAlerts(unresolvedOnly = false, page = page, pageSize = PAGE_SIZE)
                val body = response.body()
                if (!response.isSuccessful || body == null) {
                    loadError = "Alerts couldn't be loaded right now. Please try again."
                    break
                }
                loaded.addAll(body.items.map { it.toAlertItem() })
                if (page >= body.totalPages) break
            }

            isOnline = true
            if (loadError == null) {
                alerts.clear()
                alerts.addAll(loaded.sortedWith(urgentFirst))
                lastSyncedAt = Instant.now()
            }
        } catch (e: Exception) {
            isOnline = false
            loadError = "Couldn't reach the server. Check your connection and try again."
        } finally {
            isLoading = false
        }
    }

    // Resolves one alert, then reloads so its resolvedAt / clearedBy come from the API
    suspend fun acknowledge(id: String): Boolean {
        val alertId = id.toLongOrNull() ?: return false
        val succeeded = resolve(alertId, clearedByName())
        if (succeeded) refresh()
        return succeeded
    }

    // Resolves every active alert so it moves to Resolved and stays in the history
    suspend fun clearAll(): ClearAllResult {
        val clearedBy = clearedByName()
        val activeIds = alerts.filter { !it.isResolved }.mapNotNull { it.id.toLongOrNull() }
        var cleared = 0
        var failed = 0

        // A few at a time, so a long list doesn't take ages one by one
        for (batch in activeIds.chunked(CLEAR_BATCH_SIZE)) {
            val outcomes = coroutineScope {
                batch.map { id -> async { resolve(id, clearedBy) } }.awaitAll()
            }
            cleared += outcomes.count { it }
            failed += outcomes.count { !it }
        }

        refresh()
        return ClearAllResult(cleared, failed)
    }

    private suspend fun resolve(id: Long, clearedBy: String): Boolean {
        return try {
            ApiClient.service.resolveAlert(id, ResolveAlertDto(clearedBy = clearedBy)).isSuccessful
        } catch (e: Exception) {
            false
        }
    }

    // Who cleared an alert. The signed-in email or the role if there's no email
    private fun clearedByName(): String =
        ApiClient.getSignedInEmail() ?: ApiClient.getSignedInRole() ?: "Operator"

    // 0 = active alert, 1 = active caution, 2 = resolved
    private val AlertItem.sortRank: Int
        get() = when {
            isResolved -> 2
            severity == AlertSeverity.ALERT -> 0
            else -> 1
        }

    private val urgentFirst = compareBy<AlertItem> { it.sortRank }.thenByDescending { it.occurredAt }
}