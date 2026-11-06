package com.example.smarthydro.data

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.example.smarthydro.api.ApiClient
import com.google.gson.reflect.TypeToken

// The physical farm locations a structure can belong to. Site grouping is only shown to
// signed-in team members (see StructureScreen); a farmer's own structures are shown flat,
// since a single farm never needs this distinction.
data class Site(val id: String, val name: String)

object SiteStore {

    val sites = mutableStateListOf<Site>()

    var isLoading by mutableStateOf(false)
        private set

    var loadError by mutableStateOf<String?>(null)
        private set

    init {
        // Load immediately from local persistent cache on startup so the top bar,
        // site dropdowns, and structure groupings render in 0ms without waiting for network
        loadFromCacheSync()
    }

    // Synchronous immediate memory load from SharedPreferences so views never start empty
    fun loadFromCacheSync() {
        try {
            val json = ApiClient.getCachedSitesJson()
            if (!json.isNullOrBlank()) {
                val type = object : TypeToken<List<Site>>() {}.type
                val cached: List<Site>? = ApiClient.gson.fromJson(json, type)
                if (!cached.isNullOrEmpty() && sites.isEmpty()) {
                    sites.replaceWithUnique(cached) { it.id }
                }
            }
        } catch (_: Exception) {
        }
    }

    // Asynchronous Room load to populate from SQLite database
    suspend fun loadFromCache() {
        try {
            val roomSites = com.example.smarthydro.data.local.LocalDatabase.siteDao.getAll().map {
                Site(id = it.id, name = it.name)
            }
            if (roomSites.isNotEmpty()) {
                if (roomSites != sites.toList()) {
                    sites.replaceWithUnique(roomSites) { it.id }
                    ApiClient.saveCachedSitesJson(ApiClient.gson.toJson(roomSites))
                }
            } else {
                loadFromCacheSync()
            }
        } catch (_: Exception) {
            loadFromCacheSync()
        }
    }

    suspend fun clearCache() {
        sites.clear()
        ApiClient.saveCachedSitesJson(null)
        try {
            com.example.smarthydro.data.local.LocalDatabase.siteDao.deleteAll()
        } catch (_: Exception) {
        }
    }

    fun nameFor(siteId: String?): String =
        sites.find { it.id == siteId }?.name ?: "Unassigned"

    // Fetches the sites the signed-in user can see.
    // Cache-first: immediately displays cached sites, then syncs with API in background.
    suspend fun refresh() {
        if (!ApiClient.isSignedIn()) {
            loadError = null
            return
        }

        // If memory is empty, load cached sites immediately
        if (sites.isEmpty()) {
            loadFromCache()
        }

        if (sites.isEmpty()) {
            isLoading = true
        }
        loadError = null

        try {
            val response = ApiClient.service.getSites()
            val body = response.body()
            if (response.isSuccessful && body != null) {
                val newSites = body.map { Site(id = it.id, name = it.name) }
                // Diff check: only update if changes occurred on backend
                if (newSites != sites.toList()) {
                    sites.replaceWithUnique(newSites) { it.id }
                    ApiClient.saveCachedSitesJson(ApiClient.gson.toJson(newSites))
                    try {
                        val roomEntities = newSites.map {
                            com.example.smarthydro.data.local.CachedSite(id = it.id, name = it.name)
                        }
                        com.example.smarthydro.data.local.LocalDatabase.siteDao.deleteAll()
                        com.example.smarthydro.data.local.LocalDatabase.siteDao.insertAll(roomEntities)
                    } catch (_: Exception) {
                    }
                }
            } else if (sites.isEmpty()) {
                loadError = "Couldn't load sites."
            }
        } catch (e: Exception) {
            if (sites.isEmpty()) {
                loadError = "Couldn't reach the server. Check your connection."
            }
        } finally {
            isLoading = false
        }
    }
}
