package com.example.smarthydro

import com.example.smarthydro.data.BoxControl
import com.example.smarthydro.data.BoxTelemetryMapper
import com.example.smarthydro.data.SensorDetailMapper
import com.example.smarthydro.models.BoxControlRequestDto
import com.example.smarthydro.models.BoxControlResponseDto
import com.example.smarthydro.models.BoxDoseRequestDto
import com.example.smarthydro.models.BoxRelaysDto
import com.example.smarthydro.models.BoxTelemetryDto
import com.google.gson.GsonBuilder
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks switching and dosing straight on a box over its own hotspot: which relay each page
// switches, how the box's reply shows on the cards, and what the person is told when it fails
class BoxControlTest {

    private val gson = GsonBuilder().create()

    // Mock data: the live reply of a box whose software has local control, with the water pump and light on
    private val liveReplyWithRelays = """{"ph":6.2,"ec":1.4,"temperature":24.0,"humidity":60.0,"light":300.0,
        "waterLevelRaw":2650,"waterLevelDetected":true,"mlPh":0,"mlEc":0,"mlTemp":0,"mlHumidity":0,
        "relays":{"growLight":true,"circulationFan":false,"mainWaterPump":true,"extractorFan":false,
        "phUpPump":false,"phDownPump":false,"nutrientUpPump":false,"dilutionDownPump":false}}"""

    // Mock data: the live reply of an older box, which has no relays at all
    private val liveReplyWithoutRelays = """{"ph":6.2,"ec":1.4,"temperature":24.0,"humidity":60.0,"light":300.0,
        "waterLevelRaw":2650,"waterLevelDetected":true,"mlPh":0,"mlEc":0,"mlTemp":0,"mlHumidity":0}"""

    private val controlAccepted = """{"ok":true,"relays":{"growLight":false,"mainWaterPump":true}}"""
    private val controlRefused = """{"ok":false,"message":"That relay is held for the dosing lockout."}"""

    private fun box(relays: BoxRelaysDto?) = BoxTelemetryDto(
        ph = 6.2, ec = 1.4, temperature = 24.0, humidity = 60.0, light = 300.0,
        waterLevelRaw = 2650, waterLevelDetected = true, mlPh = 0, mlEc = 0, mlTemp = 0, mlHumidity = 0,
        relays = relays
    )

    // ---- the relays after a switch ----

    @Test
    fun switchedRelayChangesAndTheRestStayAsTheyWere() {
        val before = BoxRelaysDto(growLight = false, mainWaterPump = true, circulationFan = false)
        val after = BoxControl.withRelay(before, "growLight", true)

        assertEquals(true, after.growLight)
        assertEquals(true, after.mainWaterPump)
        assertEquals(false, after.circulationFan)
    }

    @Test
    fun everyRelayTheAppSwitchesCanBeSetOnAndOff() {
        listOf("sunlight", "water", "temperature", "humidity", "nutrients", "clean_water").forEach { sensorId ->
            val actuator = BoxControl.actuatorFor(sensorId)!!

            assertTrue(actuator, BoxControl.isOn(sensorId, BoxControl.withRelay(BoxRelaysDto(), actuator, true)))
            assertFalse(actuator, BoxControl.isOn(sensorId, BoxControl.withRelay(BoxRelaysDto(), actuator, false)))
        }
    }

    @Test
    fun switchingWhenTheBoxReportedNoRelaysStartsFromNone() {
        val after = BoxControl.withRelay(null, "extractorFan", true)

        assertEquals(true, after.extractorFan)
        assertNull(after.growLight)
    }

    @Test
    fun unknownRelayLeavesTheRelaysAsTheyWere() {
        val before = BoxRelaysDto(growLight = true)

        assertEquals(before, BoxControl.withRelay(before, "sprinkler", true))
    }

    // ---- which relay each page switches ----

    @Test
    fun eachSensorPageSwitchesItsOwnRelay() {
        assertEquals("mainWaterPump", BoxControl.actuatorFor("water"))
        assertEquals("phUpPump", BoxControl.actuatorFor("clean_water"))
        assertEquals("circulationFan", BoxControl.actuatorFor("temperature"))
        assertEquals("growLight", BoxControl.actuatorFor("sunlight"))
        assertEquals("nutrientUpPump", BoxControl.actuatorFor("nutrients"))
        assertEquals("extractorFan", BoxControl.actuatorFor("humidity"))
    }

    @Test
    fun aPageWithNoSwitchHasNoRelay() {
        assertNull(BoxControl.actuatorFor("spray_system"))
        assertNull(BoxControl.actuatorFor("moisture"))
    }

    // ---- how the box's relays show on the switches ----

