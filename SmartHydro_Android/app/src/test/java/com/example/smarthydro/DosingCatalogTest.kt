package com.example.smarthydro

import com.example.smarthydro.data.DoseOption
import com.example.smarthydro.data.DosingCatalog
import com.example.smarthydro.models.DosingCommandRequestDto
import com.example.smarthydro.transport.BleCommands
import com.google.gson.GsonBuilder
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks the doses the Android dosing buttons send match the website's, and who may send them
class DosingCatalogTest {

    // Mock data: the doses the website's catalog sends, as type and volume in mL
    private val websiteDoses = mapOf(
        "PH_DOWN" to 2.0,
        "PH_UP" to 2.0,
        "NUTRIENT_UP" to 20.0,
        "DILUTION_DOWN" to 25.0
    )

    private val operatorRole = "Operator"
    private val adminRole = "Admin"
    private val workerRole = "Worker"
    private val controllerMac = "AA:BB:CC:DD:EE:FF"

    @Test
    fun thePhPageLowersWithTheMinusButtonAndRaisesWithThePlusButton() {
        assertEquals("PH_DOWN", DosingCatalog.minusFor("clean_water")!!.type)
        assertEquals("PH_UP", DosingCatalog.plusFor("clean_water")!!.type)
    }

    @Test
    fun theNutrientsPageDilutesWithTheMinusButtonAndAddsWithThePlusButton() {
        assertEquals("DILUTION_DOWN", DosingCatalog.minusFor("nutrients")!!.type)
        assertEquals("NUTRIENT_UP", DosingCatalog.plusFor("nutrients")!!.type)
    }

    @Test
    fun everyDoseHasTheSameVolumeAsTheWebsite() {
        val doses = listOf(
            DosingCatalog.minusFor("clean_water")!!,
            DosingCatalog.plusFor("clean_water")!!,
            DosingCatalog.minusFor("nutrients")!!,
            DosingCatalog.plusFor("nutrients")!!
        )

        doses.forEach { dose -> assertEquals(dose.type, websiteDoses[dose.type]!!, dose.volumeMl, 0.0) }
        assertEquals(websiteDoses.keys, doses.map { it.type }.toSet())
    }

    @Test
    fun everyDoseFitsOneBoxCommand() {
        val doses = listOf(
            DosingCatalog.minusFor("clean_water")!!,
            DosingCatalog.plusFor("clean_water")!!,
            DosingCatalog.minusFor("nutrients")!!,
            DosingCatalog.plusFor("nutrients")!!
        )

        doses.forEach { dose ->
            assertNotNull(dose.type, BleCommands.dose(controllerMac, 1, dose.type, dose.volumeMl))
        }
    }

    @Test
    fun aPageWithoutDosingHasNoDoseButtons() {
        listOf("water", "temperature", "humidity", "sunlight").forEach { sensorId ->
            assertNull(sensorId, DosingCatalog.minusFor(sensorId))
            assertNull(sensorId, DosingCatalog.plusFor(sensorId))
        }
    }

    @Test
    fun onlyOperatorsAndAdminsMayDose() {
        assertTrue(DosingCatalog.canDose(operatorRole))
        assertTrue(DosingCatalog.canDose(adminRole))
        assertFalse(DosingCatalog.canDose(workerRole))
        assertFalse(DosingCatalog.canDose(null))
    }

    @Test
    fun theRoleCheckIgnoresCapitals() {
        assertTrue(DosingCatalog.canDose("operator"))
        assertTrue(DosingCatalog.canDose("ADMIN"))
    }

    @Test
    fun aWholeVolumeIsShownWithoutADecimalPoint() {
        assertEquals("20", DoseOption("NUTRIENT_UP", "Add nutrients", 20.0).volumeText)
        assertEquals("2.5", DoseOption("PH_UP", "Raise pH", 2.5).volumeText)
    }

    @Test
    fun theRequestIsSentWithTheFieldNamesTheApiExpects() {
        val dose = DosingCatalog.plusFor("nutrients")!!

        val json = GsonBuilder().create().toJson(
            DosingCommandRequestDto(deviceMac = controllerMac, dosingType = dose.type, targetVolumeMl = dose.volumeMl)
        )

        assertTrue(json, json.contains("\"deviceMac\":\"$controllerMac\""))
        assertTrue(json, json.contains("\"dosingType\":\"NUTRIENT_UP\""))
        assertTrue(json, json.contains("\"targetVolumeMl\":20.0"))
    }
}
