package com.example.smarthydro

import com.example.smarthydro.data.SensorDetailMapper
import com.example.smarthydro.models.ActuatorHistoryDto
import com.example.smarthydro.models.PagedResultDto
import com.example.smarthydro.models.TelemetrySnapshotDto
import com.google.gson.GsonBuilder
import com.google.gson.reflect.TypeToken
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

// The actuator history the API sends for a structure, and how the detail pages list it
class ActuatorHistoryJsonTest {

    private val gson = GsonBuilder().create()
    private val type = object : TypeToken<PagedResultDto<ActuatorHistoryDto>>() {}.type

    // Mock data: a reply copied from the API (the field with the time is called "changedAt")
    private val pumpHistoryReply = """
        {"items":[
          {"id":60,"deviceMac":"AA:BB:CC:DD:EE:FF","actuatorName":"main_water_pump","newState":false,"changedAt":"2026-10-02T16:16:53.075216Z","changedBy":"operator@example.com","device":null},
          {"id":59,"deviceMac":"AA:BB:CC:DD:EE:FF","actuatorName":"main_water_pump","newState":true,"changedAt":"2026-10-02T16:16:49.946223Z","changedBy":"operator@example.com","device":null}
        ],"totalCount":2,"page":1,"pageSize":20,"totalPages":1,"hasPreviousPage":false,"hasNextPage":false}
    """.trimIndent()

    // Mock data: one entry with no time, one with a time that cannot be read, and one good one
    private val historyWithBadTimes = """{"items":[
        {"id":1,"deviceMac":"A","actuatorName":"main_water_pump","newState":true},
        {"id":2,"deviceMac":"A","actuatorName":"main_water_pump","newState":false,"changedAt":"not a date"},
        {"id":3,"deviceMac":"A","actuatorName":"main_water_pump","newState":true,"changedAt":"2026-10-02T16:16:49Z"}
    ]}"""

    // Mock data: nine pump entries, one second apart
    private val nineEntries = """{"items":[""" + (1..9).joinToString(",") {
        """{"id":$it,"deviceMac":"A","actuatorName":"main_water_pump","newState":true,"changedAt":"2026-10-02T16:16:0$it.000Z"}"""
    } + "]}"

    private fun parse(json: String): List<ActuatorHistoryDto> =
        gson.fromJson<PagedResultDto<ActuatorHistoryDto>>(json, type).items

    private fun snapshot() = TelemetrySnapshotDto(
        phLevel = 6.2, ecMs = 1.4, lightLux = 300.0, waterLevelRaw = 2650, waterLevelDetected = true,
        temperatureC = 24.0, humidityPercent = 60.0, recordedAt = "2026-10-02T16:00:00Z"
    )

    @Test
    fun theReplyIsReadWithItsTimes() {
        val items = parse(pumpHistoryReply)

        assertEquals(2, items.size)
        assertNotNull(items[0].timestamp)
        assertEquals("2026-10-02T16:16:53.075216Z", items[0].timestamp)
        assertEquals("main_water_pump", items[0].actuatorName)
    }

    @Test
    fun theWaterPageListsThePumpActivityNewestFirst() {
        val items = parse(pumpHistoryReply)

        val data = SensorDetailMapper.build("water", snapshot(), null, items, emptyMap())!!

        assertEquals(2, data.recentActivities.size)
        assertEquals("Water pump turned OFF", data.recentActivities[0].description)
        assertEquals("Water pump turned ON", data.recentActivities[1].description)
        assertTrue(data.recentActivities.all { it.time.isNotBlank() })
    }

    @Test
    fun otherSensorsDoNotShowThePumpActivity() {
        val items = parse(pumpHistoryReply)

        val data = SensorDetailMapper.build("temperature", snapshot(), null, items, emptyMap())!!

        assertTrue(data.recentActivities.isEmpty())
    }

    @Test
    fun anEntryWithNoReadableTimeIsLeftOut() {
        val items = parse(historyWithBadTimes)

        val data = SensorDetailMapper.build("water", snapshot(), null, items, emptyMap())!!

        assertEquals(1, data.recentActivities.size)
        assertEquals("Water pump turned ON", data.recentActivities[0].description)
    }

    @Test
    fun onlyTheFiveNewestAreShown() {
        val items = parse(nineEntries)

        val data = SensorDetailMapper.build("water", snapshot(), null, items, emptyMap())!!

        assertEquals(5, data.recentActivities.size)
    }
}
