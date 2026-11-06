package com.example.smarthydro.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.example.smarthydro.api.ApiClient

/**
 * Simple Mode provides a streamlined, accessible, single-screen experience:
 * - Automatically active for online users with role "Worker"
 * - Manually toggleable via Settings strictly when in Offline Mode
 * - Suppresses the bottom navigation bar and keeps everything on one screen
 * - Remembers the active structure across app sessions
 */
object SimpleModeState {
    var offlineSimpleMode by mutableStateOf(false)

    val isActive: Boolean
        get() = if (ApiClient.isSignedIn()) {
            ApiClient.getSignedInRole().equals("Worker", ignoreCase = true)
        } else {
            offlineSimpleMode
        }

    fun syncFromPreferences() {
        offlineSimpleMode = ApiClient.isOfflineSimpleMode()
    }

    fun setOfflineMode(enabled: Boolean) {
        offlineSimpleMode = enabled
        ApiClient.setOfflineSimpleMode(enabled)
    }

    fun getRememberedStructureId(): String? = ApiClient.getSimpleModeStructureId()

    fun rememberStructureId(id: String?) {
        ApiClient.setSimpleModeStructureId(id)
    }
}
