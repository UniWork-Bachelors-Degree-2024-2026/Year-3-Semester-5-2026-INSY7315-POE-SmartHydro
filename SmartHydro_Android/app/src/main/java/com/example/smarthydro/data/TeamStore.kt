package com.example.smarthydro.data

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.local.CachedTeamMember
import com.example.smarthydro.data.local.LocalDatabase
import com.example.smarthydro.models.CreateSiteInviteDto
import com.example.smarthydro.models.TeamMemberDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

// The values TeamMemberDto.status comes back as
object TeamStatus {
    const val JOINED = "Joined"
    const val INVITED = "Invited"
    const val EXPIRED = "Expired"
}

enum class TeamActionOutcome { DONE, NOT_ALLOWED, NOT_FOUND, FAILED }

data class TeamActionResult(val outcome: TeamActionOutcome, val message: String)

// The workers and pending invitations across every site the signed-in Operator manages.
object TeamStore {

    val members = mutableStateListOf<TeamMemberDto>()

    var isLoading by mutableStateOf(false)
        private set

    var loadError by mutableStateOf<String?>(null)
        private set

    init {
        loadFromCacheSync()
    }

    // Immediately loads cached team members on IO thread so TeamScreen opens with 0ms delay
    fun loadFromCacheSync() {
        try {
            CoroutineScope(Dispatchers.IO).launch {
                loadFromCache()
            }
        } catch (_: Exception) {
        }
    }

    // Loads cached team members from Room SQLite
    suspend fun loadFromCache() {
        try {
            val cached = LocalDatabase.teamDao.getAll().map { it.toDto() }
            if (cached.isNotEmpty() && members != cached) {
                members.replaceWithUnique(cached) { "${it.siteId}_${it.email}" }
            }
        } catch (_: Exception) {
        }
    }

    // Background fetch & diff against the backend API, returns true if network fetch succeeded
    suspend fun refresh(): Boolean {
        loadError = null

        if (ApiClient.getSignedInRole() != "Operator") {
            members.clear()
            return false
        }

        if (members.isEmpty()) {
            loadFromCache()
        }

        if (members.isEmpty()) {
            isLoading = true
        }

        var success = true
        try {
            val response = ApiClient.service.getTeam()
            val body = response.body()
            if (response.isSuccessful && body != null) {
                if (members != body) {
                    members.replaceWithUnique(body) { "${it.siteId}_${it.email}" }
                    LocalDatabase.teamDao.deleteAll()
                    LocalDatabase.teamDao.insertAll(body.map { CachedTeamMember.fromDto(it) })
                }
            } else {
                success = false
                if (members.isEmpty()) {
                    loadError = "Couldn't load your team."
                }
            }
        } catch (e: Exception) {
            success = false
            if (members.isEmpty()) {
                loadError = "Couldn't reach the server. Check your connection."
            }
        } finally {
            isLoading = false
        }
        return success
    }

    suspend fun invite(siteId: String, email: String): TeamActionResult {
        return try {
            val response = ApiClient.service.createSiteInvite(siteId, CreateSiteInviteDto(email))
            if (response.isSuccessful) {
                refresh()
                TeamActionResult(TeamActionOutcome.DONE, "Invitation sent to $email.")
            } else {
                failureFor(response.code())
            }
        } catch (e: Exception) {
            TeamActionResult(TeamActionOutcome.FAILED, "Couldn't reach the server. Check your connection.")
        }
    }

    suspend fun removeMember(siteId: String, userId: Int): TeamActionResult {
        // Optimistic removal from in-memory and local cache
        val memberToRemove = members.firstOrNull { it.siteId == siteId && it.userId == userId }
        members.removeAll { it.siteId == siteId && it.userId == userId }
        try {
            LocalDatabase.teamDao.deleteBySiteAndUserId(siteId, userId)
        } catch (_: Exception) {
        }

        return try {
            val response = ApiClient.service.removeTeamMember(siteId, userId)
            if (response.isSuccessful) {
                refresh()
                TeamActionResult(TeamActionOutcome.DONE, "Worker removed from the site.")
            } else {
                // Revert on failure if needed
                memberToRemove?.let {
                    if (!members.contains(it)) members.add(it)
                    LocalDatabase.teamDao.insert(CachedTeamMember.fromDto(it))
                }
                failureFor(response.code())
            }
        } catch (e: Exception) {
            memberToRemove?.let {
                if (!members.contains(it)) members.add(it)
                try { LocalDatabase.teamDao.insert(CachedTeamMember.fromDto(it)) } catch (_: Exception) {}
            }
            TeamActionResult(TeamActionOutcome.FAILED, "Couldn't reach the server. Check your connection.")
        }
    }

    suspend fun cancelInvite(siteId: String, email: String): TeamActionResult {
        // Optimistic cancellation from in-memory and local cache
        val inviteToRemove = members.firstOrNull { it.siteId == siteId && it.email.equals(email, ignoreCase = true) }
        members.removeAll { it.siteId == siteId && it.email.equals(email, ignoreCase = true) }
        try {
            LocalDatabase.teamDao.deleteBySiteAndEmail(siteId, email)
        } catch (_: Exception) {
        }

        return try {
            val response = ApiClient.service.cancelSiteInvite(siteId, email)
            if (response.isSuccessful) {
                refresh()
                TeamActionResult(TeamActionOutcome.DONE, "Invitation cancelled.")
            } else {
                inviteToRemove?.let {
                    if (!members.contains(it)) members.add(it)
                    LocalDatabase.teamDao.insert(CachedTeamMember.fromDto(it))
                }
                failureFor(response.code())
            }
        } catch (e: Exception) {
            inviteToRemove?.let {
                if (!members.contains(it)) members.add(it)
                try { LocalDatabase.teamDao.insert(CachedTeamMember.fromDto(it)) } catch (_: Exception) {}
            }
            TeamActionResult(TeamActionOutcome.FAILED, "Couldn't reach the server. Check your connection.")
        }
    }

    suspend fun clearCache() {
        members.clear()
        try {
            LocalDatabase.teamDao.deleteAll()
        } catch (_: Exception) {
        }
    }

    private fun failureFor(code: Int): TeamActionResult = when (code) {
        403 -> TeamActionResult(TeamActionOutcome.NOT_ALLOWED, "You don't manage this site.")
        404 -> TeamActionResult(TeamActionOutcome.NOT_FOUND, "That worker or invitation is no longer there.")
        429 -> TeamActionResult(TeamActionOutcome.FAILED, "Too many invites sent recently. Please wait before trying again.")
        else -> TeamActionResult(TeamActionOutcome.FAILED, "Something went wrong. Please try again.")
    }
}
