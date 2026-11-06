package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import com.example.smarthydro.transport.BoxLink
import com.example.smarthydro.transport.BoxMode
import com.example.smarthydro.transport.BoxModeInfo
import com.example.smarthydro.transport.ModeControl
import com.example.smarthydro.ui.theme.SmartHydroTheme

// Previews of the mode buttons for Android Studio, for the ways a box can be and each theme

@Composable
private fun ModePreview(info: BoxModeInfo, dark: Boolean, requested: BoxMode? = null) {
    SmartHydroTheme(darkTheme = dark) {
        ModeControl.content(info, BoxLink.BLUETOOTH, requested)?.let { content ->
            Box(Modifier.background(MaterialTheme.colorScheme.background).padding(16.dp)) {
                ModeControlSection(content = content, onChoose = {})
            }
        }
    }
}

@Preview(name = "LoRa, light", showBackground = true, widthDp = 380)
@Composable
private fun LoRaLight() = ModePreview(BoxModeInfo(BoxMode.LORA_BLE, BoxMode.LORA_BLE, false), dark = false)

@Preview(name = "LoRa, dark", showBackground = true, widthDp = 380)
@Composable
private fun LoRaDark() = ModePreview(BoxModeInfo(BoxMode.LORA_BLE, BoxMode.LORA_BLE, false), dark = true)

@Preview(name = "Switching to Online, light", showBackground = true, widthDp = 380)
@Composable
private fun SwitchingLight() = ModePreview(BoxModeInfo(BoxMode.LORA_BLE, BoxMode.LORA_BLE, false), dark = false, requested = BoxMode.ONLINE)

@Preview(name = "Fell back to Online, light", showBackground = true, widthDp = 380)
@Composable
private fun FellBackLight() = ModePreview(BoxModeInfo(BoxMode.ONLINE, BoxMode.LORA_BLE, true), dark = false)

@Preview(name = "Fell back to Online, dark", showBackground = true, widthDp = 380)
@Composable
private fun FellBackDark() = ModePreview(BoxModeInfo(BoxMode.ONLINE, BoxMode.LORA_BLE, true), dark = true)

@Preview(name = "Hotspot, light", showBackground = true, widthDp = 380)
@Composable
private fun HotspotLight() = ModePreview(BoxModeInfo(BoxMode.SOFTAP, BoxMode.SOFTAP, false), dark = false)
