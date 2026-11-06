package com.example.smarthydro.data

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.local.CachedAdminUser
import com.example.smarthydro.data.local.CachedPendingOperator
import com.example.smarthydro.data.local.LocalDatabase
import com.example.smarthydro.models.AdminSiteDto
import com.example.smarthydro.models.AdminUserDto
import com.example.smarthydro.models.PendingOperatorDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

object AdminStore {

    val users = mutableStateListOf<AdminUserDto>()
    val pendingOperators = mutableStateListOf<PendingOperatorDto>()
    val sites = mutableStateListOf<AdminSiteDto>()

    var isLoading by mutableStateOf(false)
        private set

    var loadError by mutableStateOf<String?>(null)
        private set

    init {
        loadFromCacheSync()
    }

    // Immediately loads cached admin data on IO thread so screens open without a blank frame
    fun loadFromCacheSync() {
        try {
            CoroutineScope(Dispatchers.IO).launch {
                loadFromCache()
            }
        } catch (_: Exception) {
        }
    }

    // Loads cached admin users and pending operators from Room SQLite
    suspend fun loadFromCache() {
        try {
            val cachedUsers = LocalDatabase.adminUserDao.getAll().map { it.toAdminUserDto() }
            if (cachedUsers.isNotEmpty() && users != cachedUsers) {
                users.replaceWithUnique(cachedUsers) { it.id }
            }

            val cachedPending = LocalDatabase.adminUserDao.getPendingAll().map { it.toPendingOperatorDto() }
            if (cachedPending.isNotEmpty() && pendingOperators != cachedPending) {
                pendingOperators.replaceWithUnique(cachedPending) { it.id }
            }

            val cachedSites = LocalDatabase.adminUserDao.getSitesAll().map { it.toAdminSiteDto() }
            if (cachedSites.isNotEmpty() && sites != cachedSites) {
                sites.replaceWithUnique(cachedSites) { it.id }
            }
        } catch (_: Exception) {
        }
    }

    // Background fetch & diff against the backend API, returns true if network fetch succeeded
    suspend fun refresh(): Boolean {
        loadError = null

        if (!ApiClient.isSignedIn() || ApiClient.getSignedInRole() != "Admin") {
            return false
        }

        if (users.isEmpty() || sites.isEmpty()) {
            loadFromCache()
        }

        if (users.isEmpty() && pendingOperators.isEmpty() && sites.isEmpty()) {
            isLoading = true
        }

        var success = true
        try {
            val usersRes = ApiClient.service.getAdminUsers()
            val pendingRes = ApiClient.service.getAdminPendingOperators()
            val sitesRes = ApiClient.service.getAdminSites()

            if (usersRes.isSuccessful) {
                val freshUsers = usersRes.body() ?: emptyList()
                if (users != freshUsers) {
                    users.replaceWithUnique(freshUsers) { it.id }
                    LocalDatabase.adminUserDao.deleteAll()
                    LocalDatabase.adminUserDao.insertAll(freshUsers.map { CachedAdminUser.fromDto(it) })
                }
            } else {
                success = false
                if (users.isEmpty()) {
                    loadError = "Failed to load user directory"
                }
            }

            if (pendingRes.isSuccessful) {
                val freshPending = pendingRes.body() ?: emptyList()
                if (pendingOperators != freshPending) {
                    pendingOperators.replaceWithUnique(freshPending) { it.id }
                    LocalDatabase.adminUserDao.deleteAllPending()
                    LocalDatabase.adminUserDao.insertPendingAll(freshPending.map { CachedPendingOperator.fromDto(it) })
                }
            } else {
                success = false
            }

            if (sitesRes.isSuccessful) {
                val freshSites = sitesRes.body() ?: emptyList()
                if (sites != freshSites) {
                    sites.replaceWithUnique(freshSites) { it.id }
                    LocalDatabase.adminUserDao.deleteAllSites()
                    LocalDatabase.adminUserDao.insertSitesAll(freshSites.map { com.example.smarthydro.data.local.CachedAdminSite.fromDto(it) })
                }
            } else {
                success = false
            }
        } catch (e: Exception) {
            success = false
            if (users.isEmpty() && sites.isEmpty()) {
                loadError = "Network error: check connection."
            }
        } finally {
            isLoading = false
        }
        return success
    }

    // Optimistically update or mutate local list + Room
    suspend fun removeUser(userId: Int) {
        users.removeAll { it.id == userId }
        try {
            LocalDatabase.adminUserDao.deleteById(userId)
        } catch (_: Exception) {
        }
    }

    suspend fun removePendingOperator(id: Int) {
        pendingOperators.removeAll { it.id == id }
        try {
            LocalDatabase.adminUserDao.deletePendingById(id)
        } catch (_: Exception) {
        }
    }

    suspend fun updateSiteOwner(siteId: String, newOwnerEmail: String) {
        val index = sites.indexOfFirst { it.id == siteId }
        if (index != -1) {
            val updated = sites[index].copy(ownerEmail = newOwnerEmail)
            sites[index] = updated
            try {
                LocalDatabase.adminUserDao.insertSitesAll(listOf(com.example.smarthydro.data.local.CachedAdminSite.fromDto(updated)))
            } catch (_: Exception) {
            }
        }
    }

    suspend fun clearCache() {
        users.clear()
        pendingOperators.clear()
        sites.clear()
        try {
            LocalDatabase.adminUserDao.deleteAll()
            LocalDatabase.adminUserDao.deleteAllPending()
            LocalDatabase.adminUserDao.deleteAllSites()
        } catch (_: Exception) {
        }
    }
}