    @Test
    fun aSwitchIsOnWhenTheBoxReportsItsRelayOn() {
        val relays = gson.fromJson(liveReplyWithRelays, BoxTelemetryDto::class.java).relays

        assertTrue(BoxControl.isOn("water", relays))
        assertTrue(BoxControl.isOn("sunlight", relays))
        assertFalse(BoxControl.isOn("temperature", relays))
        assertFalse(BoxControl.isOn("humidity", relays))
    }

    @Test
    fun thePhSwitchIsOnWhenEitherPhPumpIsOn() {
        assertTrue(BoxControl.isOn("clean_water", BoxRelaysDto(phDownPump = true)))
        assertTrue(BoxControl.isOn("clean_water", BoxRelaysDto(phUpPump = true)))
        assertFalse(BoxControl.isOn("clean_water", BoxRelaysDto()))
    }

    @Test
    fun theNutrientSwitchIsOnWhenEitherNutrientPumpIsOn() {
        assertTrue(BoxControl.isOn("nutrients", BoxRelaysDto(dilutionDownPump = true)))
        assertTrue(BoxControl.isOn("nutrients", BoxRelaysDto(nutrientUpPump = true)))
        assertFalse(BoxControl.isOn("nutrients", BoxRelaysDto()))
    }

    @Test
    fun anOlderBoxWithNoRelaysShowsEverySwitchOff() {
        val relays = gson.fromJson(liveReplyWithoutRelays, BoxTelemetryDto::class.java).relays

        assertNull(relays)
        listOf("water", "clean_water", "temperature", "sunlight", "nutrients", "humidity")
            .forEach { assertFalse(it, BoxControl.isOn(it, relays)) }
    }

    @Test
    fun theCardsShowTheBoxsRelays() {
        val nodes = BoxTelemetryMapper.sensorNodes(box(BoxRelaysDto(mainWaterPump = true, growLight = true)))

        assertTrue(nodes.first { it.id == "water" }.controlOn)
        assertTrue(nodes.first { it.id == "sunlight" }.controlOn)
        assertFalse(nodes.first { it.id == "humidity" }.controlOn)
    }

    @Test
    fun theDetailPageShowsTheBoxsRelay() {
        val withLight = SensorDetailMapper.buildFromBox("sunlight", box(BoxRelaysDto(growLight = true)))!!
        val withoutLight = SensorDetailMapper.buildFromBox("sunlight", box(BoxRelaysDto(growLight = false)))!!

        assertTrue(withLight.toggleOn)
        assertFalse(withoutLight.toggleOn)
    }

    @Test
    fun cardsForABoxThatHasNotAnsweredHaveEverySwitchOff() {
        assertTrue(BoxTelemetryMapper.sensorNodes(null).none { it.controlOn })
    }

    // ---- what the app sends ----

    @Test
    fun aSwitchRequestCarriesTheRelayAndTheState() {
        val json = gson.toJson(BoxControlRequestDto(actuator = "mainWaterPump", state = true))

        assertTrue(json, json.contains("\"actuator\":\"mainWaterPump\""))
        assertTrue(json, json.contains("\"state\":true"))
    }

    @Test
    fun aDoseRequestCarriesTheTypeAndTheVolume() {
        val json = gson.toJson(BoxDoseRequestDto(type = "PH_UP", volumeMl = 10.0))

        assertTrue(json, json.contains("\"type\":\"PH_UP\""))
        assertTrue(json, json.contains("\"volumeMl\":10.0"))
    }

    // ---- what the box answers ----

    @Test
    fun anAcceptedSwitchReportsTheRelaysAfterwards() {
        val reply = gson.fromJson(controlAccepted, BoxControlResponseDto::class.java)

        assertEquals(true, reply.ok)
        assertEquals(true, reply.relays!!.mainWaterPump)
        assertEquals(false, reply.relays!!.growLight)
    }

    // ---- what the person is told ----

    @Test
    fun aRefusalUsesTheWordsTheBoxGave() {
        assertEquals("That relay is held for the dosing lockout.", BoxControl.refusalMessage(409, controlRefused))
    }

    @Test
    fun aBoxWithoutTheRouteIsToldToUpdate() {
        val message = BoxControl.refusalMessage(404, null)

        assertTrue(message, message.contains("update"))
    }

    @Test
    fun anyOtherRefusalIsAskedToTryAgain() {
        assertEquals("The box didn't accept that. Please try again.", BoxControl.refusalMessage(500, "not json"))
    }

    @Test
    fun theOfflineMessageSaysTheBoxIsOffline() {
        assertTrue(BoxControl.OFFLINE_MESSAGE, BoxControl.OFFLINE_MESSAGE.contains("offline"))
    }
}
