package com.example.smarthydro

import com.example.smarthydro.ui.SimpleModeRoutes
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks which screens Simple Mode leaves when it is switched on
class SimpleModeRoutesTest {

    @Test
    fun theNormalAppsScreensAreLeft() {
        listOf(
            "readings/12", "sensor_detail/12/ph", "edit_structure/12", "team", "thresholds", "add_structure",
            "gallery", "alerts", "admin_dashboard", "admin_approvals", "admin_sites"
        ).forEach { assertTrue(it, SimpleModeRoutes.isNormalScreen(it)) }
    }

    @Test
    fun simpleModesOwnScreenAndSettingsAreKept() {
        assertFalse(SimpleModeRoutes.isNormalScreen("home"))
        assertFalse(SimpleModeRoutes.isNormalScreen("settings"))
    }

    @Test
    fun signInScreensAreKept() {
        listOf(
            "splash", "login", "claim_invite", "register", "register_verify", "register_pending/a@b.c",
            "forgot_password", "reset_password/a@b.c"
        ).forEach { assertFalse(it, SimpleModeRoutes.isNormalScreen(it)) }
    }

    @Test
    fun theBoxPairingScreenIsKept() {
        assertFalse(SimpleModeRoutes.isNormalScreen("box_pairing/12"))
    }

    @Test
    fun noRouteYetIsNotNormal() {
        assertFalse(SimpleModeRoutes.isNormalScreen(""))
    }

    @Test
    fun aRouteThatOnlyLooksSimilarIsNotMatched() {
        assertFalse(SimpleModeRoutes.isNormalScreen("readings"))
        assertFalse(SimpleModeRoutes.isNormalScreen("teams"))
        assertFalse(SimpleModeRoutes.isNormalScreen("galleryx"))
    }
}
