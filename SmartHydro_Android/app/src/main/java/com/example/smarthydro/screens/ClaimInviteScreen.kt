package com.example.smarthydro.screens

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.data.CacheManager
import com.example.smarthydro.models.ClaimInviteCodeDto
import com.example.smarthydro.ui.errorAnnouncement
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

// A worker who was emailed an invite (site name, an 8-character access code, and a link)
// lands here on Android via the access-code path; the link instead opens the MVC web claim
// page. Successfully claiming creates the Worker account and signs them straight in.
@Composable
fun ClaimInviteScreen(
    onClaimed: () -> Unit,
    onNavigateToLogin: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    var email by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }
    var fullName by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var confirmPassword by remember { mutableStateOf("") }
    var isPasswordVisible by remember { mutableStateOf(false) }
    var isConfirmPasswordVisible by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText
    val dividerColor = if (isDark) CardDividerDark else CardDivider

    fun validate(): String? {
        if (email.trim().isEmpty()) return "Enter the email address your invite was sent to."
        if (code.trim().isEmpty()) return "Enter the access code from your invitation email."
        if (password.length < 8 || password.length > 128) return "Password must be at least 8 characters."
        if (password != confirmPassword) return "Passwords do not match."
        return null
    }

    fun submit() {
        val validationError = validate()
        if (validationError != null) {
            errorMessage = validationError
            return
        }
        errorMessage = null
        isLoading = true
        scope.launch {
            try {
                val response = ApiClient.service.claimInviteCode(
                    ClaimInviteCodeDto(
                        email = email.trim().lowercase(),
                        code = code.trim(),
                        newPassword = password,
                        fullName = fullName.trim().takeIf { it.isNotEmpty() }
                    )
                )
                val body = response.body()
                if (response.isSuccessful && body != null) {
                    CacheManager.onUserAuthenticated(body.email)
                    ApiClient.setAuthToken(body.token, body.expiresAt, body.email, body.role)
                    onClaimed()
                } else {
                    errorMessage = loginErrorMessage(
                        response = response,
                        unauthorizedMessage = "Invalid or expired access code.",
                        fallback = when (response.code()) {
                            409 -> "An account already exists for this email. Sign in instead, or enter your current password to join this site."
                            else -> "Invalid or expired access code. It may have been used already, expired, or been locked after too many wrong tries — ask your Operator to resend it."
                        }
                    )
                }
            } catch (e: Exception) {
                errorMessage = "Couldn't reach the server. Check your connection and try again."
            } finally {
                isLoading = false
            }
        }
    }

    Box(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Spacer(modifier = Modifier.height(40.dp))

            Box(
                modifier = Modifier
                    .size(108.dp)
                    .clip(CircleShape)
                    .background(ForestGreen.copy(alpha = if (isDark) 0.18f else 0.10f)),
                contentAlignment = Alignment.Center
            ) {
                Box(
                    modifier = Modifier
                        .size(84.dp)
                        .clip(CircleShape)
                        .background(ForestGreen),
                    contentAlignment = Alignment.Center
                ) {
                    androidx.compose.foundation.Image(
                        painter = painterResource(id = R.drawable.ic_smarthydro_logo),
                        contentDescription = "Smart Hydro Logo",
                        modifier = Modifier.size(64.dp)
                    )
                }
            }

            Spacer(modifier = Modifier.height(14.dp))

            Text(
                text = "Smart Hydro",
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                letterSpacing = 1.5.sp,
                color = ForestGreen
            )

            Spacer(modifier = Modifier.height(28.dp))

            Surface(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(20.dp),
                color = MaterialTheme.colorScheme.surface,
                border = BorderStroke(1.dp, if (isDark) CardBorderDark else dividerColor),
                shadowElevation = if (isDark) 0.dp else 2.dp
            ) {
                Column(
                    modifier = Modifier.padding(horizontal = 20.dp, vertical = 24.dp)
                ) {
                    Text(
                        text = "Join your team",
                        modifier = Modifier.screenHeading(),
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold,
                        color = primaryText
                    )

                    Spacer(modifier = Modifier.height(6.dp))

                    Text(
                        text = "Enter the email and access code from your invitation, then choose a password.",
                        fontSize = 13.sp,
                        lineHeight = 18.sp,
                        color = secondaryText
                    )

                    Spacer(modifier = Modifier.height(22.dp))

                    OutlinedTextField(
                        value = email,
                        onValueChange = { email = it; errorMessage = null },
                        label = { Text("Email address") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next),
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = authFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(14.dp))

                    OutlinedTextField(
                        value = code,
                        onValueChange = { code = it.uppercase(); errorMessage = null },
                        label = { Text("Access code") },
                        placeholder = { Text("e.g. HYDR-9X2W") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(
                            capitalization = KeyboardCapitalization.Characters,
                            keyboardType = KeyboardType.Ascii, imeAction = ImeAction.Next),
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = authFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(14.dp))

                    OutlinedTextField(
                        value = fullName,
                        onValueChange = { fullName = it; errorMessage = null },
                        label = { Text("Full name (optional)") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = authFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(14.dp))

                    OutlinedTextField(
                        value = password,
                        onValueChange = { password = it; errorMessage = null },
                        label = { Text("Password") },
                        placeholder = { Text("At least 8 characters") },
                        singleLine = true,
                        visualTransformation = if (isPasswordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Next),
                        trailingIcon = {
                            IconButton(onClick = { isPasswordVisible = !isPasswordVisible }) {
                                Icon(
                                    painter = painterResource(id = if (isPasswordVisible) R.drawable.ic_eye else R.drawable.ic_eye_slash),
                                    contentDescription = if (isPasswordVisible) "Hide password" else "Show password",
                                    tint = secondaryText,
                                    modifier = Modifier.size(20.dp)
                                )
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = authFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(14.dp))

                    OutlinedTextField(
                        value = confirmPassword,
                        onValueChange = { confirmPassword = it; errorMessage = null },
                        label = { Text("Confirm password") },
                        singleLine = true,
                        keyboardActions = KeyboardActions(onDone = { if (!isLoading) submit() }),
                        visualTransformation = if (isConfirmPasswordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                        trailingIcon = {
                            IconButton(onClick = { isConfirmPasswordVisible = !isConfirmPasswordVisible }) {
                                Icon(
                                    painter = painterResource(id = if (isConfirmPasswordVisible) R.drawable.ic_eye else R.drawable.ic_eye_slash),
                                    contentDescription = if (isConfirmPasswordVisible) "Hide password" else "Show password",
                                    tint = secondaryText,
                                    modifier = Modifier.size(20.dp)
                                )
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = authFieldColors(isDark)
                    )

                    errorMessage?.let { message ->
                        Spacer(modifier = Modifier.height(12.dp))
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(10.dp))
                                .errorAnnouncement()
                                .background(cautionText.copy(alpha = 0.10f))
                                .padding(horizontal = 14.dp, vertical = 10.dp)
                        ) {
                            Text(
                                text = message,
                                fontSize = 13.sp,
                                lineHeight = 18.sp,
                                color = cautionText
                            )
                        }
                    }

                    Spacer(modifier = Modifier.height(18.dp))

                    val canSubmit = email.isNotBlank() && code.isNotBlank() &&
                            password.isNotBlank() && confirmPassword.isNotBlank()

                    Button(
                        onClick = { submit() },
                        enabled = canSubmit && !isLoading,
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(52.dp),
                        shape = RoundedCornerShape(12.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                            contentColor = Color.White,
                            disabledContainerColor = (if (isDark) PrimaryButtonBgDark else ForestGreen).copy(alpha = 0.4f),
                            disabledContentColor = Color.White.copy(alpha = 0.8f)
                        )
                    ) {
                        if (isLoading) {
                            CircularProgressIndicator(
                                modifier = Modifier.size(22.dp),
                                color = Color.White,
                                strokeWidth = 2.dp
                            )
                        } else {
                            Text(text = "Join Team", fontSize = 16.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(20.dp))

            TextButton(onClick = onNavigateToLogin) {
                Text(
                    text = "Back to sign in",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Medium,
                    color = secondaryText
                )
            }

            Spacer(modifier = Modifier.height(32.dp))
        }
    }
}
