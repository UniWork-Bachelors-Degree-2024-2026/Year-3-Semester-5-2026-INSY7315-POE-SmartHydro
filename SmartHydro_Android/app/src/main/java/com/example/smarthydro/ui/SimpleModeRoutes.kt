package com.example.smarthydro.ui

// Which screens belong to the normal app. Simple Mode keeps everything on its own single screen, so none of these are
// shown while it is on
object SimpleModeRoutes {

    private val exact = setOf(
        "team", "thresholds", "add_structure", "gallery", "alerts",
        "admin_dashboard", "admin_approvals", "admin_sites"
    )

    private val prefixes = listOf("readings/", "sensor_detail/", "edit_structure/")

    // True for a screen of the normal app. Settings, the sign-in screens, the box pairing screen and the home route,
    // which becomes Simple Mode's screen by itself, are not
    fun isNormalScreen(route: String): Boolean =
        route in exact || prefixes.any { route.startsWith(it) }
}
