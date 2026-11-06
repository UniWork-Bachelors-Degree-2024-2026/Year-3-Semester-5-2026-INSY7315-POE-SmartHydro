package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.models.AlertSeverity
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*

// The Filter panel on the Alerts page for severity and location
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AlertFilterSheet(
    severity: AlertSeverity?,
    location: String?,
    locations: List<String>,
    onApply: (severity: AlertSeverity?, location: String?) -> Unit,
    onDismiss: () -> Unit
) {
    val isDark = LocalThemeIsDark.current
    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    var chosenSeverity by remember { mutableStateOf(severity) }
    var chosenLocation by remember { mutableStateOf(location) }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp)
                .padding(bottom = 28.dp)
        ) {
            Text(text = "Filter alerts", modifier = Modifier.screenHeading(), fontSize = 20.sp, fontWeight = FontWeight.Bold, color = primaryText)
            Text(text = "Narrow the list by severity or location.", fontSize = 13.sp, color = secondaryText)

            Spacer(modifier = Modifier.height(18.dp))

            SectionLabel("Severity")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OptionChip("Any severity", chosenSeverity == null) { chosenSeverity = null }
                OptionChip("Alert", chosenSeverity == AlertSeverity.ALERT) { chosenSeverity = AlertSeverity.ALERT }
                OptionChip("Caution", chosenSeverity == AlertSeverity.CAUTION) { chosenSeverity = AlertSeverity.CAUTION }
            }

            Spacer(modifier = Modifier.height(16.dp))

            SectionLabel("Location")
            // Scrolls sideways when there are lots of structures
            Row(
                modifier = Modifier.horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                OptionChip("All locations", chosenLocation == null) { chosenLocation = null }
                locations.forEach { place ->
                    OptionChip(place, chosenLocation == place) { chosenLocation = place }
                }
            }

            Spacer(modifier = Modifier.height(24.dp))

            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedButton(
                    onClick = { onApply(null, null) },
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp),
                    shape = RoundedCornerShape(12.dp),
                    border = BorderStroke(1.dp, if (isDark) AccentGreenDark else ForestGreen),
                    colors = smartHydroOutlinedButtonColors(isDark = isDark)
                ) {
                    Text(text = "Reset", color = primaryText, fontWeight = FontWeight.SemiBold)
                }
                Button(
                    onClick = { onApply(chosenSeverity, chosenLocation) },
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = smartHydroButtonColors(isDark = isDark)
                ) {
                    Text(text = "Apply", fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

@Composable
private fun SectionLabel(text: String) {
    Text(
        text = text,
        fontSize = 13.sp,
        fontWeight = FontWeight.SemiBold,
        color = if (LocalThemeIsDark.current) TextPrimaryDark else ForestGreenDark,
        modifier = Modifier.padding(bottom = 8.dp)
    )
}

@Composable
private fun OptionChip(label: String, selected: Boolean, onClick: () -> Unit) {
    FilterChip(
        selected = selected,
        onClick = onClick,
        label = { Text(label, maxLines = 1) },
        shape = RoundedCornerShape(20.dp),
        colors = smartHydroChipColors(LocalThemeIsDark.current)
    )
}