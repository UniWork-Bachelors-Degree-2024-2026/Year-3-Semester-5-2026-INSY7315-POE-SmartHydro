package com.example.smarthydro.data

import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.models.StructureThresholdsDto
import com.example.smarthydro.models.ThresholdResponseDto
import com.example.smarthydro.models.UpdateThresholdDto
import com.example.smarthydro.screens.extractAuthErrorMessage
import java.text.DecimalFormat
import java.text.DecimalFormatSymbols
import java.util.Locale
import kotlin.coroutines.cancellation.CancellationException
import kotlin.math.round

// What one reading's typed minimum and maximum came to
sealed class ThresholdCheck {
    object Blank : ThresholdCheck()
    data class Invalid(val message: String) : ThresholdCheck()
    data class Valid(val min: Double, val max: Double) : ThresholdCheck()
}

data class ThresholdSaveResult(val ok: Boolean, val message: String? = null, val saved: ThresholdResponseDto? = null)

data class ThresholdResetResult(val ok: Boolean, val message: String? = null, val structure: StructureThresholdsDto? = null)

// Checks, saves and resets safe ranges for the thresholds page
object ThresholdEditor {

    private val numberPattern = Regex("^[-+]?(\\d+([.,]\\d*)?|[.,]\\d+)$")
    private val shownFormat = DecimalFormat("0.##", DecimalFormatSymbols(Locale.US))
    // Must be Operator or Admin to change range
    fun canEdit(): Boolean {
        val role = ApiClient.getSignedInRole()
        return ApiClient.isSignedIn() && (role == "Operator" || role == "Admin")
    }

    fun show(value: Double): String = shownFormat.format(value)

    // A saved range in the unit the app shows
    fun shownMin(threshold: ThresholdResponseDto, sensor: ThresholdSensor): String = show(threshold.minValue * sensor.scale)

    fun shownMax(threshold: ThresholdResponseDto, sensor: ThresholdSensor): String = show(threshold.maxValue * sensor.scale)

    fun allowedText(sensor: ThresholdSensor): String = "Allowed ${show(sensor.lowest)}–${show(sensor.highest)} ${sensor.unit}"

    private fun parse(text: String): Double? {
        val clean = text.trim()
        if (!numberPattern.matches(clean)) return null
        return clean.replace(',', '.').toDoubleOrNull()
    }

    private fun roundTo2(value: Double): Double = round(value * 100.0) / 100.0

    // Checks one reading's minimum and maximum as typed
    fun check(sensor: ThresholdSensor, minText: String, maxText: String): ThresholdCheck {
        val minBlank = minText.isBlank()
        val maxBlank = maxText.isBlank()

        if (minBlank && maxBlank) return ThresholdCheck.Blank
        if (minBlank || maxBlank) return ThresholdCheck.Invalid("Enter both a minimum and a maximum.")

        val min = parse(minText)
        val max = parse(maxText)
        if (min == null || max == null) return ThresholdCheck.Invalid("Enter numbers only.")

        if (min < sensor.lowest || min > sensor.highest || max < sensor.lowest || max > sensor.highest) {
            return ThresholdCheck.Invalid("Values must be between ${show(sensor.lowest)} and ${show(sensor.highest)} ${sensor.unit}.")
        }

        if (min >= max) return ThresholdCheck.Invalid("The minimum must be lower than the maximum.")

        val apiMin = roundTo2(min / sensor.scale)
        val apiMax = roundTo2(max / sensor.scale)
        if (apiMin >= apiMax) return ThresholdCheck.Invalid("The minimum and maximum are too close together.")

        return ThresholdCheck.Valid(apiMin, apiMax)
    }

    fun isChange(check: ThresholdCheck, saved: ThresholdResponseDto?): Boolean =
        check is ThresholdCheck.Valid && (saved == null || check.min != saved.minValue || check.max != saved.maxValue)

    // Saves one reading's range on one structure
    suspend fun save(structureId: Int, sensor: ThresholdSensor, min: Double, max: Double): ThresholdSaveResult {
        return try {
            val response = ApiClient.service.upsertThreshold(
                UpdateThresholdDto(structureId = structureId, sensorType = sensor.key, minValue = min, maxValue = max)
            )
            val body = response.body()
            if (response.isSuccessful && body != null) {
                ThresholdSaveResult(ok = true, saved = body)
            } else {
                ThresholdSaveResult(ok = false, message = errorFor(response.code(), response.errorBody()?.string()))
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            ThresholdSaveResult(ok = false, message = "Couldn't reach the server. Check your connection.")
        }
    }

    // Puts a saved range back to its healthy default
    suspend fun reset(structureId: Int, thresholdId: Int): ThresholdResetResult {
        return try {
            val deleted = ApiClient.service.deleteThreshold(thresholdId)
            if (!deleted.isSuccessful && deleted.code() != 404) {
                return ThresholdResetResult(ok = false, message = errorFor(deleted.code(), deleted.errorBody()?.string()))
            }

            val response = ApiClient.service.getThresholdsByStructure(structureId)
            val body = response.body()
            if (response.isSuccessful && body != null) {
                remember(structureId, body.thresholds)
                ThresholdResetResult(ok = true, structure = body)
            } else {
                ThresholdResetResult(ok = true)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            ThresholdResetResult(ok = false, message = "Couldn't reach the server. Check your connection.")
        }
    }

    // Keeps a structure's latest ranges
    fun remember(structureId: Int, thresholds: List<ThresholdResponseDto>) {
        StructureDataCache.putThresholds(structureId, thresholds.associateBy { it.sensorType.uppercase() })
    }

    private fun errorFor(code: Int, body: String?): String = when (code) {
        400 -> extractAuthErrorMessage(body) ?: "That range wasn't accepted."
        401, 403 -> "You don't have permission to change this structure's ranges."
        404 -> "This structure couldn't be found. It may have been removed."
        else -> "Couldn't save. Please try again."
    }
}