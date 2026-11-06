package com.example.smarthydro.transport

import com.example.smarthydro.data.BoxControl
import com.example.smarthydro.data.BoxControlResult
import com.example.smarthydro.data.BoxPoller
import com.example.smarthydro.models.BoxTelemetryDto

// Reaches a box through the hotspot it creates itself. This only wraps the code the screens already
// use (BoxControl and BoxPoller), so nothing about how a box over its hotspot behaves changes
class HotspotTransport(
    private val boxIpAddress: String,
    private val deviceMac: String
) : BoxTransport {

    override val link = BoxLink.HOTSPOT

    override suspend fun setRelay(actuator: String, on: Boolean): BoxControlResult =
        BoxControl.setRelay(boxIpAddress, actuator, on)

    override suspend fun dose(type: String, volumeMl: Double): BoxControlResult =
        BoxControl.dose(boxIpAddress, type, volumeMl)

    override suspend fun readings(onReading: suspend (BoxTelemetryDto?) -> Unit) =
        BoxPoller.run(boxIpAddress, deviceMac, onReading)
}
