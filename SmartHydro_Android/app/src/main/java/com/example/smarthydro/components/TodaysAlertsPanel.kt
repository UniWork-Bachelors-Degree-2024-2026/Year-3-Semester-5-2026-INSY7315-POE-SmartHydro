package com.example.smarthydro.components

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.data.AlertStore
import com.example.smarthydro.models.AlertItem
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

// Card at the bottom of the readings page listing today's alerts, with a menu to open the full list
@Composable
fun TodaysAlertsPanel(
    alerts: List<AlertItem>,
    onViewAllAlerts: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    var menuExpanded by remember { mutableStateOf(false) }
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    Column(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp))
            .background(MaterialTheme.colorScheme.surface)
            .padding(14.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                text = "Today's Alerts",
                fontSize = 18.sp,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface
            )

            Box {
                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .clip(RoundedCornerShape(10.dp))
                        .clickable { menuExpanded = true }
                        .semantics { contentDescription = "Alert options" },
                    contentAlignment = Alignment.Center
                ) {
                    Text(text = "⋮", modifier = Modifier.decorative(), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = secondaryText)
                }

                DropdownMenu(
                    expanded = menuExpanded,
                    onDismissRequest = { menuExpanded = false }
                ) {
                    DropdownMenuItem(
                        text = { Text(text = "View all alerts") },
                        onClick = {
                            menuExpanded = false
                            onViewAllAlerts()
                        }
                    )
                    HorizontalDivider()
                    DropdownMenuItem(
                        text = { Text(text = "Refresh") },
                        onClick = {
                            menuExpanded = false
                            scope.launch {
                                AlertStore.refresh()
                                Toast.makeText(context, "Alerts refreshed", Toast.LENGTH_SHORT).show()
                            }
                        }
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(8.dp))

        if (alerts.isEmpty()) {
            Text(
                text = "Nothing to show yet.",
                fontSize = 13.sp,
                color = secondaryText,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 12.dp)
            )
        } else {
            alerts.forEach { alert ->
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 6.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Box(
                        modifier = Modifier
                            .size(40.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(alert.accentColor),
                        contentAlignment = Alignment.Center
                    ) {
                        SensorIcon(iconType = alert.iconType, tint = Color.White, size = 22.dp)
                    }

                    Spacer(modifier = Modifier.width(12.dp))

                    Column {
                        Text(
                            text = alert.title,
                            fontSize = 15.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = MaterialTheme.colorScheme.onSurface
                        )
                        Text(
                            text = alert.timeAndLocation,
                            fontSize = 12.sp,
                            color = secondaryText
                        )
                    }
                }
            }

            Text(
                text = "Nothing more to show...",
                fontSize = 13.sp,
                color = secondaryText,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 8.dp)
            )
        }
    }
}
