package com.example.smarthydro.screens

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Login
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.ResetPasswordDialog
import com.example.smarthydro.components.SignOutConfirmDialog
import com.example.smarthydro.components.TopHeaderBar
import com.example.smarthydro.models.ForgotPasswordDto
import com.example.smarthydro.models.ResetPasswordDto
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.switchRow
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun SettingsScreen(
    onNavigateBack: () -> Unit,
    onNavigateToLogin: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val isDark = LocalThemeIsDark.current
    val onToggleDarkMode = LocalThemeToggle.current

    val signedInEmail = ApiClient.getSignedInEmail()
    val signedInRole = ApiClient.getSignedInRole()
    val isSignedIn = ApiClient.isSignedIn()

    // State for Custom Full Name editing
    // Keyed on the email so the field reloads when a different account is signed in
    var customFullNameInput by remember(signedInEmail) { mutableStateOf(ApiClient.getCustomFullName() ?: "") }
    var isEditingFullName by remember { mutableStateOf(false) }

    // Dialog state for Password Reset
    var showPasswordResetDialog by remember { mutableStateOf(false) }
    var resetEmailInput by remember { mutableStateOf(signedInEmail ?: "") }
    var resetOtpCodeInput by remember { mutableStateOf("") }
    var newPasswordInput by remember { mutableStateOf("") }
    var confirmPasswordInput by remember { mutableStateOf("") }
    var isNewPasswordVisible by remember { mutableStateOf(false) }
    var isConfirmPasswordVisible by remember { mutableStateOf(false) }
    var isResetLoading by remember { mutableStateOf(false) }
    var resetDialogStep by remember { mutableStateOf(1) } // 1 = Request Code, 2 = Enter Code, New & Confirm Password
    var resetErrorMessage by remember { mutableStateOf<String?>(null) }

    // Dialog state for Logout Confirmation
    var showLogoutConfirmation by remember { mutableStateOf(false) }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        // App top header with synced branding
        TopHeaderBar()

        Column(
            modifier = Modifier
                .fillMaxSize()
                .navigationBarsPadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 12.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp)
        ) {
            // Navigation Back Bar & Screen Title
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier
                        .clip(RoundedCornerShape(8.dp))
                        .clickable { onNavigateBack() }
                        .padding(horizontal = 4.dp, vertical = 4.dp)
                ) {
                    Icon(
                        imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                        contentDescription = "Back",
                        tint = if (isDark) TextPrimaryDark else ForestGreenDark,
                        modifier = Modifier.size(22.dp)
                    )
                    Text(
                        text = "Settings",
                        modifier = Modifier.screenHeading(),
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold,
                        color = if (isDark) TextPrimaryDark else ForestGreenDark
                    )
                }

                // Small pill showing current theme
                Surface(
                    shape = RoundedCornerShape(12.dp),
                    color = if (isDark) SurfaceDarkVariant else MintBannerBg
                ) {
                    Text(
                        text = if (isDark) "Dark Theme" else "Light Theme",
                        fontSize = 12.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = if (isDark) ForestGreenLight else ForestGreenDark,
                        modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp)
                    )
                }
            }

            // --- 1. APPEARANCE SECTION ---
            SettingsSectionHeader(title = "Appearance", isDark = isDark)

            SettingsCard(isDark = isDark) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .switchRow(isDark) { onToggleDarkMode() }
                        .padding(16.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(14.dp),
                        modifier = Modifier.weight(1f)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(40.dp)
                                .clip(CircleShape)
                                .background(if (isDark) SurfaceDarkVariant else MintBannerBg),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = if (isDark) Icons.Filled.DarkMode else Icons.Filled.LightMode,
                                contentDescription = null,
                                tint = if (isDark) Color(0xFFFFD54F) else ForestGreen,
                                modifier = Modifier.size(22.dp)
                            )
                        }

                        Column {
                            Text(
                                text = "Dark Mode",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.onSurface
                            )
                            Text(
                                text = if (isDark) "Switch to clean light mode" else "Enable eye-friendly dark palette",
                                fontSize = 13.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }
                    }

                    Switch(
                        checked = isDark,
                        onCheckedChange = null,
                        colors = SwitchDefaults.colors(
                            checkedThumbColor = Color.White,
                            checkedTrackColor = if (isDark) SwitchTrackActiveDark else ForestGreenLight,
                            uncheckedThumbColor = Color.White,
                            uncheckedTrackColor = if (isDark) SwitchTrackInactiveDark else SwitchTrackInactive
                        )
                    )
                }
            }

            SettingsCard(isDark = isDark) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .switchRow(HighContrast.enabled) { next ->
                            HighContrast.enabled = next
                            ApiClient.setHighContrast(next)
                        }
                        .padding(16.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(14.dp),
                        modifier = Modifier.weight(1f)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(40.dp)
                                .clip(CircleShape)
                                .background(if (isDark) SurfaceDarkVariant else MintBannerBg),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = Icons.Filled.Contrast,
                                contentDescription = null,
                                tint = if (isDark) Color(0xFFFFD54F) else ForestGreen,
                                modifier = Modifier.size(22.dp)
                            )
                        }

                        Column {
                            Text(
                                text = "High Contrast",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.onSurface
                            )
                            Text(
                                text = "Stronger text, borders and switches",
                                fontSize = 13.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }
                    }

                    Switch(
                        checked = HighContrast.enabled,
                        onCheckedChange = null,
                        colors = SwitchDefaults.colors(
                            checkedThumbColor = Color.White,
                            checkedTrackColor = if (isDark) SwitchTrackActiveDark else ForestGreen,
                            uncheckedThumbColor = Color.White,
                            uncheckedTrackColor = if (isDark) SwitchTrackInactiveDark else SwitchTrackInactive
                        )
                    )
                }
            }

            // --- ACCESSIBILITY SECTION ---
            SettingsSectionHeader(title = "Accessibility", isDark = isDark)

            SettingsCard(isDark = isDark) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable(role = Role.Button) {
                            // Opens the phone's accessibility settings, where TalkBack is switched on
                            try {
                                context.startActivity(
                                    android.content.Intent(android.provider.Settings.ACTION_ACCESSIBILITY_SETTINGS)
                                        .addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                                )
                            } catch (e: android.content.ActivityNotFoundException) {
                                Toast.makeText(context, "Open your phone's Settings and search for TalkBack", Toast.LENGTH_LONG).show()
                            }
                        }
                        .padding(16.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(14.dp),
                        modifier = Modifier.weight(1f)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(40.dp)
                                .clip(CircleShape)
                                .background(if (isDark) SurfaceDarkVariant else MintBannerBg),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = Icons.Filled.Accessibility,
                                contentDescription = null,
                                tint = if (isDark) Color(0xFFFFD54F) else ForestGreen,
                                modifier = Modifier.size(22.dp)
                            )
                        }

                        Column {
                            Text(
                                text = "Screen Reader (TalkBack)",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.onSurface
                            )
                            Text(
                                text = "Opens your phone's accessibility settings, where you can turn TalkBack on and set up a volume key shortcut",
                                fontSize = 13.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }
                    }

                    Icon(
                        imageVector = Icons.Filled.ChevronRight,
                        contentDescription = null,
                        tint = if (isDark) TextSecondaryDark else TextSecondary,
                        modifier = Modifier.size(22.dp)
                    )
                }
            }

            // Simple Mode Toggle - Accessible strictly in Offline Mode
            if (!isSignedIn) {
                SettingsCard(isDark = isDark) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .switchRow(com.example.smarthydro.ui.SimpleModeState.offlineSimpleMode) { next ->
                                com.example.smarthydro.ui.SimpleModeState.setOfflineMode(next)
                            }
                            .padding(16.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(14.dp),
                            modifier = Modifier.weight(1f)
                        ) {
                            Box(
                                modifier = Modifier
                                    .size(40.dp)
                                    .clip(CircleShape)
                                    .background(if (isDark) SurfaceDarkVariant else MintBannerBg),
                                contentAlignment = Alignment.Center
                            ) {
                                Icon(
                                    imageVector = Icons.Filled.TouchApp,
                                    contentDescription = null,
                                    tint = if (isDark) Color(0xFFFFD54F) else ForestGreen,
                                    modifier = Modifier.size(22.dp)
                                )
                            }

                            Column {
                                Text(
                                    text = "Simple Mode",
                                    fontSize = 16.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                                Text(
                                    text = "Larger icons and direct controls",
                                    fontSize = 13.sp,
                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }
                        }

                        Switch(
                            checked = com.example.smarthydro.ui.SimpleModeState.offlineSimpleMode,
                            onCheckedChange = null,
                            modifier = Modifier.scale(0.8f),
                            colors = SwitchDefaults.colors(
                                checkedThumbColor = Color.White,
                                checkedTrackColor = if (isDark) SwitchTrackActiveDark else ForestGreen,
                                uncheckedThumbColor = Color.White,
                                uncheckedTrackColor = if (isDark) SwitchTrackInactiveDark else SwitchTrackInactive
                            )
                        )
                    }
                }
            }

            // --- 2. USER PROFILE & FULL NAME SECTION ---
            SettingsSectionHeader(title = "Profile & Account", isDark = isDark)

            SettingsCard(isDark = isDark) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(14.dp)
                ) {
                    // Account info header
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(44.dp)
                                .clip(CircleShape)
                                .background(ForestGreen),
                            contentAlignment = Alignment.Center
                        ) {
                            Text(
                                text = (ApiClient.getEffectiveDisplayName().firstOrNull() ?: 'U').uppercase(),
                                color = Color.White,
                                fontSize = 20.sp,
                                fontWeight = FontWeight.Bold
                            )
                        }

                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = ApiClient.getEffectiveDisplayName(),
                                fontSize = 16.sp,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onSurface
                            )
                            Text(
                                text = if (isSignedIn) {
                                    "${signedInEmail ?: "Signed in"} • Role: ${signedInRole ?: "Team Member"}"
                                } else {
                                    "Browsing offline mode"
                                },
                                fontSize = 13.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }
                    }

                    HorizontalDivider(color = if (isDark) CardDividerDark else CardDivider)

                    // Optional Full Name Field
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(
                            text = "Enter Full Name (optional)",
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Medium,
                            color = if (isDark) TextSecondaryDark else TextSecondary
                        )

                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            OutlinedTextField(
                                value = customFullNameInput,
                                onValueChange = {
                                    customFullNameInput = it
                                    isEditingFullName = true
                                },
                                placeholder = { Text("Enter Full Name (optional)") },
                                singleLine = true,
                                modifier = Modifier.weight(1f),
                                colors = settingsFieldColors(isDark),
                                shape = RoundedCornerShape(10.dp)
                            )

                            Button(
                                onClick = {
                                    ApiClient.setCustomFullName(customFullNameInput)
                                    isEditingFullName = false
                                    Toast.makeText(context, "Full name saved", Toast.LENGTH_SHORT).show()
                                },
                                colors = ButtonDefaults.buttonColors(
                                    containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                                    contentColor = Color.White
                                ),
                                shape = RoundedCornerShape(10.dp),
                                enabled = isEditingFullName || customFullNameInput != (ApiClient.getCustomFullName() ?: "")
                            ) {
                                Text("Save", fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
                            }
                        }

                        if (!ApiClient.getCustomFullName().isNullOrBlank()) {
                            TextButton(
                                onClick = {
                                    customFullNameInput = ""
                                    ApiClient.setCustomFullName(null)
                                    isEditingFullName = false
                                    Toast.makeText(context, "Full name cleared", Toast.LENGTH_SHORT).show()
                                },
                                contentPadding = PaddingValues(0.dp)
                            ) {
                                Text(
                                    text = "Reset to default name",
                                    fontSize = 12.sp,
                                    color = if (isDark) StatusCautionTextDark else StatusCautionText
                                )
                            }
                        }
                    }
                }
            }

            if (!isSignedIn) {
                // --- PROMINENT GO ONLINE BANNER (OFFLINE MODE) ---
                Surface(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(16.dp))
                        .border(
                            1.dp,
                            if (isDark) ForestGreenLight.copy(alpha = 0.35f) else CardBorder,
                            RoundedCornerShape(16.dp)
                        ),
                    color = if (isDark) SurfaceDarkVariant else MintBannerBg
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(18.dp),
                        verticalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            Box(
                                modifier = Modifier
                                    .size(36.dp)
                                    .clip(CircleShape)
                                    .background(if (isDark) PrimaryButtonBgDark else ForestGreen),
                                contentAlignment = Alignment.Center
                            ) {
                                Icon(
                                    imageVector = Icons.AutoMirrored.Filled.Login,
                                    contentDescription = null,
                                    tint = Color.White,
                                    modifier = Modifier.size(20.dp)
                                )
                            }
                            Column {
                                Text(
                                    text = "You're in Offline Mode",
                                    fontSize = 16.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                                )
                                Text(
                                    text = "Direct WiFi box control enabled",
                                    fontSize = 12.sp,
                                    color = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }
                        }

                        Text(
                            text = "Sign in with your team credentials to go online, access cloud synchronization, and receive real-time push alerts.",
                            fontSize = 13.sp,
                            color = if (isDark) TextSecondaryDark else TextSecondary,
                            lineHeight = 18.sp
                        )

                        Button(
                            onClick = onNavigateToLogin,
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(46.dp),
                            shape = RoundedCornerShape(10.dp),
                            colors = ButtonDefaults.buttonColors(
                                containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                                contentColor = Color.White
                            )
                        ) {
                            Icon(
                                imageVector = Icons.AutoMirrored.Filled.Login,
                                contentDescription = null,
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(modifier = Modifier.width(8.dp))
                            Text(
                                text = "Sign In to Go Online",
                                fontSize = 14.sp,
                                fontWeight = FontWeight.Bold
                            )
                        }
                    }
                }
            }

            // --- 3. SECURITY & ACCOUNT ACTION SECTION (ONLINE ONLY) ---
            if (isSignedIn) {
                SettingsSectionHeader(
                    title = "Security & Account",
                    isDark = isDark
                )

                SettingsCard(isDark = isDark) {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        SettingsActionRow(
                            icon = Icons.Filled.LockReset,
                            title = "Reset Password",
                            subtitle = "Request a verification code to update your account password",
                            isDark = isDark,
                            onClick = {
                                resetEmailInput = signedInEmail ?: ""
                                resetOtpCodeInput = ""
                                newPasswordInput = ""
                                confirmPasswordInput = ""
                                resetErrorMessage = null
                                resetDialogStep = 1
                                showPasswordResetDialog = true
                            }
                        )

                        HorizontalDivider(color = if (isDark) CardDividerDark else CardDivider)

                        SettingsActionRow(
                            icon = Icons.AutoMirrored.Filled.Logout,
                            title = "Sign Out",
                            subtitle = "Sign out from the SmartHydro cloud server",
                            isDark = isDark,
                            titleColor = if (isDark) StatusCautionTextDark else StatusCautionText,
                            onClick = { showLogoutConfirmation = true }
                        )
                    }
                }
            }

            // Extra bottom spacing so the action buttons sit comfortably above the phone's 3-button navigation bar
            Spacer(modifier = Modifier.height(36.dp))
        }
    }

    // --- DIALOG: PASSWORD RESET ---
    if (showPasswordResetDialog) {
        ResetPasswordDialog(
            initialEmail = resetEmailInput,
            isDark = isDark,
            isLoading = isResetLoading,
            errorMessage = resetErrorMessage,
            step = resetDialogStep,
            onDismiss = { showPasswordResetDialog = false },
            onRequestCode = { email ->
                resetEmailInput = email
                isResetLoading = true
                resetErrorMessage = null
                scope.launch {
                    try {
                        val res = ApiClient.service.forgotPassword(ForgotPasswordDto(email))
                        if (res.isSuccessful) {
                            Toast.makeText(context, "Reset code sent! Check your inbox.", Toast.LENGTH_LONG).show()
                            resetDialogStep = 2
                        } else {
                            resetErrorMessage = res.errorBody()?.string() ?: "Failed to send reset code."
                        }
                    } catch (e: Exception) {
                        resetErrorMessage = "Network error: unable to contact server."
                    } finally {
                        isResetLoading = false
                    }
                }
            },
            onResetPassword = { email, code, newPassword ->
                isResetLoading = true
                resetErrorMessage = null
                scope.launch {
                    try {
                        val res = ApiClient.service.resetPassword(
                            ResetPasswordDto(
                                email = email,
                                code = code,
                                newPassword = newPassword
                            )
                        )
                        if (res.isSuccessful) {
                            Toast.makeText(context, "Password reset successfully! Please sign in.", Toast.LENGTH_LONG).show()
                            showPasswordResetDialog = false
                            ApiClient.clearAuthToken()
                            onNavigateToLogin()
                        } else {
                            resetErrorMessage = res.errorBody()?.string() ?: "Password reset failed. Invalid or expired code."
                        }
                    } catch (e: Exception) {
                        resetErrorMessage = "Network error: unable to contact server."
                    } finally {
                        isResetLoading = false
                    }
                }
            }
        )
    }

    // --- DIALOG: SIGN OUT CONFIRMATION ---
    if (showLogoutConfirmation) {
        SignOutConfirmDialog(
            userEmail = signedInEmail,
            isDark = isDark,
            onDismiss = { showLogoutConfirmation = false },
            onConfirmSignOut = {
                showLogoutConfirmation = false
                ApiClient.clearAuthToken()
                ApiClient.setPreferOfflineMode(false)
                onNavigateToLogin()
            }
        )
    }
}

