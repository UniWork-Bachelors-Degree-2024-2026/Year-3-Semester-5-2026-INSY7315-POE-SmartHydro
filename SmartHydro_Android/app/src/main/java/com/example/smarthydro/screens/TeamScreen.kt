package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.InviteWorkerDialog
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.SiteStore
import com.example.smarthydro.data.TeamStatus
import com.example.smarthydro.data.TeamStore
import com.example.smarthydro.models.TeamMemberDto
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

@Composable
fun TeamScreen(
    onNavigateBack: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    var showInviteDialog by remember { mutableStateOf(false) }
    var memberPendingRemove by remember { mutableStateOf<TeamMemberDto?>(null) }
    var invitePendingCancel by remember { mutableStateOf<TeamMemberDto?>(null) }

    var isRefreshing by remember { mutableStateOf(false) }
    val refreshRotation by animateFloatAsState(
        targetValue = if (isRefreshing) 360f else 0f,
        animationSpec = if (isRefreshing) {
            infiniteRepeatable(
                animation = tween(durationMillis = 800, easing = LinearEasing),
                repeatMode = RepeatMode.Restart
            )
        } else {
            tween(durationMillis = 200, easing = LinearEasing)
        },
        label = "teamRefreshRotation"
    )

    fun refreshTeam() {
        if (isRefreshing) return
        scope.launch {
            isRefreshing = true
            SiteStore.refresh()
            TeamStore.refresh()
            isRefreshing = false
            Toast.makeText(context, "Team refreshed", Toast.LENGTH_SHORT).show()
        }
    }

    LaunchedEffect(Unit) {
        TeamStore.loadFromCache()
        SiteStore.refresh()
        TeamStore.refresh()
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        TopHeaderBar()

        PullToRefresh(
            isRefreshing = isRefreshing,
            onRefresh = { refreshTeam() }
        ) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 16.dp),
                contentPadding = PaddingValues(top = 16.dp, bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                // Header Row: Centered Title with Back button on the left and Refresh on the right
                item {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 4.dp)
                    ) {
                        IconButton(
                            onClick = onNavigateBack,
                            modifier = Modifier
                                .align(Alignment.CenterStart)
                                .size(40.dp)
                        ) {
                            Icon(
                                painter = painterResource(id = R.drawable.ic_chevron_left),
                                contentDescription = "Back",
                                tint = if (isDark) TextPrimaryDark else ForestGreenDark,
                                modifier = Modifier.size(24.dp)
                            )
                        }

                        Text(
                            text = "Your Team",
                            fontSize = 22.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (isDark) TextPrimaryDark else ForestGreenDark,
                            modifier = Modifier.screenHeading().align(Alignment.Center)
                        )

                        IconButton(
                            onClick = { refreshTeam() },
                            modifier = Modifier
                                .align(Alignment.CenterEnd)
                                .size(40.dp)
                        ) {
                            Icon(
                                imageVector = Icons.Filled.Refresh,
                                contentDescription = "Refresh Team",
                                tint = ForestGreen,
                                modifier = Modifier
                                    .size(24.dp)
                                    .graphicsLayer(rotationZ = refreshRotation)
                            )
                        }
                    }
                }

                item {
                    Button(
                        onClick = { showInviteDialog = true },
                        modifier = Modifier.fillMaxWidth().height(48.dp),
                        shape = RoundedCornerShape(12.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                            contentColor = Color.White
                        )
                    ) {
                        Text(text = "Invite a Worker", fontWeight = FontWeight.Bold)
                    }
                }

                when {
                    TeamStore.isLoading && TeamStore.members.isEmpty() -> item {
                        Text(
                            text = "Loading your team…",
                            fontSize = 14.sp,
                            color = secondaryText,
                            modifier = Modifier.padding(vertical = 24.dp)
                        )
                    }
                    TeamStore.loadError != null -> item {
                        Text(
                            text = TeamStore.loadError ?: "",
                            fontSize = 14.sp,
                            color = if (isDark) StatusCautionTextDark else StatusCautionText,
                            modifier = Modifier.padding(vertical = 24.dp)
                        )
                    }
                    TeamStore.members.isEmpty() -> item {
                        Text(
                            text = "Nobody has been invited to your sites yet.",
                            fontSize = 14.sp,
                            color = secondaryText,
                            modifier = Modifier.padding(vertical = 24.dp)
                        )
                    }
                    else -> {
                        val bySite = TeamStore.members.groupBy { it.siteId to it.siteName }
                        bySite.forEach { (site, members) ->
                            item(key = "team_site_${site.first}") {
                                Text(
                                    text = site.second,
                                    fontSize = 15.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = secondaryText,
                                    modifier = Modifier.padding(top = 4.dp, bottom = 2.dp)
                                )
                            }

                            items(members, key = { "${it.siteId}_${it.email}" }) { member ->
                                TeamMemberRow(
                                    member = member,
                                    onRemove = { memberPendingRemove = member },
                                    onCancelInvite = { invitePendingCancel = member }
                                )
                            }
                        }
                    }
                }
            }
        }
    }

    if (showInviteDialog) {
        InviteWorkerDialog(
            onDismiss = { showInviteDialog = false },
            onInvite = { siteId, email ->
                showInviteDialog = false
                scope.launch {
                    val result = TeamStore.invite(siteId, email)
                    Toast.makeText(context, result.message, Toast.LENGTH_SHORT).show()
                }
            }
        )
    }

    memberPendingRemove?.let { member ->
        ConfirmationDialog(
            title = "Remove from site?",
            message = "${member.email} will lose access to ${member.siteName}. Their account and any other sites they belong to are unaffected.",
            confirmButtonText = "Remove",
            dismissButtonText = "Cancel",
            isDestructive = true,
            onDismiss = { memberPendingRemove = null },
            onConfirm = {
                val userId = member.userId
                val siteId = member.siteId
                memberPendingRemove = null
                if (userId != null) {
                    scope.launch {
                        val result = TeamStore.removeMember(siteId, userId)
                        Toast.makeText(context, result.message, Toast.LENGTH_SHORT).show()
                    }
                }
            }
        )
    }

    invitePendingCancel?.let { invite ->
        ConfirmationDialog(
            title = "Cancel invitation?",
            message = "The link and access code sent to ${invite.email} will stop working.",
            confirmButtonText = "Cancel invitation",
            dismissButtonText = "Keep it",
            isDestructive = true,
            onDismiss = { invitePendingCancel = null },
            onConfirm = {
                val siteId = invite.siteId
                val email = invite.email
                invitePendingCancel = null
                scope.launch {
                    val result = TeamStore.cancelInvite(siteId, email)
                    Toast.makeText(context, result.message, Toast.LENGTH_SHORT).show()
                }
            }
        )
    }
}

