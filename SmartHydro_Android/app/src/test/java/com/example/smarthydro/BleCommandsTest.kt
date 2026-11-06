package com.example.smarthydro

import com.example.smarthydro.transport.BleCommands
import com.example.smarthydro.transport.BoxMode
import com.example.smarthydro.transport.CommandIdSource
import com.google.gson.JsonParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks the commands built for a box over Bluetooth: the JSON, the dose limits and the command ids
class BleCommandsTest {

    private val mac = "A4:CF:12:9B:3E:01"

    private fun json(text: String) = JsonParser.parseString(text).asJsonObject

    // ---- relays and mode ----

    @Test
    fun relayCommandNamesTheActuatorAndState() {
        val command = json(BleCommands.relay(mac, 7, "growLight", true))

        assertEquals("growLight", command["actuator"].asString)
        assertTrue(command["state"].asBoolean)
        assertEquals(7, command["cid"].asInt)
        assertEquals(mac, command["m"].asString)
    }

    @Test
    fun relayCommandCanSwitchOff() {
        assertEquals(false, json(BleCommands.relay(mac, 8, "mainWaterPump", false))["state"].asBoolean)
    }

    @Test
    fun modeCommandCarriesTheModeNumber() {
        assertEquals(1, json(BleCommands.mode(mac, 9, BoxMode.ONLINE))["mode"].asInt)
        assertEquals(0, json(BleCommands.mode(mac, 10, BoxMode.LORA_BLE))["mode"].asInt)
        assertEquals(2, json(BleCommands.mode(mac, 11, BoxMode.SOFTAP))["mode"].asInt)
    }

    // ---- doses ----

    @Test
    fun eachDoseTypeUsesItsOwnKey() {
        assertEquals(2.0, json(BleCommands.dose(mac, 1, "PH_UP", 2.0)!!)["pu"].asDouble, 0.0001)
        assertEquals(2.0, json(BleCommands.dose(mac, 2, "PH_DOWN", 2.0)!!)["pd"].asDouble, 0.0001)
        assertEquals(20.0, json(BleCommands.dose(mac, 3, "NUTRIENT_UP", 20.0)!!)["nu"].asDouble, 0.0001)
        assertEquals(25.0, json(BleCommands.dose(mac, 4, "DILUTION_DOWN", 25.0)!!)["nd"].asDouble, 0.0001)
    }

    @Test
    fun doseCommandCarriesTheBoxAndId() {
        val command = json(BleCommands.dose(mac, 12, "PH_UP", 2.0)!!)

        assertEquals(12, command["cid"].asInt)
        assertEquals(mac, command["m"].asString)
    }

    @Test
    fun doseOfAnUnknownTypeIsRefused() {
        assertNull(BleCommands.dose(mac, 1, "WATER", 10.0))
    }

    @Test
    fun doseTheBoxWouldNotRunIsRefused() {
        assertNull(BleCommands.dose(mac, 1, "PH_UP", 0.4))
        assertNull(BleCommands.dose(mac, 1, "NUTRIENT_UP", 25.1))
        assertNull(BleCommands.dose(mac, 1, "DILUTION_DOWN", 50.0))
        assertNull(BleCommands.dose(mac, 1, "PH_UP", Double.NaN))
    }

    @Test
    fun phDoseIsLimitedToTwoMillilitres() {
        assertNotNull(BleCommands.dose(mac, 1, "PH_UP", 2.0))
        assertNotNull(BleCommands.dose(mac, 1, "PH_DOWN", 2.0))
        assertNull(BleCommands.dose(mac, 1, "PH_UP", 2.1))
        assertNull(BleCommands.dose(mac, 1, "PH_DOWN", 10.0))
    }

    @Test
    fun otherDosesAreLimitedToTwentyFiveMillilitres() {
        assertNotNull(BleCommands.dose(mac, 1, "NUTRIENT_UP", 25.0))
        assertNotNull(BleCommands.dose(mac, 1, "DILUTION_DOWN", 25.0))
        assertEquals(2.0, BleCommands.maxDoseMl("PH_UP"), 0.0)
        assertEquals(25.0, BleCommands.maxDoseMl("NUTRIENT_UP"), 0.0)
    }

    @Test
    fun smallestDoseIsAccepted() {
        assertNotNull(BleCommands.dose(mac, 1, "PH_UP", 0.5))
        assertNotNull(BleCommands.dose(mac, 1, "NUTRIENT_UP", 0.5))
    }

    // ---- command ids ----

    @Test
    fun idsCountUpFromTheStart() {
        val ids = CommandIdSource(first = 100)

        assertEquals(100, ids.next())
        assertEquals(101, ids.next())
        assertEquals(102, ids.next())
    }

    @Test
    fun idsGoBackToOneAfterTheLimitAndNeverUseZero() {
        val ids = CommandIdSource(first = CommandIdSource.MAX_ID)

        assertEquals(CommandIdSource.MAX_ID, ids.next())
        assertEquals(1, ids.next())
    }

    @Test
    fun idsStartAtARandomPlaceInRange() {
        repeat(50) {
            val first = CommandIdSource().next()

            assertTrue(first in 1..CommandIdSource.MAX_ID)
        }
    }

    @Test
    fun startOutsideTheRangeIsBroughtBackIn() {
        assertEquals(1, CommandIdSource(first = 0).next())
        assertEquals(CommandIdSource.MAX_ID, CommandIdSource(first = 99999).next())
    }
}