@Composable
private fun SettingsSectionHeader(title: String, isDark: Boolean) {
    Text(
        text = title,
        fontSize = 14.sp,
        fontWeight = FontWeight.Bold,
        color = if (isDark) TextSecondaryDark else ForestGreenDark,
        modifier = Modifier.padding(start = 4.dp, top = 4.dp)
    )
}

@Composable
private fun SettingsCard(
    isDark: Boolean,
    content: @Composable () -> Unit
) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardBorder, RoundedCornerShape(16.dp)),
        color = MaterialTheme.colorScheme.surface,
        content = content
    )
}

@Composable
private fun SettingsActionRow(
    icon: ImageVector,
    title: String,
    subtitle: String,
    isDark: Boolean,
    titleColor: Color = MaterialTheme.colorScheme.onSurface,
    onClick: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable { onClick() }
            .padding(16.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(CircleShape)
                .background(if (isDark) SurfaceDarkVariant else MintBannerBg),
            contentAlignment = Alignment.Center
        ) {
            Icon(
                imageVector = icon,
                contentDescription = title,
                tint = titleColor,
                modifier = Modifier.size(22.dp)
            )
        }

        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = title,
                fontSize = 15.sp,
                fontWeight = FontWeight.SemiBold,
                color = titleColor
            )
            Text(
                text = subtitle,
                fontSize = 13.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary
            )
        }

        Icon(
            imageVector = Icons.Filled.ChevronRight,
            contentDescription = null,
            tint = if (isDark) TextSecondaryDark else TextSecondary,
            modifier = Modifier.size(20.dp)
        )
    }
}

@Composable
private fun settingsFieldColors(isDark: Boolean) = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = ForestGreen,
    unfocusedBorderColor = if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.7f),
    focusedContainerColor = MaterialTheme.colorScheme.surface,
    unfocusedContainerColor = MaterialTheme.colorScheme.surface,
    focusedTextColor = MaterialTheme.colorScheme.onSurface,
    unfocusedTextColor = MaterialTheme.colorScheme.onSurface
)