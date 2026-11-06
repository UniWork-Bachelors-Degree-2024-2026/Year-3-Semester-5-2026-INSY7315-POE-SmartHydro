package com.example.smarthydro.data

import com.example.smarthydro.api.BoxApiClient
import com.example.smarthydro.models.BoxControlRequestDto
import com.example.smarthydro.models.BoxControlResponseDto
import com.example.smarthydro.models.BoxDoseRequestDto
import com.example.smarthydro.models.BoxDoseResponseDto
import com.example.smarthydro.models.BoxRelaysDto
import com.google.gson.GsonBuilder
import java.io.IOException
import kotlin.coroutines.cancellation.CancellationException

// What came of asking a box to do something over its own hotspot
sealed class BoxControlResult {

    // The box did it. relays is how it reports its relays afterwards, when it reports them at all
    data class Done(val relays: BoxRelaysDto? = null, val durationMs: Int? = null) : BoxControlResult()

    // The box could not be reached, so nothing was sent
    object Offline : BoxControlResult()

    // The box answered but did not do it
    data class Refused(val message: String) : BoxControlResult()
}

// Switches relays and runs doses on a box directly, through the hotspot the box creates itself,
// so it works with no internet and no sign-in
object BoxControl {

    // What the person is told when the box cannot be reached
    const val OFFLINE_MESSAGE = "The box is offline right now, so this can't be done."

    // What the person is told while the app has not yet found the box
    const val SEARCHING_MESSAGE = "Still looking for the box. Try again in a moment."

    private val gson = GsonBuilder().create()

    // The relay the box knows for the switch of a sensor page, or null when the page has no switch.
    // The names match the ones the cloud uses, so a switch does the same thing either way
    fun actuatorFor(sensorId: String): String? = when (sensorId) {
        "water" -> "mainWaterPump"
        "clean_water" -> "phUpPump"
        "temperature" -> "circulationFan"
        "sunlight" -> "growLight"
        "nutrients" -> "nutrientUpPump"
        "humidity" -> "extractorFan"
        else -> null
    }

    // Whether the switch of a sensor page is on, going by the relays the box reported
    fun isOn(sensorId: String, relays: BoxRelaysDto?): Boolean = when (sensorId) {
        "water" -> relays?.mainWaterPump == true
        "clean_water" -> relays?.phUpPump == true || relays?.phDownPump == true
        "temperature" -> relays?.circulationFan == true
        "sunlight" -> relays?.growLight == true
        "nutrients" -> relays?.nutrientUpPump == true || relays?.dilutionDownPump == true
        "humidity" -> relays?.extractorFan == true
        else -> false
    }

    // The relays once one has been switched, for a link whose answer does not say what is on afterwards. The next
    // reading from the box replaces this with what is really on
    fun withRelay(relays: BoxRelaysDto?, actuator: String, on: Boolean): BoxRelaysDto {
        val current = relays ?: BoxRelaysDto()
        return when (actuator) {
            "growLight" -> current.copy(growLight = on)
            "circulationFan" -> current.copy(circulationFan = on)
            "mainWaterPump" -> current.copy(mainWaterPump = on)
            "extractorFan" -> current.copy(extractorFan = on)
            "phUpPump" -> current.copy(phUpPump = on)
            "phDownPump" -> current.copy(phDownPump = on)
            "nutrientUpPump" -> current.copy(nutrientUpPump = on)
            "dilutionDownPump" -> current.copy(dilutionDownPump = on)
            else -> current
        }
    }

    // Words what went wrong from the box's own answer. An older box has no such route and
    // answers 404, which is worth saying plainly because the fix is a software update
    internal fun refusalMessage(code: Int, errorBody: String?): String {
        val fromBox = try {
            gson.fromJson(errorBody, BoxControlResponseDto::class.java)?.message
        } catch (e: Exception) {
            null
        }

        return when {
            !fromBox.isNullOrBlank() -> fromBox
            code == 404 -> "This box's software can't be controlled from the phone yet. It needs an update."
            else -> "The box didn't accept that. Please try again."
        }
    }

    // Switches one relay on or off
    suspend fun setRelay(boxIpAddress: String, actuator: String, on: Boolean): BoxControlResult = try {
        val response = BoxApiClient.serviceFor(boxIpAddress).setRelay(BoxControlRequestDto(actuator, on))
        val body = response.body()

        if (response.isSuccessful && body?.ok != false) {
            BoxControlResult.Done(relays = body?.relays)
        } else {
            BoxControlResult.Refused(refusalMessage(response.code(), response.errorBody()?.string()))
        }
    } catch (e: CancellationException) {
        throw e
    } catch (e: IOException) {
        BoxControlResult.Offline
    } catch (e: Exception) {
        BoxControlResult.Refused("The box sent a reply the app couldn't read.")
    }

    // Runs one dose pump for a set volume
    suspend fun dose(boxIpAddress: String, type: String, volumeMl: Double): BoxControlResult = try {
        val response = BoxApiClient.serviceFor(boxIpAddress).dose(BoxDoseRequestDto(type, volumeMl))
        val body: BoxDoseResponseDto? = response.body()

        if (response.isSuccessful && body?.ok != false) {
            BoxControlResult.Done(durationMs = body?.durationMs)
        } else {
            BoxControlResult.Refused(refusalMessage(response.code(), response.errorBody()?.string()))
        }
    } catch (e: CancellationException) {
        throw e
    } catch (e: IOException) {
        BoxControlResult.Offline
    } catch (e: Exception) {
        BoxControlResult.Refused("The box sent a reply the app couldn't read.")
    }
}
