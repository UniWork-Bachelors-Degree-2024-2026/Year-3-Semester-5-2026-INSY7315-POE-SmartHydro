package com.example.smarthydro.data

import com.example.smarthydro.models.StructureDashboardSummaryDto
import com.example.smarthydro.models.ThresholdResponseDto

// Keeps a structure's latest dashboard summary and ranges in memory for a short while, so a page
// opened straight from the readings screen has something to show immediately while it refreshes
object StructureDataCache {

    const val SUMMARY_FRESH_MS = 30_000L
    const val THRESHOLDS_FRESH_MS = 5 * 60_000L

    private class Entry<T>(val value: T, val savedAtMs: Long)

    private val summaries = mutableMapOf<Int, Entry<StructureDashboardSummaryDto>>()
    private val thresholds = mutableMapOf<Int, Entry<Map<String, ThresholdResponseDto>>>()

    // A saved time that is in the future, because the clock moved back, is not trusted
    fun isFresh(savedAtMs: Long, nowMs: Long, maxAgeMs: Long): Boolean = nowMs - savedAtMs in 0..maxAgeMs

    @Synchronized
    fun summary(structureId: Int, nowMs: Long = System.currentTimeMillis()): StructureDashboardSummaryDto? =
        summaries[structureId]?.takeIf { isFresh(it.savedAtMs, nowMs, SUMMARY_FRESH_MS) }?.value

    @Synchronized
    fun putSummary(structureId: Int, value: StructureDashboardSummaryDto, nowMs: Long = System.currentTimeMillis()) {
        summaries[structureId] = Entry(value, nowMs)
    }

    @Synchronized
    fun thresholds(structureId: Int, nowMs: Long = System.currentTimeMillis()): Map<String, ThresholdResponseDto>? =
        thresholds[structureId]?.takeIf { isFresh(it.savedAtMs, nowMs, THRESHOLDS_FRESH_MS) }?.value

    @Synchronized
    fun putThresholds(structureId: Int, value: Map<String, ThresholdResponseDto>, nowMs: Long = System.currentTimeMillis()) {
        thresholds[structureId] = Entry(value, nowMs)
    }

    // Used when a different person signs in, so nothing from the last account is ever shown
    @Synchronized
    fun clear() {
        summaries.clear()
        thresholds.clear()
    }
}
