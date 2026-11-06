package com.example.smarthydro.transport

import com.example.smarthydro.data.BoxControlResult
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.math.roundToInt

// Sends commands to a box over Bluetooth and waits for the box's answer to each one. It has no Bluetooth in it:
// write is what puts the JSON on the box's command characteristic and answers is what the box notifies back,
// so the waiting, retrying and wording can be tested without a phone
class BleCommandRunner(
    private val deviceMac: String,
    private val ids: CommandIdSource,
    private val answers: Flow<BleCommandAck>,
    private val isReady: () -> Boolean,
    private val write: suspend (String) -> Boolean,
    private val attemptTimeoutMs: Long = 3_000L,
    private val attempts: Int = 2
) {

    companion object {
        const val UNCONFIRMED_MESSAGE = "The box didn't confirm that. Check its readings before trying again."

        // How long a pump runs per mL, as the firmware sets it (MS_PER_ML in Config.h)
        private const val MS_PER_ML = 800
    }

    // Switches one relay on or off
    suspend fun setRelay(actuator: String, on: Boolean): BoxControlResult =
        send { id -> BleCommands.relay(deviceMac, id, actuator, on) }

    // Runs one dose pump. It counts as done once the box says the pump has started, the same as over the hotspot
    suspend fun dose(type: String, volumeMl: Double): BoxControlResult {
        // A dose the box would not run as asked is refused here, before an id is used
        if (BleCommands.dose(deviceMac, 1, type, volumeMl) == null) {
            BoxLog.w("cmd", "dose $type $volumeMl mL refused before sending: the box would not run that")
            return BoxControlResult.Refused(BleCommandAck.refusalMessage("pulse_limit"))
        }

        val result = send { id -> BleCommands.dose(deviceMac, id, type, volumeMl).orEmpty() }
        return if (result is BoxControlResult.Done) result.copy(durationMs = (volumeMl * MS_PER_ML).roundToInt()) else result
    }

    // Asks the box to change its connectivity mode
    suspend fun setMode(mode: BoxMode): BoxControlResult =
        send { id -> BleCommands.mode(deviceMac, id, mode) }

    // Writes a command and waits for the answer carrying its id. If no answer comes it writes the same command again
    // once: the box answers a repeated id with its earlier result instead of running the command twice
    private suspend fun send(build: (Int) -> String): BoxControlResult {
        if (!isReady()) {
            BoxLog.w("cmd", "not sent, the Bluetooth connection is not ready")
            return BoxControlResult.Offline
        }

        val commandId = ids.next()
        val json = build(commandId)
        var delivered = false
        BoxLog.i("cmd", "send cid=$commandId: ${BoxLog.clip(json)}")

        for (attempt in 1..attempts) {
            val answer = coroutineScope {
                // Listening starts before the write, so an answer that comes straight back is not missed
                val waiting = async(start = CoroutineStart.UNDISPATCHED) {
                    withTimeoutOrNull(attemptTimeoutMs) { answers.first { it.commandId == commandId } }
                }
                if (write(json)) {
                    delivered = true
                    waiting.await()
                } else {
                    waiting.cancel()
                    null
                }
            }

            if (answer != null) {
                BoxLog.i("cmd", "answer to cid=$commandId: ok=${answer.ok} reason=${answer.reason} phase=${answer.phase}")
                return answer.toResult()
            }
            if (!delivered) {
                BoxLog.w("cmd", "cid=$commandId could not be delivered to the box")
                return BoxControlResult.Offline
            }
            BoxLog.w("cmd", "no answer to cid=$commandId after attempt $attempt of $attempts")
        }
        BoxLog.w("cmd", "gave up on cid=$commandId, the box never confirmed it")
        return BoxControlResult.Refused(UNCONFIRMED_MESSAGE)
    }
}
