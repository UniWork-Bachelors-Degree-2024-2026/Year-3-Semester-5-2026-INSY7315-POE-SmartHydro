package com.example.smarthydro.screens

import android.content.Intent
import android.provider.Settings
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.BoxApiClient
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.BoxPairing
import com.example.smarthydro.data.HistoryLogic
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.statusAnnouncement
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun BoxPairingScreen(
    structureId: String,
    onNavigateBack: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    val structure = StructureStore.find(structureId)
    val mac = structure?.macAddress

    val ssid = mac?.let { BoxPairing.expectedSsid(it, structure.structureType) }
    val hotspotPrefix = structure?.let { BoxPairing.hotspotPrefix(it.structureType) } ?: "SmartHydro-"

    var boxIpAddress by remember(structureId) { mutableStateOf(structure?.boxIpAddress ?: "192.168.4.1") }
    var isTesting by remember { mutableStateOf(false) }
    var resultMessage by remember { mutableStateOf<String?>(null) }
    var resultSucceeded by remember { mutableStateOf(false) }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        TopHeaderBar()

        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column {
                    Text(
                        text = "Pair with Your Box",
                        modifier = Modifier.screenHeading(),
                        fontSize = 20.sp,
                        fontWeight = FontWeight.Bold,
                        color = if (isDark) TextPrimaryDark else ForestGreenDark
                    )
                    Text(
                        text = structure?.title ?: "Structure",
                        fontSize = 13.sp,
                        color = secondaryText
                    )
                }

                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(8.dp))
                        .clickable { onNavigateBack() }
                        .padding(horizontal = 6.dp, vertical = 4.dp)
                ) {
                    Text(text = "Back", fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = secondaryText)
                }
            }

            if (mac.isNullOrBlank()) {
                Text(
                    text = "This structure has no controller MAC address saved, so its box network can't be worked out. Edit the structure and add its MAC first.",
                    fontSize = 14.sp,
                    color = if (isDark) StatusCautionTextDark else StatusCautionText
                )
            } else {
                BluetoothPairingSection(structure)

                Text(
                    text = "Or use the box's Wi-Fi hotspot",
                    fontSize = 13.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = secondaryText
                )

                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(14.dp))
                        .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                        .padding(16.dp)
                ) {
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(text = "1. Join this WiFi network", fontSize = 15.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
                        Text(text = "Network: ${ssid ?: "$hotspotPrefix…"}", fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurface)
                        // The box's hotspot is open, so there is nothing to type in
                        Text(text = "No password needed", fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurface)
                        Text(
                            text = "Can't see it? Look for a network starting with \"$hotspotPrefix\", and make sure the box is switched on and nearby.",
                            fontSize = 12.sp,
                            color = secondaryText
                        )
                        Text(
                            text = "This is your box's own hotspot - your phone won't have internet while connected to it. If your phone asks whether to stay connected, choose to stay.",
                            fontSize = 12.sp,
                            color = secondaryText
                        )
                    }
                }

                Button(
                    onClick = {
                        context.startActivity(Intent(Settings.ACTION_WIFI_SETTINGS))
                    },
                    modifier = Modifier.fillMaxWidth().height(48.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                        contentColor = androidx.compose.ui.graphics.Color.White
                    )
                ) {
                    Text(text = "Open WiFi Settings", fontWeight = FontWeight.Bold)
                }

                Text(text = "2. Confirm the box's address", fontSize = 15.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
                Text(
                    text = "Leave this as 192.168.4.1. That's the box's address on its own hotspot, so it only needs changing if you've been given a different one.",
                    fontSize = 12.sp,
                    color = secondaryText
                )

                OutlinedTextField(
                    value = boxIpAddress,
                    onValueChange = { boxIpAddress = it },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = ForestGreen)
                )

                Button(
                    onClick = {
                        if (isTesting) return@Button
                        isTesting = true
                        resultMessage = null
                        scope.launch {
                            try {
                                val response = BoxApiClient.serviceFor(boxIpAddress).getLiveTelemetry()
                                // A faulty pH probe reports -1, which shouldn't be quoted as a real reading
                                val body = response.body()?.let { HistoryLogic.withoutFaults(it) }
                                if (response.isSuccessful && body != null) {
                                    StructureStore.setBoxIpAddress(structureId, boxIpAddress.trim())
                                    resultSucceeded = true
                                    // A box with no pH probe still connects, it just has no pH to quote
                                    resultMessage = body.ph?.let { "Connected! Reading ${"%.1f".format(it)} pH right now." }
                                        ?: "Connected! The box is answering."
                                } else {
                                    resultSucceeded = false
                                    resultMessage = "The box didn't answer. Check you're connected to its network."
                                }
                            } catch (e: Exception) {
                                resultSucceeded = false
                                resultMessage = "Couldn't reach ${boxIpAddress.trim()}. Check you're connected to the right network."
                            } finally {
                                isTesting = false
                            }
                        }
                    },
                    modifier = Modifier.fillMaxWidth().height(48.dp),
                    shape = RoundedCornerShape(12.dp),
                    enabled = !isTesting,
                    colors = ButtonDefaults.buttonColors(containerColor = if (isDark) SurfaceDarkVariant else BackgroundLight)
                ) {
                    Text(
                        text = if (isTesting) "Testing…" else "Test Connection",
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface
                    )
                }

                resultMessage?.let { message ->
                    Text(
                        text = message,
                        modifier = Modifier.statusAnnouncement(),
                        fontSize = 14.sp,
                        color = if (resultSucceeded) (if (isDark) StatusGoodTextDark else StatusGoodText) else (if (isDark) StatusCautionTextDark else StatusCautionText)
                    )
                }

                if (resultSucceeded) {
                    Button(
                        onClick = {
                            Toast.makeText(context, "Paired with ${ssid ?: "your box"}.", Toast.LENGTH_SHORT).show()
                            onNavigateBack()
                        },
                        modifier = Modifier.fillMaxWidth().height(48.dp),
                        shape = RoundedCornerShape(12.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                            contentColor = androidx.compose.ui.graphics.Color.White
                        )
                    ) {
                        Text(text = "Done", fontWeight = FontWeight.Bold)
                    }
                }
            }
        }
    }
}