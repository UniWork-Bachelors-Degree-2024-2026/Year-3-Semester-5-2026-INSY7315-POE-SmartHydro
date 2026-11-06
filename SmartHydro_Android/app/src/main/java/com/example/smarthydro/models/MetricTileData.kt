package com.example.smarthydro.models

data class MetricTileData(
    val label: String,
    val value: String,
    val unit: String? = null,
    val badgeText: String,
    val badgeType: BadgeType = BadgeType.NEUTRAL
)