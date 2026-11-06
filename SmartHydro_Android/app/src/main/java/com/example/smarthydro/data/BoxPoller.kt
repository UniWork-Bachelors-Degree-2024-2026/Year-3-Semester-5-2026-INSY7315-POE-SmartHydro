package com.example.smarthydro.data

import com.example.smarthydro.api.BoxApiClient
import com.example.smarthydro.models.BoxTelemetryDto
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlin.coroutines.cancellation.CancellationException

// Reads a directly paired box over and over, for as long as the screen that started it is open
object BoxPoller {

    // Every reading is kept on the phone for the charts, and onReading gets the newest one, or null
    // after two missed reads in a row so a stale reading is never shown as live
    suspend fun run(
        boxIpAddress: String,
        deviceMac: String,
        onReading: suspend (BoxTelemetryDto?) -> Unit
    ) {
        var failedReads = 0
        var hasHadReading = false

        while (currentCoroutineContext().isActive) {
            try {
                val response = BoxApiClient.serviceFor(boxIpAddress).getLiveTelemetry()
                // Faulty sensors come back as stand-in numbers
                val body = response.body()?.let { HistoryLogic.withoutFaults(it) }

                if (response.isSuccessful && body != null) {
                    failedReads = 0
                    hasHadReading = true
                    TelemetryHistoryStore.recordBoxReading(deviceMac, body)
                    onReading(body)
                } else {
                    failedReads++
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // Not currently on the box's network
                failedReads++
            }

            // With nothing on screen yet the first miss already settles it, otherwise one miss keeps the last reading
            if (failedReads >= 2 || (failedReads == 1 && !hasHadReading)) onReading(null)

            delay(HistoryLogic.BOX_POLL_INTERVAL_MS)
        }
    }
}