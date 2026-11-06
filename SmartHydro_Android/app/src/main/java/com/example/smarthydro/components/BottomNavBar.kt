package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.ui.theme.*

enum class NavTab(
    val route: String,
    val title: String,
    val iconRes: Int
) {
    STRUCTURE("home", "Structure", R.drawable.ic_structure),
    READINGS("readings", "Readings", R.drawable.ic_readings),
    CAMERA("gallery", "Camera", R.drawable.ic_camera),
    ALERTS("alerts", "Alerts", R.drawable.ic_notifications),

    // Admin-specific tabs
    ADMIN_DASHBOARD("admin_dashboard", "Dashboard", R.drawable.ic_dashboard),
    ADMIN_APPROVALS("admin_approvals", "Approvals", R.drawable.ic_pending_users),
    ADMIN_SITES("admin_sites", "Sites", R.drawable.ic_sites_tree)
}

@Composable
fun BottomNavBar(
    currentRoute: String,
    readingsLocked: Boolean = false,
    tabs: List<NavTab> = NavTab.values().toList(),
    onTabSelected: (NavTab) -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val pillBg = if (isDark) NavSelectedPillDark else NavSelectedPill
    val activeContentColor = if (isDark) InkStrongDark else ForestGreen

    Row(
        modifier = modifier
            .fillMaxWidth()
            .background(if (isDark) HeaderDarkBg else ForestGreen)
            .navigationBarsPadding()
            .padding(horizontal = 8.dp, vertical = 6.dp),
        horizontalArrangement = Arrangement.SpaceAround,
        verticalAlignment = Alignment.CenterVertically
    ) {
        tabs.forEach { tab ->
            val isSelected = when (tab) {
                NavTab.STRUCTURE -> currentRoute == "home" || currentRoute == "add_structure" || currentRoute.startsWith("edit_structure/")
                NavTab.READINGS -> currentRoute.startsWith("readings/") || currentRoute.startsWith("sensor_detail/")
                NavTab.CAMERA -> currentRoute == "gallery" || currentRoute == "camera_view"
                NavTab.ALERTS -> currentRoute == "alerts"
                NavTab.ADMIN_DASHBOARD -> currentRoute == "admin_dashboard"
                NavTab.ADMIN_APPROVALS -> currentRoute == "admin_approvals"
                NavTab.ADMIN_SITES -> currentRoute == "admin_sites"
            }

            // Readings stays tappable while locked (so it can explain why), but is dimmed
            val isLocked = tab == NavTab.READINGS && readingsLocked
            val contentColor = if (isSelected) activeContentColor else Color.White.copy(alpha = if (isLocked) 0.45f else 0.85f)

            if (isSelected) {
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(18.dp))
                        .background(pillBg)
                        .clickable { onTabSelected(tab) }
                        .padding(horizontal = 12.dp, vertical = 6.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Icon(
                            painter = painterResource(id = tab.iconRes),
                            contentDescription = tab.title,
                            tint = contentColor,
                            modifier = Modifier.size(24.dp)
                        )
                        Spacer(modifier = Modifier.height(2.dp))
                        Text(
                            text = tab.title,
                            color = contentColor,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold
                        )
                    }
                }
            } else {
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(18.dp))
                        .clickable { onTabSelected(tab) }
                        .padding(horizontal = 8.dp, vertical = 6.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Icon(
                            painter = painterResource(id = tab.iconRes),
                            contentDescription = tab.title,
                            tint = contentColor,
                            modifier = Modifier.size(24.dp)
                        )
                        Spacer(modifier = Modifier.height(2.dp))
                        Text(
                            text = tab.title,
                            color = contentColor,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Normal
                        )
                    }
                }
            }
        }
    }
}
