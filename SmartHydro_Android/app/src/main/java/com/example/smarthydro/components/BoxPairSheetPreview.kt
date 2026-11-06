package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import com.example.smarthydro.models.StructureType
import com.example.smarthydro.transport.BoxAdvertisement
import com.example.smarthydro.transport.ConnectionState
import com.example.smarthydro.transport.DisconnectReason
import com.example.smarthydro.transport.PairSheetContent
import com.example.smarthydro.transport.PairingFailure
import com.example.smarthydro.transport.PairingState
import com.example.smarthydro.ui.theme.SmartHydroTheme

// Previews of the pairing sheet for Android Studio, one for each step and each theme

private val previewBox = BoxAdvertisement.parse("SmartHydro-Tower-9B3E01", "AA:BB:CC:DD:EE:FF", -50, 0L)!!

@Composable
private fun SheetPreview(state: PairingState, dark: Boolean, type: StructureType = StructureType.HYDROPONIC) {
    SmartHydroTheme(darkTheme = dark) {
        PairSheetContent.from(state)?.let { content ->
            androidx.compose.foundation.layout.Box(Modifier.background(MaterialTheme.colorScheme.surface)) {
                BoxPairSheetBody(content = content, structureType = type, onAction = {})
            }
        }
    }
}

@Preview(name = "Offer, light", showBackground = true, widthDp = 380)
@Composable
private fun OfferLight() = SheetPreview(PairingState.Confirming(previewBox, listOf(previewBox)), dark = false)

@Preview(name = "Offer, dark", showBackground = true, widthDp = 380)
@Composable
private fun OfferDark() = SheetPreview(PairingState.Confirming(previewBox, listOf(previewBox)), dark = true)

@Preview(name = "Offer fodder, light", showBackground = true, widthDp = 380)
@Composable
private fun OfferFodderLight() {
    val fodder = BoxAdvertisement.parse("SmartHydro-Fodder-44A702", "AA:BB:CC:DD:EE:01", -50, 0L)!!
    SheetPreview(PairingState.Confirming(fodder, listOf(fodder)), dark = false, type = StructureType.FODDER)
}

@Preview(name = "Connecting, light", showBackground = true, widthDp = 380)
@Composable
private fun ConnectingLight() = SheetPreview(PairingState.Connecting(previewBox, ConnectionState.NegotiatingMtu), dark = false)

@Preview(name = "Enter the code, light", showBackground = true, widthDp = 380)
@Composable
private fun CodeLight() = SheetPreview(PairingState.Connecting(previewBox, ConnectionState.Pairing(247)), dark = false)

@Preview(name = "Enter the code, dark", showBackground = true, widthDp = 380)
@Composable
private fun CodeDark() = SheetPreview(PairingState.Connecting(previewBox, ConnectionState.Pairing(247)), dark = true)

@Preview(name = "Connected, light", showBackground = true, widthDp = 380)
@Composable
private fun DoneLight() = SheetPreview(PairingState.Done(previewBox), dark = false)

@Preview(name = "Failed, light", showBackground = true, widthDp = 380)
@Composable
private fun FailedLight() = SheetPreview(PairingState.Failed(PairingFailure.Connect(DisconnectReason.STALE_PAIRING)), dark = false)

@Preview(name = "Failed, dark", showBackground = true, widthDp = 380)
@Composable
private fun FailedDark() = SheetPreview(PairingState.Failed(PairingFailure.Connect(DisconnectReason.PAIRING_FAILED)), dark = true)
