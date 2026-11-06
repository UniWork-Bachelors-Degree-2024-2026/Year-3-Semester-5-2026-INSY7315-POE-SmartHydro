package com.example.smarthydro.components

import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.example.smarthydro.R

@Composable
fun SensorIcon(
    iconType: String,
    tint: Color = Color.White,
    size: Dp = 26.dp,
    modifier: Modifier = Modifier
) {
    val drawableRes = when (iconType) {
        "sprinkler" -> R.drawable.ic_sprinkler
        "droplets", "water_drop", "water" -> R.drawable.ic_water_drop
        "ph_droplet", "clean_water", "ph" -> R.drawable.ic_ph
        "thermometer", "temperature" -> R.drawable.ic_thermostat
        "humidity_droplet", "humidity" -> R.drawable.ic_humidity
        "sun", "light" -> R.drawable.ic_sun
        "nutrients_bottle", "nutrients" -> R.drawable.ic_nutrients
        "carrot", "fodder" -> R.drawable.ic_carrot
        "flower" -> R.drawable.ic_flower
        "strawberry" -> R.drawable.ic_strawberry
        else -> R.drawable.ic_sensors
    }

    Icon(
        painter = painterResource(id = drawableRes),
        contentDescription = iconType,
        tint = tint,
        modifier = modifier.size(size)
    )
}
