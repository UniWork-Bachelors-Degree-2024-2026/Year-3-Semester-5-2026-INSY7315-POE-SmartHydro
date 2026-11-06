package com.example.smarthydro

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.AdminStore
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.data.TeamStore
import com.example.smarthydro.data.local.LocalDatabase
import com.example.smarthydro.ui.theme.HighContrast
import com.example.smarthydro.ui.theme.SmartHydroTheme
import com.example.smarthydro.ui.theme.systemPrefersHighContrast

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Loads the saved JWT and user preferences before any screen can ask ApiClient for it
        ApiClient.init(applicationContext)
        // Opens (or creates) the on-device readings cache before any screen can use it
        LocalDatabase.init(applicationContext)
        // Pre-populates structures and sites into memory synchronously so the first screen renders without delay
        StructureStore.loadFromCacheSync()
        SiteStore.loadFromCacheSync()
        AdminStore.loadFromCacheSync()
        TeamStore.loadFromCacheSync()
        // Applies the saved high contrast choice, or the device contrast setting if none was saved, before the first frame
        HighContrast.enabled = ApiClient.getSavedHighContrast(systemPrefersHighContrast(applicationContext))
        com.example.smarthydro.ui.SimpleModeState.syncFromPreferences()
        enableEdgeToEdge()
        setContent {
            val systemInDark = isSystemInDarkTheme()
            // Initializes dark mode state from persisted SharedPreferences if saved, or falls back to system theme
            var isDarkMode by remember { mutableStateOf(ApiClient.getSavedDarkMode(systemInDark)) }

            SmartHydroTheme(
                darkTheme = isDarkMode,
                onToggleDarkTheme = {
                    val nextMode = !isDarkMode
                    isDarkMode = nextMode
                    ApiClient.setDarkMode(nextMode)
                }
            ) {
                AppNavigation()
            }
        }
    }
}
