package com.example.smarthydro.screens

import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.components.BoxPairSheet
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.models.StructureItem
import com.example.smarthydro.transport.AndroidPairingEffects
import com.example.smarthydro.transport.BluetoothPermissions
import com.example.smarthydro.transport.BoxConnections
import com.example.smarthydro.transport.ConnectionState
import com.example.smarthydro.transport.PairSheetContent
import com.example.smarthydro.transport.PairingController
import com.example.smarthydro.transport.PairingEvent
import com.example.smarthydro.transport.PairingFailure
import com.example.smarthydro.transport.PairingFlow
import com.example.smarthydro.transport.PairingState
import com.example.smarthydro.transport.statusText
import com.example.smarthydro.transport.toEvent
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.statusAnnouncement
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch

// The part of the pairing screen that pairs a structure's box over Bluetooth: finding the box, the sheet that
// connects to it, and what to do with a box that was already saved. The steps themselves are decided by
// PairingFlow, so this only shows them and passes the person's choices on
@Composable
fun BluetoothPairingSection(structure: StructureItem) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    var savedAddress by remember(structure.id) { mutableStateOf(structure.boxBluetoothAddress) }

    // The permission launcher is made before the controller, so it reaches the controller through this
    val controllerRef = remember(structure.id) { arrayOfNulls<PairingController>(1) }
    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        controllerRef[0]?.send(PairingEvent.PermissionsAnswered(result.values.all { it }))
    }

    val controller = remember(structure.id) {
        val effects = AndroidPairingEffects(
            context = context,
            scope = scope,
            structureId = structure.id,
            askForPermissions = { permissionLauncher.launch(it.toTypedArray()) },
            onSaved = { savedAddress = it }
        )
        PairingController(PairingFlow(structure.macAddress, structure.structureType), effects).also {
            effects.send = it::send
            controllerRef[0] = it
        }
    }

    // Leaving the screen part way stops the search or the connection attempt, and leaves a finished pairing alone
    DisposableEffect(controller) { onDispose { controller.send(PairingEvent.Cancel) } }

    val state by controller.state.collectAsState()

    fun missingPermissions() = BluetoothPermissions.missing(Build.VERSION.SDK_INT) {
        context.checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED
    }

    fun findBox() = controller.send(PairingEvent.Start(missingPermissions()))

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Text(
            text = "Pair over Bluetooth",
            modifier = Modifier.screenHeading(),
            fontSize = 15.sp,
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.onSurface
        )
        Text(text = "Control your box without joining its Wi-Fi.", fontSize = 12.sp, color = secondaryText)

        savedAddress?.let { address ->
            SavedBox(
                structureId = structure.id,
                address = address,
                onForget = {
                    scope.launch {
                        BoxConnections.close(structure.id)
                        StructureStore.setBoxBluetoothAddress(structure.id, null)
                        savedAddress = null
                    }
                }
            )
        }

        when (val current = state) {
            is PairingState.NeedsPermission ->
                Text(
                    text = BluetoothPermissions.reason(Build.VERSION.SDK_INT),
                    modifier = Modifier.statusAnnouncement(),
                    fontSize = 13.sp,
                    color = secondaryText
                )

            is PairingState.Searching -> {
                if (!current.finished) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp, color = ForestGreen)
                        Spacer(modifier = Modifier.width(10.dp))
                        Text(text = "Looking for your box…", modifier = Modifier.statusAnnouncement(), fontSize = 13.sp, color = secondaryText)
                    }
                } else {
                    Text(text = "Pick your box from the list.", fontSize = 13.sp, color = secondaryText)
                }

                current.boxes.forEach { box ->
                    BoxRow(name = box.name, signal = box.signalLabel, onClick = { controller.send(PairingEvent.BoxChosen(box.address)) })
                }
            }

            is PairingState.Failed ->
                if (current.failure !is PairingFailure.Connect) {
                    Text(
                        text = current.failure.message,
                        modifier = Modifier.statusAnnouncement(),
                        fontSize = 13.sp,
                        color = if (isDark) StatusCautionTextDark else StatusCautionText
                    )
                }

            else -> Unit
        }

        // Offered when nothing is going on, or to search again after a search that found nothing or ran out of time
        val searchAgain = (state as? PairingState.Searching)?.finished == true
        val failedToFind = (state as? PairingState.Failed)?.failure.let { it != null && it !is PairingFailure.Connect }
        if (state is PairingState.Idle || failedToFind || searchAgain) {
            val label = if (state is PairingState.Idle && savedAddress == null) "Find my box" else "Search again"
            Button(
                onClick = { findBox() },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(48.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                    contentColor = Color.White
                )
            ) {
                Text(text = label, fontWeight = FontWeight.Bold)
            }
        }
    }

    // The sheet for the step pairing is at, if it has one
    PairSheetContent.from(state)?.let { content ->
        BoxPairSheet(content = content, structureType = structure.structureType) { action ->
            action.toEvent(state, missingPermissions())?.let { controller.send(it) }
        }
    }
}

// A box that was paired before, with how its connection is doing and the ways to use or forget it
@Composable
private fun SavedBox(structureId: String, address: String, onForget: () -> Unit) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current

    val connections by BoxConnections.registry.all.collectAsState()
    val box = connections[structureId]?.connection
    val idle = remember { MutableStateFlow<ConnectionState>(ConnectionState.Idle) }
    val connection by (box?.state ?: idle).collectAsState()

    val working = connection is ConnectionState.Ready || connection is ConnectionState.Connecting ||
        connection is ConnectionState.NegotiatingMtu || connection is ConnectionState.DiscoveringServices ||
        connection is ConnectionState.Subscribing || connection is ConnectionState.Pairing

    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(text = "Saved box: $address", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurface)
        Text(
            text = connection.statusText(),
            modifier = Modifier.statusAnnouncement(),
            fontSize = 13.sp,
            color = if (connection is ConnectionState.Ready) (if (isDark) StatusGoodTextDark else StatusGoodText) else (if (isDark) TextSecondaryDark else TextSecondary)
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (!working) {
                OutlinedButton(onClick = { BoxConnections.open(context, structureId, address) }, shape = RoundedCornerShape(10.dp)) {
                    Text(text = "Connect now", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
                }
            }
            OutlinedButton(onClick = onForget, shape = RoundedCornerShape(10.dp)) {
                Text(text = "Forget this box", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
            }
        }
    }
}

// One box in the list, with how strong its signal is
@Composable
private fun BoxRow(name: String, signal: String, onClick: () -> Unit) {
    val isDark = LocalThemeIsDark.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(10.dp))
            .background(MaterialTheme.colorScheme.surface)
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 12.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(text = name, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
        Text(text = signal, fontSize = 12.sp, color = if (isDark) TextSecondaryDark else TextSecondary)
    }
}
