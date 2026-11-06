package com.example.smarthydro.components

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.unit.dp
import com.example.smarthydro.ui.theme.*

// A filter chip that opens a list of choices
@Composable
fun FilterDropdownChip(
    label: String,
    active: Boolean,
    options: List<Pair<String, () -> Unit>>
) {
    var open by remember { mutableStateOf(false) }

    Box {
        FilterChip(
            selected = active,
            onClick = { open = true },
            label = { Text("$label ▾", maxLines = 1) },
            shape = RoundedCornerShape(20.dp),
            colors = smartHydroChipColors(LocalThemeIsDark.current)
        )
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            options.forEach { (text, action) ->
                DropdownMenuItem(
                    text = { Text(text) },
                    onClick = {
                        open = false
                        action()
                    }
                )
            }
        }
    }
}

// Green when selected
@Composable
fun smartHydroChipColors(isDark: Boolean) = FilterChipDefaults.filterChipColors(
    containerColor = MaterialTheme.colorScheme.surface,
    labelColor = if (isDark) TextSecondaryDark else TextSecondary,
    selectedContainerColor = ForestGreen.copy(alpha = if (isDark) 0.28f else 0.14f),
    selectedLabelColor = if (isDark) TextPrimaryDark else ForestGreenDark
)