package com.example.smarthydro.data

// One dose a sensor page can send: the type the API expects, what it is called and its fixed volume
data class DoseOption(val type: String, val label: String, val volumeMl: Double) {

    // The volume as shown to the person, without a trailing ".0"
    val volumeText: String
        get() = if (volumeMl == Math.floor(volumeMl)) volumeMl.toInt().toString() else volumeMl.toString()
}

// Lists the one-shot doses the app can send, with the same fixed volumes as the website so a screen
// can never ask for any other amount. Each dosing page has a minus dose and a plus dose
object DosingCatalog {

    // The volumes stay inside what a box runs in one command: 2 mL for pH and 25 mL for the rest
    private val phDown = DoseOption("PH_DOWN", "Lower pH", 2.0)
    private val phUp = DoseOption("PH_UP", "Raise pH", 2.0)
    private val nutrientUp = DoseOption("NUTRIENT_UP", "Add nutrients", 20.0)
    private val dilutionDown = DoseOption("DILUTION_DOWN", "Dilute", 25.0)

    // The dose behind the "-" button of a sensor page, or null when the page has none
    fun minusFor(sensorId: String): DoseOption? = when (sensorId.lowercase()) {
        "clean_water", "ph" -> phDown
        "nutrients", "ec" -> dilutionDown
        else -> null
    }

    // The dose behind the "+" button of a sensor page, or null when the page has none
    fun plusFor(sensorId: String): DoseOption? = when (sensorId.lowercase()) {
        "clean_water", "ph" -> phUp
        "nutrients", "ec" -> nutrientUp
        else -> null
    }

    // Only operators and admins may dose. The API refuses anyone else, so the buttons say so up front
    fun canDose(role: String?): Boolean =
        role.equals("Operator", ignoreCase = true) || role.equals("Admin", ignoreCase = true)
}
