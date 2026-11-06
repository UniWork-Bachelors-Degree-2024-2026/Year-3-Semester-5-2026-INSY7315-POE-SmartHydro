package com.example.smarthydro.models

import androidx.compose.ui.graphics.Color

data class SensorNode(
    val id: String,
    val title: String,
    val valueDisplay: String,
    val statusBadgeText: String? = null,
    val statusBadgeType: BadgeType = BadgeType.GOOD,
    val controlLabel: String,
    val controlTag: String,
    val controlOn: Boolean,
    val infoText: String,
    val accentColor: Color,
    val iconType: String,
    val hasReading: Boolean = true
)