@Composable
private fun TeamMemberRow(
    member: TeamMemberDto,
    onRemove: () -> Unit,
    onCancelInvite: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    val (statusLabel, pillBg, pillText) = when (member.status) {
        TeamStatus.JOINED -> Triple("Joined", if (isDark) StatusGoodBgDark else StatusGoodBg, if (isDark) StatusGoodTextDark else StatusGoodText)
        TeamStatus.EXPIRED -> Triple("Expired", if (isDark) StatusCautionBgDark else StatusCautionBg, if (isDark) StatusCautionTextDark else StatusCautionText)
        else -> Triple("Invited", if (isDark) StatusNeutralBgDark else StatusNeutralBg, if (isDark) StatusNeutralTextDark else StatusNeutralText)
    }

    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .border(1.5.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp))
            .background(MaterialTheme.colorScheme.surface)
            .padding(14.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = member.fullName?.takeIf { it.isNotBlank() } ?: member.email,
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )
                if (!member.fullName.isNullOrBlank()) {
                    Text(text = member.email, fontSize = 13.sp, color = secondaryText)
                }
                Text(
                    text = when (member.status) {
                        TeamStatus.JOINED -> "Joined ${formatDate(member.since)}"
                        TeamStatus.EXPIRED -> "Invitation expired"
                        else -> "Invited ${formatDate(member.since)}"
                    },
                    fontSize = 12.sp,
                    color = secondaryText
                )
            }

            Spacer(modifier = Modifier.width(8.dp))

            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(12.dp))
                    .background(pillBg)
                    .padding(horizontal = 10.dp, vertical = 2.dp)
            ) {
                Text(text = statusLabel, color = pillText, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
            }

            Spacer(modifier = Modifier.width(8.dp))

            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(10.dp))
                    .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                    .clickable { if (member.status == TeamStatus.JOINED) onRemove() else onCancelInvite() }
                    .padding(horizontal = 10.dp, vertical = 8.dp)
            ) {
                Text(
                    text = if (member.status == TeamStatus.JOINED) "Remove" else "Cancel",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = secondaryText
                )
            }
        }
    }
}


// Formats an ISO timestamp for display, falling back to the raw value if it can't be parsed
private fun formatDate(value: String): String {
    return try {
        DateTimeFormatter.ofPattern("d MMM yyyy", Locale.ENGLISH)
            .withZone(ZoneId.systemDefault())
            .format(Instant.parse(value))
    } catch (e: Exception) {
        value
    }
}