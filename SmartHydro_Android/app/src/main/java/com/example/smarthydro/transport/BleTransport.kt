package com.example.smarthydro.transport

import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.data.TelemetryHistoryStore
import com.example.smarthydro.models.BoxTelemetryDto
import com.example.smarthydro.util.MacAddressFormat
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onStart
import kotlinx.coroutines.flow.transformLatest

// Reaches a box over Bluetooth, so a relay switch, a dose and the readings work the same as over the hotspot.
// incoming is what the box notifies and record is where each reading is kept on the phone for the charts
class BleTransport(
    private val deviceMac: String,
    private val runner: BleCommandRunner,
    private val incoming: Flow<BleTelemetry>,
    private val record: suspend (String, BoxTelemetryDto) -> Unit = { mac, reading -> TelemetryHistoryStore.recordBoxReading(mac, reading) },
    private val quietAfterMs: Long = QUIET_AFTER_MS
) : BoxTransport {

    companion object {

        // A box sends a reading about every 3 seconds, so this is three or four missed in a row
        const val QUIET_AFTER_MS = 10_000L

        // Wires a transport to a connection that is already open
        fun forBox(box: BleBox, deviceMac: String, ids: CommandIdSource = CommandIdSource()): BleTransport {
            val runner = BleCommandRunner(
                deviceMac = deviceMac,
                ids = ids,
                answers = box.answers,
                isReady = { box.state.value is ConnectionState.Ready },
                write = box::writeCommand
            )
            return BleTransport(deviceMac, runner, box.readings)
        }
    }

    override val link = BoxLink.BLUETOOTH

    override suspend fun setRelay(actuator: String, on: Boolean): BoxControlResult =
        runner.setRelay(actuator, on)

    override suspend fun dose(type: String, volumeMl: Double): BoxControlResult =
        runner.dose(type, volumeMl)

    override suspend fun setMode(mode: BoxMode): BoxControlResult =
        runner.setMode(mode)

    // The mode details this box reports with every reading. Readings from any other box in range are left out
    override fun modes(): Flow<BoxModeInfo> {
        val mac = MacAddressFormat.normalise(deviceMac)

        return incoming
            .filter { it.deviceMac == mac }
            .map { BoxModeInfo(mode = it.mode, preferred = it.preferredMode, autoFallback = it.autoFallback) }
    }

    // Passes on the readings of this box, and a null once it has been quiet for a while so a stale reading is
    // never shown as live. Readings from any other box in range are left out
    @OptIn(ExperimentalCoroutinesApi::class)
    override suspend fun readings(onReading: suspend (BoxTelemetryDto?) -> Unit) {
        val mac = MacAddressFormat.normalise(deviceMac)

        incoming
            .filter { it.deviceMac == mac }
            .map<BleTelemetry, BoxTelemetryDto?> { it.reading }
            // The null that starts it only sets the quiet timer going, so a box never heard from also goes quiet
            .onStart { emit(null) }
            .transformLatest { reading ->
                if (reading != null) emit(reading)
                delay(quietAfterMs)
                emit(null)
            }
            .collect { reading ->
                if (reading != null) record(deviceMac, reading)
                onReading(reading)
            }
    }
}
