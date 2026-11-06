package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.ForestGreen
import com.example.smarthydro.ui.theme.HeaderSubtitleGreen
import com.example.smarthydro.ui.theme.LocalNavigateToSettings
import kotlinx.coroutines.launch

import androidx.compose.ui.draw.clip
import com.example.smarthydro.ui.theme.CardBorder
import com.example.smarthydro.ui.theme.CardBorderDark
import com.example.smarthydro.ui.theme.ForestGreenDark
import com.example.smarthydro.ui.theme.HeaderDarkBg
import com.example.smarthydro.ui.theme.LocalThemeIsDark
import com.example.smarthydro.ui.theme.TextPrimary
import com.example.smarthydro.ui.theme.TextPrimaryDark
import com.example.smarthydro.ui.theme.TextSecondary
import com.example.smarthydro.ui.theme.TextSecondaryDark
import androidx.compose.foundation.clickable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material3.*

@Composable
fun TopHeaderBar(
    title: String? = null,
    modifier: Modifier = Modifier,
    onNavigateToSettings: (() -> Unit)? = LocalNavigateToSettings.current
) {
    val isAdmin = ApiClient.getSignedInRole() == "Admin"
    val isOffline = !ApiClient.isSignedIn()
    val isDark = LocalThemeIsDark.current

    var offlineSiteName by remember { mutableStateOf(ApiClient.getOfflineSiteName()) }
    var showEditSiteDialog by remember { mutableStateOf(false) }
    var siteNameInput by remember { mutableStateOf("") }

    // Determine effective title: explicit title, or user's assigned site name when logged in, or offline site name
    val effectiveTitle = remember(title, ApiClient.isSignedIn(), isAdmin, SiteStore.sites.toList(), offlineSiteName) {
        title ?: if (isAdmin) {
            "Admin Console"
        } else if (ApiClient.isSignedIn()) {
            SiteStore.sites.firstOrNull()?.name ?: "Emeris NMB"
        } else {
            offlineSiteName
        }
    }

    Box(
        modifier = modifier
            .fillMaxWidth()
            .background(if (isDark) HeaderDarkBg else ForestGreen)
            .statusBarsPadding()
            .padding(horizontal = 16.dp, vertical = 14.dp)
    ) {
        // Center Title (clickable in offline mode to rename)
        Row(
            modifier = Modifier
                .align(Alignment.Center)
                .fillMaxWidth(0.7f)
                .then(
                    if (isOffline && title == null) {
                        Modifier
                            .clip(RoundedCornerShape(8.dp))
                            .clickable {
                                siteNameInput = offlineSiteName
                                showEditSiteDialog = true
                            }
                            .padding(horizontal = 8.dp, vertical = 4.dp)
                    } else {
                        Modifier
                    }
                ),
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                text = effectiveTitle,
                modifier = Modifier.screenHeading(),
                color = Color.White,
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center
            )
            if (isOffline && title == null) {
                Spacer(modifier = Modifier.width(6.dp))
                Icon(
                    imageVector = Icons.Filled.Edit,
                    contentDescription = "Rename Site",
                    tint = HeaderSubtitleGreen,
                    modifier = Modifier.size(16.dp)
                )
            }
        }

        // Settings gear icon on right
        IconButton(
            onClick = {
                onNavigateToSettings?.invoke()
            },
            modifier = Modifier
                .align(Alignment.CenterEnd)
                .size(40.dp)
        ) {
            Icon(
                imageVector = Icons.Filled.Settings,
                contentDescription = "Open Settings",
                tint = Color.White,
                modifier = Modifier.size(24.dp)
            )
        }
    }

    // Custom themed dialog to rename the site in offline mode
    if (showEditSiteDialog) {
        RenameSiteDialog(
            initialName = siteNameInput,
            isDark = isDark,
            onDismiss = { showEditSiteDialog = false },
            onSaveName = { newName ->
                ApiClient.setOfflineSiteName(newName)
                offlineSiteName = newName
                showEditSiteDialog = false
            }
        )
    }
}
