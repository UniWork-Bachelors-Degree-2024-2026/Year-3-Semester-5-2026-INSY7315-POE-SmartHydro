package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.TaskAlt
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.PullToRefresh
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.data.AdminStore
import com.example.smarthydro.models.PendingOperatorDto
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun AdminPendingApprovalsScreen(
    onNavigateBack: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    val pendingList = AdminStore.pendingOperators
    val isLoading = AdminStore.isLoading
    var isOperating by remember { mutableStateOf(false) }
    var isRefreshing by remember { mutableStateOf(false) }

    val refreshRotation by animateFloatAsState(
        targetValue = if (isRefreshing) 360f else 0f,
        animationSpec = if (isRefreshing) {
            infiniteRepeatable(
                animation = tween(750, easing = LinearEasing),
                repeatMode = RepeatMode.Restart
            )
        } else {
            tween(200)
        },
        label = "rotation"
    )

    fun loadPending(isManualClick: Boolean = false) {
        if (isManualClick) {
            isRefreshing = true
        }
        scope.launch {
            val success = AdminStore.refresh()
            if (isManualClick) {
                isRefreshing = false
                if (success) {
                    Toast.makeText(context, "Pending approvals refreshed", Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    LaunchedEffect(Unit) {
        AdminStore.loadFromCache()
        loadPending(isManualClick = false)
    }

    fun handleApprove(operator: PendingOperatorDto) {
        isOperating = true
        scope.launch {
            try {
                val res = ApiClient.service.approveOperator(operator.id)
                if (res.isSuccessful) {
                    Toast.makeText(context, "Operator approved! Site created.", Toast.LENGTH_SHORT).show()
                    AdminStore.removePendingOperator(operator.id)
                    AdminStore.refresh()
                } else {
                    Toast.makeText(context, res.body()?.message ?: "Approval failed", Toast.LENGTH_SHORT).show()
                }
            } catch (e: Exception) {
                Toast.makeText(context, "Network error", Toast.LENGTH_SHORT).show()
            } finally {
                isOperating = false
            }
        }
    }

    fun handleDeny(operator: PendingOperatorDto) {
        isOperating = true
        scope.launch {
            try {
                val res = ApiClient.service.denyOperator(operator.id)
                if (res.isSuccessful) {
                    Toast.makeText(context, "Operator denied", Toast.LENGTH_SHORT).show()
                    AdminStore.removePendingOperator(operator.id)
                    AdminStore.refresh()
                } else {
                    Toast.makeText(context, res.body()?.message ?: "Denial failed", Toast.LENGTH_SHORT).show()
                }
            } catch (e: Exception) {
                Toast.makeText(context, "Network error", Toast.LENGTH_SHORT).show()
            } finally {
                isOperating = false
            }
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        TopHeaderBar()

        PullToRefresh(
            isRefreshing = isRefreshing,
            onRefresh = { loadPending(isManualClick = true) }
        ) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 16.dp),
                contentPadding = PaddingValues(top = 16.dp, bottom = 28.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Header Row
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Column {
                            Text(
                                text = "Pending Approvals",
                                modifier = Modifier.screenHeading(),
                                fontSize = 20.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isDark) TextPrimaryDark else ForestGreenDark
                            )
                            Text(
                                text = "Review self-registered farm operators",
                                fontSize = 12.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }

                        IconButton(onClick = { loadPending(isManualClick = true) }, enabled = !isRefreshing) {
                            Icon(
                                imageVector = Icons.Filled.Refresh,
                                contentDescription = "Refresh",
                                tint = ForestGreen,
                                modifier = Modifier
                                    .size(24.dp)
                                    .graphicsLayer { rotationZ = refreshRotation }
                            )
                        }
                    }
                }

                // Body content
                if (isLoading) {
                    item {
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(vertical = 40.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            CircularProgressIndicator(color = ForestGreen)
                        }
                    }
                } else if (pendingList.isEmpty()) {
                    item {
                        Surface(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(16.dp))
                                .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp)),
                            color = MaterialTheme.colorScheme.surface
                        ) {
                            Column(
                                modifier = Modifier.padding(32.dp),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.spacedBy(12.dp)
                            ) {
                                Box(
                                    modifier = Modifier
                                        .size(52.dp)
                                        .clip(CircleShape)
                                        .background(StatusGoodBg),
                                    contentAlignment = Alignment.Center
                                ) {
                                    Icon(
                                        imageVector = Icons.Filled.TaskAlt,
                                        contentDescription = null,
                                        tint = StatusGoodText,
                                        modifier = Modifier.size(28.dp)
                                    )
                                }
                                Text(
                                    text = "All Caught Up!",
                                    fontSize = 17.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = if (isDark) TextPrimaryDark else TextPrimary
                                )
                                Text(
                                    text = "No farm operators are waiting for approval right now.",
                                    fontSize = 13.sp,
                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }
                        }
                    }
                } else {
                    items(pendingList, key = { it.id }) { operator ->
                        PendingOperatorCard(
                            operator = operator,
                            isDark = isDark,
                            enabled = !isOperating,
                            onApprove = { handleApprove(operator) },
                            onDeny = { handleDeny(operator) }
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun PendingOperatorCard(
    operator: PendingOperatorDto,
    isDark: Boolean,
    enabled: Boolean,
    onApprove: () -> Unit,
    onDeny: () -> Unit
) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(14.dp)),
        color = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Box(
                    modifier = Modifier
                        .size(42.dp)
                        .clip(CircleShape)
                        .background(if (isDark) StatusWarnBgDark else StatusWarnBg),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = (operator.email.firstOrNull() ?: 'O').uppercase(),
                        color = if (isDark) StatusWarnTextDark else StatusWarnText,
                        fontWeight = FontWeight.Bold,
                        fontSize = 18.sp
                    )
                }

                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = operator.email,
                        fontWeight = FontWeight.Bold,
                        fontSize = 15.sp,
                        color = if (isDark) TextPrimaryDark else TextPrimary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Text(
                        text = "Farm: ${operator.farmName ?: "Unspecified"}",
                        fontSize = 13.sp,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }

                Surface(
                    shape = RoundedCornerShape(6.dp),
                    color = if (isDark) StatusWarnBgDark else StatusWarnBg
                ) {
                    Text(
                        text = "Pending",
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        color = if (isDark) StatusWarnTextDark else StatusWarnText,
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 3.dp)
                    )
                }
            }

            HorizontalDivider(color = if (isDark) CardDividerDark else CardDivider)

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                // Deny button
                OutlinedButton(
                    onClick = onDeny,
                    enabled = enabled,
                    modifier = Modifier.weight(1f),
                    shape = RoundedCornerShape(10.dp),
                    border = BorderStroke(1.dp, if (isDark) StatusCautionTextDark.copy(alpha = 0.7f) else StatusCautionText.copy(alpha = 0.5f)),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = if (isDark) StatusCautionTextDark else StatusCautionText)
                ) {
                    Icon(Icons.Filled.Close, contentDescription = null, modifier = Modifier.size(16.dp))
                    Spacer(modifier = Modifier.width(6.dp))
                    Text("Deny", fontWeight = FontWeight.SemiBold)
                }

                // Approve button
                Button(
                    onClick = onApprove,
                    enabled = enabled,
                    modifier = Modifier.weight(1f),
                    shape = RoundedCornerShape(10.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                        contentColor = Color.White
                    )
                ) {
                    Icon(Icons.Filled.Check, contentDescription = null, modifier = Modifier.size(16.dp))
                    Spacer(modifier = Modifier.width(6.dp))
                    Text("Approve", fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}