package com.example.smarthydro

import com.example.smarthydro.models.AccountActionResponseDto
import com.example.smarthydro.models.CameraPhotoMetadataDto
import com.example.smarthydro.models.PagedResultDto
import com.example.smarthydro.models.StructureThresholdsDto
import com.example.smarthydro.models.TelemetryRealtimeDto
import com.example.smarthydro.models.UpdateThresholdDto
import com.google.gson.GsonBuilder
import com.google.gson.reflect.TypeToken
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks the app reads the API's replies and sends its requests with the API's own field names.
// The sample replies below were copied from the API's code, so update them if a reply changes
class ApiContractTest {

    private val gson = GsonBuilder().create()

    // Mock data: replies as the API sends them
    private val latestTelemetryReply = """{"id":5,"deviceMac":"AA:BB:CC:DD:EE:FF","phLevel":6.2,"ecMs":1.4,"lightLux":300.0,
        "waterLevelRaw":2650,"waterLevelDetected":true,"temperatureC":24.5,"humidityPercent":61.0,"mlPhPrediction":0,
        "mlEcPrediction":0,"mlTempPrediction":0,"mlHumidityPrediction":0,"recordedAt":"2026-10-02T16:00:00Z","device":null}"""

    private val latestTelemetryWithNullPh = """{"id":5,"deviceMac":"A","phLevel":null,"ecMs":1.4,"lightLux":0,"temperatureC":24.0,
        "humidityPercent":60.0,"mlPhPrediction":0,"mlEcPrediction":0,"mlTempPrediction":0,"mlHumidityPrediction":0,"recordedAt":"x"}"""

    private val structureThresholdsReply = """{"structureId":12,"structureName":"Big Structure - 1","structureType":"VERTICAL_TOWER","thresholds":[
        {"id":0,"structureId":12,"structureType":"VERTICAL_TOWER","sensorType":"PH","minValue":5.5,"maxValue":6.5,"isDefault":true},
        {"id":7,"structureId":12,"structureType":"VERTICAL_TOWER","sensorType":"EC","minValue":1.0,"maxValue":2.5,"isDefault":false}]}"""

    private val siteOwnerAssignedReply = """{"message":"Site owner updated."}"""

    private val galleryPageReply = """{"items":[{"id":31,"deviceMac":"AA:BB:CC:DD:EE:01","structureId":12,"plant":"Lettuce","label":"Row 2",
        "cameraLabel":"Cam 1","isFavourite":true,"capturedAt":"2026-10-02T09:30:00Z","imageSizeBytes":48210}],
        "totalCount":1,"page":1,"pageSize":12,"totalPages":1}"""

    @Test
    fun latestTelemetryIsReadWithTheApisFieldNames() {
        val dto = gson.fromJson(latestTelemetryReply, TelemetryRealtimeDto::class.java)

        assertEquals(6.2, dto.phLevel!!, 0.0)
        assertEquals(24.5, dto.temperatureC!!, 0.0)
        assertEquals(61.0, dto.humidityPercent!!, 0.0)
        assertEquals(true, dto.waterLevelDetected)
        assertEquals("2026-10-02T16:00:00Z", dto.recordedAt)
    }

    @Test
    fun aSensorTheApiSendsAsNullIsNoReading() {
        val dto = gson.fromJson(latestTelemetryWithNullPh, TelemetryRealtimeDto::class.java)

        assertNull(dto.phLevel)
        assertEquals(0.0, dto.lightLux!!, 0.0)
    }

    @Test
    fun editingARangeSendsTheStructureIdTheApiRequires() {
        val request = UpdateThresholdDto(structureId = 12, sensorType = "PH", minValue = 5.5, maxValue = 6.5)

        val json = gson.toJson(request)

        assertTrue(json, json.contains("\"structureId\":12"))
        assertTrue(json, json.contains("\"sensorType\":\"PH\""))
        assertFalse("the old structureType field is gone", json.contains("structureType"))
    }

    @Test
    fun theRangesOfAStructureAreReadWithTheirDefaults() {
        val dto = gson.fromJson(structureThresholdsReply, StructureThresholdsDto::class.java)

        assertEquals(2, dto.thresholds.size)
        assertEquals(5.5, dto.thresholds.first { it.sensorType == "PH" }.minValue, 0.0)
    }

    @Test
    fun assigningASiteOwnerIsReadWithoutAnId() {
        val dto = gson.fromJson(siteOwnerAssignedReply, AccountActionResponseDto::class.java)

        assertEquals("Site owner updated.", dto.message)
        assertNull(dto.id)
    }

    @Test
    fun theGalleryItemsAreReadWithTheFieldsTheApiBuilds() {
        val type = object : TypeToken<PagedResultDto<CameraPhotoMetadataDto>>() {}.type

        val page = gson.fromJson<PagedResultDto<CameraPhotoMetadataDto>>(galleryPageReply, type)
        val photo = page.items.first()

        assertEquals(31L, photo.id)
        assertEquals("Lettuce", photo.plant)
        assertEquals(true, photo.isFavourite)
        assertEquals(48210, photo.imageSizeBytes)
        assertNotNull(photo.capturedAt)
    }
}
