package com.example.smarthydro.models

import androidx.compose.ui.graphics.Color
import java.time.ZonedDateTime

// Alert needs action now, Caution is worth keeping an eye on
enum class AlertSeverity {
    ALERT,
    CAUTION
}

// One alert as the screens show it
data class AlertItem(
    val id: String,
    val title: String,
    val timeAndLocation: String,
    val accentColor: Color,
    val iconType: String,
    // Null when the alert's component has no matching sensor detail screen (see AlertMapper)
    val targetSensorId: String? = null,
    val structureId: String = "",
    val severity: AlertSeverity = AlertSeverity.CAUTION,
    val isResolved: Boolean = false,
    val value: String = "",
    val detail: String = "",
    val clearedBy: String? = null,
    // The structure the alert belongs to, used by the Location filter
    val location: String = "",
    // When it happened, in the phone's time zone. Used for sorting and the time / day text.
    val occurredAt: ZonedDateTime? = null
)