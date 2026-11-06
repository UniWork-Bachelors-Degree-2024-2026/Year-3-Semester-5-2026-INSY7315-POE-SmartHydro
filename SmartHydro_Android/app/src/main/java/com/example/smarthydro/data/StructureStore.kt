package com.example.smarthydro.data

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.local.CachedStructure
import com.example.smarthydro.data.local.LocalDatabase
import com.example.smarthydro.models.CreateStructureDto
import com.example.smarthydro.models.StructureDto
import com.example.smarthydro.models.UpdateStructureDto
import com.example.smarthydro.models.StructureItem
import com.example.smarthydro.models.StructureType
import com.example.smarthydro.screens.extractAuthErrorMessage
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

private const val FodderApiType = "FODDER_TRAY"
private const val HydroponicApiType = "VERTICAL_TOWER"

enum class StructureSaveOutcome { SAVED, CONFLICT, INVALID, NOT_ALLOWED, FAILED }

data class StructureSaveResult(
    val outcome: StructureSaveOutcome,
    val message: String? = null,
    val structure: StructureItem? = null,
    val field: String? = null
)

// Structures shared by the list, readings and add screens, backed by the real Structures API.
object StructureStore {

    // Snapshot state list, so every screen showing structures updates when one changes
    val structures = mutableStateListOf<StructureItem>()

    var isLoading by mutableStateOf(false)
        private set

    var loadError by mutableStateOf<String?>(null)
        private set

    fun find(id: String): StructureItem? = structures.find { it.id == id }

    // Names are compared ignoring case and surrounding spaces.
    // excludeId lets a structure keep its own name when it is being edited.
    fun nameExists(name: String, excludeId: String? = null): Boolean =
        structures.any { it.id != excludeId && it.title.trim().equals(name.trim(), ignoreCase = true) }

    // Expects a MAC address in the standard format
    fun macExists(mac: String, excludeId: String? = null): Boolean =
        structures.any { it.id != excludeId && (it.macAddress == mac || it.cameraMac == mac) }

    init {
        loadFromCacheSync()
    }

