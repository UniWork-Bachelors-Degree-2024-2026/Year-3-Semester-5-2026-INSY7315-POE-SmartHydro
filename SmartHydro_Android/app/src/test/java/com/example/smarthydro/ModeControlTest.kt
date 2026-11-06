package com.example.smarthydro

import com.example.smarthydro.transport.BoxLink
import com.example.smarthydro.transport.BoxMode
import com.example.smarthydro.transport.BoxModeInfo
import com.example.smarthydro.transport.ModeControl
import com.example.smarthydro.transport.ModeOptionState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks what the mode buttons show for each way a box can be, and when a person is asked first
class ModeControlTest {

    private fun info(mode: BoxMode?, preferred: BoxMode? = mode, fellBack: Boolean = false) = BoxModeInfo(mode, preferred, fellBack)

    private fun content(info: BoxModeInfo?, requested: BoxMode? = null, link: BoxLink? = BoxLink.BLUETOOTH) =
        ModeControl.content(info, link, requested)

    private fun statesOf(info: BoxModeInfo, requested: BoxMode? = null) =
        content(info, requested)!!.options.associate { it.mode to it.state }

    // ---- when the buttons are shown ----

    @Test
    fun buttonsAreOnlyShownOverBluetooth() {
        val loRa = info(BoxMode.LORA_BLE)

        assertNotNull(content(loRa, link = BoxLink.BLUETOOTH))
        assertNull(content(loRa, link = BoxLink.HOTSPOT))
        assertNull(content(loRa, link = BoxLink.INTERNET))
        assertNull(content(loRa, link = null))
    }

    @Test
    fun buttonsNeedABoxThatReportsItsMode() {
        assertNull(content(null))
        assertNull(content(info(mode = null)))
    }

    // ---- what they show ----

    @Test
    fun threeButtonsInTheOrderLoRaOnlineHotspot() {
        val options = content(info(BoxMode.LORA_BLE))!!.options

        assertEquals(listOf("LoRa", "Online", "Hotspot"), options.map { it.label })
        assertEquals(listOf(BoxMode.LORA_BLE, BoxMode.ONLINE, BoxMode.SOFTAP), options.map { it.mode })
        assertTrue(options.all { it.description.isNotBlank() })
    }

    @Test
    fun headlineSaysWhichModeTheBoxIsIn() {
        assertEquals("The box is using LoRa", content(info(BoxMode.LORA_BLE))!!.headline)
        assertEquals("The box is using Online", content(info(BoxMode.ONLINE))!!.headline)
        assertEquals("The box is using Hotspot", content(info(BoxMode.SOFTAP))!!.headline)
    }

    @Test
    fun theModeTheBoxIsInIsMarkedAndTheRestCanBeChosen() {
        val states = statesOf(info(BoxMode.ONLINE))

        assertEquals(ModeOptionState.CURRENT, states[BoxMode.ONLINE])
        assertEquals(ModeOptionState.AVAILABLE, states[BoxMode.LORA_BLE])
        assertEquals(ModeOptionState.AVAILABLE, states[BoxMode.SOFTAP])
        assertNull(content(info(BoxMode.ONLINE))!!.note)
    }

    // ---- switching ----

    @Test
    fun aModeBeingSwitchedToIsMarkedAndExplained() {
        val shown = content(info(BoxMode.LORA_BLE), requested = BoxMode.ONLINE)!!

        assertEquals(ModeOptionState.SWITCHING, shown.options.first { it.mode == BoxMode.ONLINE }.state)
        assertEquals(ModeOptionState.CURRENT, shown.options.first { it.mode == BoxMode.LORA_BLE }.state)
        assertEquals("Switching to Online…", shown.note)
    }

    @Test
    fun onceTheBoxHasGotThereItIsNoLongerSwitching() {
        val shown = content(info(BoxMode.ONLINE), requested = BoxMode.ONLINE)!!

        assertTrue(shown.options.none { it.state == ModeOptionState.SWITCHING })
        assertNull(shown.note)
    }

    // ---- the box moving on its own ----

    @Test
    fun aBoxThatFellBackSaysSoAndWhatItWants() {
        val shown = content(info(BoxMode.ONLINE, preferred = BoxMode.LORA_BLE, fellBack = true))!!

        assertEquals(ModeOptionState.WANTED, shown.options.first { it.mode == BoxMode.LORA_BLE }.state)
        assertEquals(ModeOptionState.CURRENT, shown.options.first { it.mode == BoxMode.ONLINE }.state)
        assertEquals(
            "The box moved to Online by itself because LoRa wasn't getting through. It switches back when it can.",
            shown.note
        )
    }

    @Test
    fun aBoxThatChoseItsModeIsNotSaidToHaveFallenBack() {
        val shown = content(info(BoxMode.SOFTAP, preferred = BoxMode.SOFTAP, fellBack = false))!!

        assertNull(shown.note)
        assertTrue(shown.options.none { it.state == ModeOptionState.WANTED })
    }

    @Test
    fun aFallbackWithNoKnownPreferenceIsNotExplained() {
        val shown = content(info(BoxMode.ONLINE, preferred = null, fellBack = true))!!

        assertNull(shown.note)
    }

    @Test
    fun askingForAModeAfterAFallbackIsShownAsSwitching() {
        val shown = content(info(BoxMode.ONLINE, preferred = BoxMode.LORA_BLE, fellBack = true), requested = BoxMode.SOFTAP)!!

        assertEquals("Switching to Hotspot…", shown.note)
        assertEquals(ModeOptionState.SWITCHING, shown.options.first { it.mode == BoxMode.SOFTAP }.state)
    }

    // ---- which buttons can be pressed ----

    @Test
    fun onlyAvailableAndWantedModesCanBeChosen() {
        val shown = content(info(BoxMode.ONLINE, preferred = BoxMode.LORA_BLE, fellBack = true), requested = BoxMode.SOFTAP)!!
        val chosen = shown.options.filter { ModeControl.canChoose(it) }.map { it.mode }

        assertEquals(listOf(BoxMode.LORA_BLE), chosen)
    }

    @Test
    fun whenNothingIsHappeningTheTwoOtherModesCanBeChosen() {
        val chosen = content(info(BoxMode.LORA_BLE))!!.options.filter { ModeControl.canChoose(it) }.map { it.mode }

        assertEquals(listOf(BoxMode.ONLINE, BoxMode.SOFTAP), chosen)
    }

    // ---- asking first ----

    @Test
    fun loRaNeedsNoAsking() {
        assertNull(ModeControl.confirmation(BoxMode.LORA_BLE))
    }

    @Test
    fun onlineWarnsAboutNeedingSavedWifiAndFallingBack() {
        val ask = ModeControl.confirmation(BoxMode.ONLINE)!!

        assertEquals("Switch to Online?", ask.title)
        assertTrue(ask.message.contains("saved Wi-Fi"))
        assertTrue(ask.message.contains("3 minutes"))
        assertEquals("Switch", ask.confirmLabel)
    }

    @Test
    fun hotspotSaysItStopsUsingWifiForTheInternet() {
        val ask = ModeControl.confirmation(BoxMode.SOFTAP)!!

        assertEquals("Switch to Hotspot?", ask.title)
        assertTrue(ask.message.contains("own Wi-Fi network"))
        assertFalse(ask.message.contains("readings"))
    }

    // ---- the words for the modes ----

    @Test
    fun eachModeHasItsOwnLabel() {
        assertEquals(listOf("LoRa", "Online", "Hotspot"), BoxMode.entries.map { it.label })
        assertEquals(BoxMode.entries.size, BoxMode.entries.map { it.label }.toSet().size)
    }
}
