package com.example.smarthydro.models

import androidx.compose.ui.graphics.Color

data class SensorDetailData(
    val sensorId: String = "",
    val title: String = "",
    val toggleLabel: String = "",
    val toggleSubtext: String = "Off · tap to switch",
    val toggleOn: Boolean = false,
    val isMultiMetric: Boolean = false,
    val isDosingControl: Boolean = false,
    val hasMinusButton: Boolean = false,
    val hasPlusButton: Boolean = false,
    val doseActionLabel: String? = null,
    val currentValue: String = "",
    val hasReading: Boolean = true,
    val unit: String = "",
    val gaugeSublabel: String = "",
    val gaugeProgress: Float? = 0.7f,
    val gaugeBandStart: Float? = null,
    val gaugeBandEnd: Float? = null,
    val statusBadge: String? = "Good",
    val statusBadgeType: BadgeType = BadgeType.GOOD,
    val optimalRangeText: String? = null,
    val accentColor: Color = Color.Unspecified,
    val scheduleBadge: String? = null,
    val lastWateredBadge: String? = null,
    val multiMetricTiles: List<MetricTileData> = emptyList(),
    val showTrend: Boolean = true,
    val recentActivities: List<ActivityItem> = emptyList()
)
