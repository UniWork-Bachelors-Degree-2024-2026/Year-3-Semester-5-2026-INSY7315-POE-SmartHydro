package com.example.smarthydro.components

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.RestartAlt
import androidx.compose.material.icons.filled.Save
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.data.ThresholdCheck
import com.example.smarthydro.data.ThresholdEditor
import com.example.smarthydro.data.ThresholdSensor
import com.example.smarthydro.models.ThresholdResponseDto
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

// Edits the safe range of the one reading a sensor detail page shows
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ThresholdEditSheet(
    structureId: Int,
    structureName: String,
    sensor: ThresholdSensor,
    thresholds: Map<String, ThresholdResponseDto>,
    onDismiss: () -> Unit,
    onChanged: (Map<String, ThresholdResponseDto>, String) -> Unit
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    val saved = thresholds[sensor.key]
    var minText by remember { mutableStateOf(saved?.let { ThresholdEditor.shownMin(it, sensor) }.orEmpty()) }
    var maxText by remember { mutableStateOf(saved?.let { ThresholdEditor.shownMax(it, sensor) }.orEmpty()) }
    var serverError by remember { mutableStateOf<String?>(null) }
    var isSaving by remember { mutableStateOf(false) }
    var isResetting by remember { mutableStateOf(false) }
    var confirmReset by remember { mutableStateOf(false) }

    val check = ThresholdEditor.check(sensor, minText, maxText)
    val canSave = !isSaving && !isResetting && ThresholdEditor.isChange(check, saved)

    fun save() {
        val range = check as? ThresholdCheck.Valid ?: return
        isSaving = true
        serverError = null
        scope.launch {
            val result = ThresholdEditor.save(structureId, sensor, range.min, range.max)
            isSaving = false
            val updated = result.saved
            if (result.ok && updated != null) {
                val merged = thresholds + (sensor.key to updated)
                ThresholdEditor.remember(structureId, merged.values.toList())
                onChanged(merged, "${sensor.label} range saved.")
            } else {
                serverError = result.message ?: "Couldn't save. Please try again."
            }
        }
    }

    if (confirmReset) {
        ConfirmationDialog(
            title = "Reset ${sensor.label}?",
            message = "$structureName will go back to the healthy default range for ${sensor.label}.",
            confirmButtonText = "Reset",
            dismissButtonText = "Cancel",
            icon = Icons.Filled.RestartAlt,
            isLoading = isResetting,
            onDismiss = { if (!isResetting) confirmReset = false },
            onConfirm = {
                val thresholdId = saved?.id
                if (thresholdId == null) {
                    confirmReset = false
                } else {
                    isResetting = true
                    scope.launch {
                        val result = ThresholdEditor.reset(structureId, thresholdId)
                        isResetting = false
                        confirmReset = false
                        val structure = result.structure
                        if (result.ok && structure != null) {
                            onChanged(
                                structure.thresholds.associateBy { it.sensorType.uppercase() },
                                "${sensor.label} is back to its default range."
                            )
                        } else {
                            serverError = result.message ?: "Couldn't reset. Please try again."
                        }
                    }
                }
            }
        )
    }

    ModalBottomSheet(
        onDismissRequest = { if (!isSaving && !isResetting) onDismiss() },
        sheetState = sheetState,
        containerColor = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp)
                .padding(bottom = 24.dp)
                .imePadding(),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            Column {
                Text(
                    text = "Optimal range",
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                )
                Text(
                    text = "$structureName raises an alert when ${sensor.label} goes outside this range.",
                    fontSize = 13.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary
                )
            }

            ThresholdRangeRow(
                sensor = sensor,
                minText = minText,
                maxText = maxText,
                onMinChange = {
                    minText = it
                    serverError = null
                },
                onMaxChange = {
                    maxText = it
                    serverError = null
                },
                isDefault = saved?.isDefault == true,
                error = (check as? ThresholdCheck.Invalid)?.message ?: serverError,
                enabled = !isSaving && !isResetting,
                onReset = if (saved != null && !saved.isDefault && saved.id != 0) {
                    { confirmReset = true }
                } else {
                    null
                }
            )

            Button(
                onClick = { save() },
                enabled = canSave,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(48.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                    contentColor = Color.White
                )
            ) {
                if (isSaving) {
                    CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp, color = Color.White)
                } else {
                    Icon(imageVector = Icons.Filled.Save, contentDescription = null, modifier = Modifier.size(18.dp))
                }
                Spacer(modifier = Modifier.width(8.dp))
                Text(text = if (isSaving) "Saving…" else "Save range", fontWeight = FontWeight.Bold)
            }
        }
    }
}