    // Loads cached structures from Room immediately on IO dispatcher so memory is primed
    fun loadFromCacheSync() {
        try {
            kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.IO).launch {
                loadFromCache()
            }
        } catch (_: Exception) {
        }
    }

    // Loads cached structures from Room immediately so screens show data instantly
    suspend fun loadFromCache() {
        try {
            val cached = StructureCacheRules
                .visibleRows(LocalDatabase.structureDao.getAll(), ApiClient.isSignedIn())
                .map { it.toStructureItem() }
            // Replaced even when empty, so signing out doesn't leave the account's structures on screen
            if (structures != cached) {
                structures.replaceWithUnique(cached) { it.id }
            }
        } catch (_: Exception) {
        }
    }

    // Clears the previous account's structures when a different user signs in
    suspend fun clearCache() {
        structures.clear()
        try {
            deleteCloudRows()
        } catch (_: Exception) {
        }
    }

    // Removes only the cloud structures from Room, never the "local-" ones
    private suspend fun deleteCloudRows() {
        LocalDatabase.structureDao.getAll()
            .filterNot { StructureCacheRules.isLocal(it.id) }
            .forEach { LocalDatabase.structureDao.deleteById(it.id) }
    }

    // Fetches the structures the signed-in user can see.
    // Cache-first: immediately shows local Room cache, then fetches from the API in the background.
    // Only updates Room and the reactive list if changes are detected, avoiding unnecessary re-renders.
    suspend fun refresh() {
        loadError = null

        // Offline mode: load from Room cache only
        if (!ApiClient.isSignedIn()) {
            loadFromCache()
            return
        }

        // Online mode: load from cache first so UI displays immediately
        loadFromCache()

        // Only show spinner if there is genuinely no cached data yet
        if (structures.isEmpty()) {
            isLoading = true
        }

        try {
            val response = ApiClient.service.getStructures()
            val body = response.body()
            if (response.isSuccessful && body != null) {
                // Keeps the box IP and Bluetooth address the phone saved for each structure
                val apiItems = body.map { it.toStructureItem() }
                val savedRows = LocalDatabase.structureDao.getAll()
                val freshRows = StructureCacheRules.keepPairing(apiItems.map { it.toCachedStructure() }, savedRows)
                val pairingById = freshRows.associateBy { it.id }
                val freshItems = apiItems.map { item ->
                    val row = pairingById[item.id]
                    if (row == null) item
                    else item.copy(boxIpAddress = row.boxIpAddress, boxBluetoothAddress = row.boxBluetoothAddress)
                }
                // Diff check: only write to Room and update list if the data actually changed
                if (structures != freshItems) {
                    structures.replaceWithUnique(freshItems) { it.id }
                    // Only the cloud rows are replaced, offline "local-" structures stay on the phone
                    deleteCloudRows()
                    LocalDatabase.structureDao.insertAll(freshRows)
                }
            } else if (structures.isEmpty()) {
                loadError = "Couldn't load structures."
            }
        } catch (e: Exception) {
            // If network fails but we had cached items, keep showing the cached items silently
            if (structures.isEmpty()) {
                loadError = "Couldn't reach the server. Check your connection."
            }
        } finally {
            isLoading = false
        }
    }

    suspend fun add(
        title: String,
        macAddress: String,
        structureType: StructureType,
        themeColor: Color,
        siteId: String?,
        cameraMac: String? = null
    ): StructureSaveResult {
        if (!ApiClient.isSignedIn()) {
            val structure = StructureItem(
                id = "local-${UUID.randomUUID()}",
                title = title,
                iconType = StructureLabelColours.iconTypeFor(themeColor),
                themeColor = themeColor,
                structureType = structureType,
                macAddress = macAddress,
                createdOn = todayFormatted(),
                siteId = siteId ?: ""
            )
            LocalDatabase.structureDao.insert(structure.toCachedStructure())
            structures.add(structure)
            return StructureSaveResult(StructureSaveOutcome.SAVED, structure = structure)
        }

        val dto = CreateStructureDto(
            name = title,
            structureType = toApiType(structureType),
            labelColour = StructureLabelColours.toApi(themeColor),
            siteId = siteId,
            controllerMac = macAddress,
            cameraMac = cameraMac
        )

        return try {
            val response = ApiClient.service.createStructure(dto)
            val created = response.body()
            if (response.isSuccessful && created != null) {
                val structure = created.toStructureItem()
                structures.add(structure)
                LocalDatabase.structureDao.insert(structure.toCachedStructure())
                StructureSaveResult(StructureSaveOutcome.SAVED, structure = structure)
            } else {
                failureFor(response.code(), response.errorBody()?.string())
            }
        } catch (e: Exception) {
            StructureSaveResult(StructureSaveOutcome.FAILED, "Couldn't reach the server. Check your connection.")
        }
    }

    // Returns the updated structure, or a failure outcome when the id is invalid or the save fails
    suspend fun update(
        id: String,
        title: String,
        macAddress: String,
        structureType: StructureType,
        themeColor: Color,
        siteId: String?,
        cameraMac: String? = null
    ): StructureSaveResult {
        if (!ApiClient.isSignedIn()) {
            val index = structures.indexOfFirst { it.id == id }
            if (index < 0) return StructureSaveResult(StructureSaveOutcome.FAILED, "That structure could not be found.")

            val updated = structures[index].copy(
                title = title,
                macAddress = macAddress,
                structureType = structureType,
                themeColor = themeColor,
                siteId = siteId ?: structures[index].siteId,
                iconType = StructureLabelColours.iconTypeFor(themeColor),
                // A new MAC means a different box, so the old Bluetooth address is forgotten
                boxBluetoothAddress = StructureCacheRules.bluetoothAfterEdit(
                    structures[index].macAddress, macAddress, structures[index].boxBluetoothAddress
                )
            )
            LocalDatabase.structureDao.insert(updated.toCachedStructure())
            structures[index] = updated
            return StructureSaveResult(StructureSaveOutcome.SAVED, structure = updated)
        }

        val structureId = id.toIntOrNull()
            ?: return StructureSaveResult(StructureSaveOutcome.FAILED, "That structure could not be found.")

        val dto = UpdateStructureDto(
            name = title,
            labelColour = StructureLabelColours.toApi(themeColor),
            siteId = siteId,
            controllerMac = macAddress,
            cameraMac = cameraMac ?: ""
        )

        return try {
            val response = ApiClient.service.updateStructure(structureId, dto)
            if (response.isSuccessful) {
                val existing = find(id)
                val updated = existing?.copy(
                    title = title,
                    macAddress = macAddress,
                    structureType = structureType,
                    themeColor = themeColor,
                    siteId = siteId ?: existing.siteId,
                    iconType = iconFor(structureType),
                    cameraMac = cameraMac,
                    boxBluetoothAddress = StructureCacheRules.bluetoothAfterEdit(
                        existing.macAddress, macAddress, existing.boxBluetoothAddress
                    )
                )
                if (updated != null) {
                    val index = structures.indexOfFirst { it.id == id }
                    if (index >= 0) structures[index] = updated
                    LocalDatabase.structureDao.insert(updated.toCachedStructure())
                }
                StructureSaveResult(StructureSaveOutcome.SAVED, structure = updated)
            } else {
                failureFor(response.code(), response.errorBody()?.string())
            }
        } catch (e: Exception) {
            StructureSaveResult(StructureSaveOutcome.FAILED, "Couldn't reach the server. Check your connection.")
        }
    }

    suspend fun remove(id: String): Boolean {
        if (!ApiClient.isSignedIn()) {
            LocalDatabase.structureDao.deleteById(id)
            return structures.removeAll { it.id == id }
        }

        val structureId = id.toIntOrNull() ?: return false

        return try {
            val response = ApiClient.service.deleteStructure(structureId)
            if (response.isSuccessful) {
                structures.removeAll { it.id == id }
                LocalDatabase.structureDao.deleteById(id)
                true
            } else {
                false
            }
        } catch (e: Exception) {
            false
        }
    }

    // A conflict names the field that clashed, so the form can show the error beside it
    private fun failureFor(code: Int, errorBody: String? = null): StructureSaveResult = when (code) {
        409 -> StructureSaveResult(
            StructureSaveOutcome.CONFLICT,
            extractAuthErrorMessage(errorBody) ?: "This name or MAC address is already linked to another structure.",
            field = conflictField(errorBody)
        )
        400 -> StructureSaveResult(StructureSaveOutcome.INVALID, "Please check the structure details and try again.")
        403, 404 -> StructureSaveResult(StructureSaveOutcome.NOT_ALLOWED, "You don't have permission to manage this site.")
        else -> StructureSaveResult(StructureSaveOutcome.FAILED, "Something went wrong. Please try again.")
    }

    private fun conflictField(errorBody: String?): String? {
        if (errorBody.isNullOrBlank()) return null
        return try {
            com.google.gson.JsonParser.parseString(errorBody).asJsonObject.get("field")?.asString
        } catch (e: Exception) {
            null
        }
    }

    private fun toApiType(type: StructureType): String =
        if (type == StructureType.FODDER) FodderApiType else HydroponicApiType

    private fun iconFor(type: StructureType): String =
        if (type == StructureType.FODDER) "carrot" else "flower"

    // Maps one structure record onto the shape the screens display
    private fun StructureDto.toStructureItem(): StructureItem {
        val resolvedType = if (structureType.equals(FodderApiType, ignoreCase = true)) StructureType.FODDER else StructureType.HYDROPONIC
        val color = StructureLabelColours.fromApi(labelColour)
        return StructureItem(
            id = id.toString(),
            title = name,
            iconType = StructureLabelColours.iconTypeFor(color),
            themeColor = color,
            structureType = resolvedType,
            macAddress = controllerMac,
            createdOn = formatCreatedOn(createdAt),
            siteId = siteId ?: "",
            cameraMac = cameraMac
        )
    }

    // A farmer's structure remembers the box's local IP once they've paired with it, so the
    // readings screen can keep talking to it without asking again every visit.
    suspend fun setBoxIpAddress(id: String, boxIpAddress: String) {
        val index = structures.indexOfFirst { it.id == id }
        if (index < 0) return

        val updated = structures[index].copy(boxIpAddress = boxIpAddress)
        structures[index] = updated
        // Saved signed in or not, so it survives a restart and the next refresh from the cloud
        LocalDatabase.structureDao.insert(updated.toCachedStructure())
    }

    // A farmer's structure remembers the box's Bluetooth address once they've paired with it, so the
    // box can be connected again after the app is restarted. A null address forgets it
    suspend fun setBoxBluetoothAddress(id: String, boxBluetoothAddress: String?) {
        val index = structures.indexOfFirst { it.id == id }
        if (index < 0) return

        val updated = structures[index].copy(boxBluetoothAddress = boxBluetoothAddress)
        structures[index] = updated
        // Saved signed in or not, so it survives a restart and the next refresh from the cloud
        LocalDatabase.structureDao.insert(updated.toCachedStructure())
    }

    // Maps a locally paired structure onto the row stored in the on-device cache
    private fun StructureItem.toCachedStructure(): CachedStructure = CachedStructure(
        id = id,
        title = title,
        iconType = iconType,
        themeColorArgb = themeColor.toArgb(),
        structureType = structureType.name,
        macAddress = macAddress,
        createdOn = createdOn,
        siteId = siteId,
        boxIpAddress = boxIpAddress,
        boxBluetoothAddress = boxBluetoothAddress
    )

    private fun CachedStructure.toStructureItem(): StructureItem = StructureItem(
        id = id,
        title = title,
        iconType = iconType,
        themeColor = Color(themeColorArgb),
        structureType = StructureType.valueOf(structureType),
        macAddress = macAddress,
        createdOn = createdOn,
        siteId = siteId,
        boxIpAddress = boxIpAddress,
        boxBluetoothAddress = boxBluetoothAddress
    )

    private fun todayFormatted(): String =
        LocalDate.now().format(DateTimeFormatter.ofPattern("d MMMM yyyy", Locale.ENGLISH))

    // Formats an ISO timestamp for display, falling back to the raw value if it can't be parsed
    private fun formatCreatedOn(createdAt: String): String {
        return try {
            DateTimeFormatter.ofPattern("d MMMM yyyy", Locale.ENGLISH)
                .withZone(ZoneId.systemDefault())
                .format(Instant.parse(createdAt))
        } catch (e: Exception) {
            createdAt
        }
    }
}