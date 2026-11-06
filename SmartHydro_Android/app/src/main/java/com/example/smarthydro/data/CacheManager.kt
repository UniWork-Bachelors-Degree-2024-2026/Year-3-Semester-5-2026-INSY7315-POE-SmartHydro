package com.example.smarthydro.data

import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.local.LocalDatabase
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Coordinates user-scoped caching across SQLite (Room), in-memory reactive state lists,
 * and persistent storage.
 *
 * Rules:
 * 1. If the SAME user logs in (or the app is reopened), cache remains intact and loads in 0ms.
 * 2. If a DIFFERENT user logs in, the previous user's cached tables and in-memory lists
 *    are immediately wiped so there is zero ghost data or cross-account leakage.
 * 3. Clearing user cache NEVER affects offline mode: structures added without an account ("local-" ids),
 *    their box IP and Bluetooth address, and readings still waiting to upload all stay on the phone.
 */
object CacheManager {

    /**
     * Called whenever a user successfully authenticates.
     * Compares the new user's email with the last logged-in user email.
     * If they differ, all previous user data is safely purged before loading the new user's data.
     */
    suspend fun onUserAuthenticated(newEmail: String?) {
        val lastEmail = ApiClient.getLastLoggedInEmail()
        val normalizedNew = newEmail?.trim()?.lowercase()
        val normalizedLast = lastEmail?.trim()?.lowercase()

        if (!normalizedLast.isNullOrBlank() && normalizedNew != normalizedLast) {
            // A different user logged into this device: clear previous user's cached tables
            clearAllUserData()
        }

        // Record the current user as the active cache owner
        if (!normalizedNew.isNullOrBlank()) {
            ApiClient.setLastLoggedInEmail(normalizedNew)
        }
    }

    /**
     * Wipes all user-specific data from both SQLite (Room) and in-memory state stores.
     */
    suspend fun clearAllUserData() = withContext(Dispatchers.IO) {
        StructureDataCache.clear()

        try {
            // Only the previous account's cloud structures, offline ones are kept
            StructureStore.clearCache()
        } catch (_: Exception) {}

        try {
            SiteStore.clearCache()
        } catch (_: Exception) {}

        try {
            TeamStore.clearCache()
        } catch (_: Exception) {}

        try {
            AdminStore.clearCache()
        } catch (_: Exception) {}

        try {
            AlertStore.alerts.clear()
        } catch (_: Exception) {}

        try {
            // Only what the cloud already has is cleared; a farmer's readings still waiting to be
            // uploaded must survive someone else signing in on the same phone
            LocalDatabase.telemetryDao.deleteSynced()
        } catch (_: Exception) {}
    }
}