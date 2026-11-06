package com.example.smarthydro.data

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import com.example.smarthydro.ui.theme.LabelAmber
import com.example.smarthydro.ui.theme.LabelBlue
import com.example.smarthydro.ui.theme.LabelBrown
import com.example.smarthydro.ui.theme.LabelGreen
import com.example.smarthydro.ui.theme.LabelRed

// Converts between the app's 5 swatch colours and the hex value stored on a structure.
object StructureLabelColours {

    private val options = listOf(LabelGreen, LabelRed, LabelAmber, LabelBlue, LabelBrown)

    // Converts a swatch colour into the hex value the API expects, falling back to green
    fun toApi(color: Color): String = "#" + hex(options.find { it == color } ?: LabelGreen)

    // Converts a stored API colour back into a swatch colour, falling back to green for
    // anything unrecognised (including the API's own legacy default, #2ECC71)
    fun fromApi(value: String?): Color {
        val cleaned = value?.trim()?.removePrefix("#")?.uppercase()
        return options.find { hex(it) == cleaned } ?: LabelGreen
    }

    // Distinct icon type key for each swatch colour:
    // Green -> flower/plant, Red -> strawberry, Amber -> sun, Blue -> water_drop, Brown -> carrot
    fun iconTypeFor(color: Color): String = when (color) {
        LabelRed -> "strawberry"
        LabelAmber -> "sun"
        LabelBlue -> "water_drop"
        LabelBrown -> "carrot"
        else -> "flower" // LabelGreen & default
    }

    // Spoken name for each swatch, since the colour and icon are the only things that tell them apart
    fun nameFor(color: Color): String = when (color) {
        LabelRed -> "Red, strawberry"
        LabelAmber -> "Amber, sun"
        LabelBlue -> "Blue, water drop"
        LabelBrown -> "Brown, carrot"
        else -> "Green, flower"
    }

    // Direct drawable resource mapping for each swatch colour
    fun drawableResFor(color: Color): Int = when (color) {
        LabelRed -> com.example.smarthydro.R.drawable.ic_strawberry
        LabelAmber -> com.example.smarthydro.R.drawable.ic_sun
        LabelBlue -> com.example.smarthydro.R.drawable.ic_water_drop
        LabelBrown -> com.example.smarthydro.R.drawable.ic_carrot
        else -> com.example.smarthydro.R.drawable.ic_flower
    }

    private fun hex(color: Color): String =
        "%06X".format(0xFFFFFF and color.toArgb())
}
