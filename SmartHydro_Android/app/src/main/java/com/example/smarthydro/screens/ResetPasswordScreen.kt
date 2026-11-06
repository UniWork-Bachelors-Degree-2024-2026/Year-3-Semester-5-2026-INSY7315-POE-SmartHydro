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
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.models.ForgotPasswordDto
import com.example.smarthydro.models.ResetPasswordDto
import com.example.smarthydro.ui.errorAnnouncement
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun ResetPasswordScreen(
    email: String,
    onResetComplete: () -> Unit,
    onNavigateToLogin: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    var code by remember { mutableStateOf("") }
    var newPassword by remember { mutableStateOf("") }
    var confirmPassword by remember { mutableStateOf("") }
    var isPasswordVisible by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    var isResending by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var resendStatus by remember { mutableStateOf<String?>(null) }

    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText
    val dividerColor = if (isDark) CardDividerDark else CardDivider

    fun validate(): String? {
        if (code.length != 6)
            return "Enter the 6-digit code from your email."
        if (newPassword.length < 8 || newPassword.length > 128)
            return "Password must be at least 8 characters."
        if (newPassword != confirmPassword)
            return "Passwords do not match."
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
                val response = ApiClient.service.resetPassword(
                    ResetPasswordDto(email = email, code = code.trim(), newPassword = newPassword)
                )
                if (response.isSuccessful) {
                    onResetComplete()
                } else {
                    errorMessage = extractAuthErrorMessage(response.errorBody()?.string()) ?: when (response.code()) {
                        409 -> "You can't reuse any of your last 5 passwords. Choose a different password."
                        429 -> "Too many attempts. Please try again shortly."
                        else -> "Invalid or expired code. Check the code and try again or request a new one."
                    }
                    code = ""
                }
            } catch (e: Exception) {
                errorMessage = "Couldn't reach the server. Check your connection and try again."
            } finally {
                isLoading = false
            }
        }
    }

    fun resend() {
        resendStatus = null
        isResending = true
        scope.launch {
            try {
                val response = ApiClient.service.forgotPassword(ForgotPasswordDto(email))
                resendStatus = if (response.isSuccessful) {
                    "A new code has been sent to your email. Check your spam folder if you don't see it."
                } else {
                    "Could not resend the code. Please try again."
                }
            } catch (e: Exception) {
                resendStatus = "Couldn't reach the server. Check your connection and try again."
            } finally {
                isResending = false
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
                        text = "Reset your password",
                        modifier = Modifier.screenHeading(),
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold,
                        color = primaryText
                    )

                    Spacer(modifier = Modifier.height(6.dp))

                    Text(
                        text = "Enter the 6-digit code we sent to",
                        fontSize = 13.sp,
                        lineHeight = 18.sp,
                        color = secondaryText
                    )
                    Text(
                        text = email,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = primaryText
                    )

                    Spacer(modifier = Modifier.height(22.dp))

                    OutlinedTextField(
                        value = code,
                        onValueChange = { input ->
                            code = input.filter { it.isDigit() }.take(6)
                            errorMessage = null
                        },
                        placeholder = {
                            Text(
                                text = "••••••",
                                modifier = Modifier.fillMaxWidth(),
                                textAlign = TextAlign.Center,
                                fontSize = 22.sp,
                                letterSpacing = 8.sp,
                                color = secondaryText.copy(alpha = 0.5f)
                            )
                        },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, imeAction = ImeAction.Next),
                        textStyle = LocalTextStyle.current.copy(
                            fontSize = 22.sp,
                            fontWeight = FontWeight.SemiBold,
                            letterSpacing = 8.sp,
                            textAlign = TextAlign.Center
                        ),
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = authFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(18.dp))

                    HorizontalDivider(color = dividerColor)

                    Spacer(modifier = Modifier.height(18.dp))

                    OutlinedTextField(
                        value = newPassword,
                        onValueChange = { newPassword = it; errorMessage = null },
                        label = { Text("New password") },
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
                        label = { Text("Confirm new password") },
                        singleLine = true,
                        keyboardActions = KeyboardActions(onDone = { if (!isLoading && code.length == 6) submit() }),
                        visualTransformation = PasswordVisualTransformation(),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
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

                    val canSubmit = code.length == 6 &&
                            newPassword.isNotBlank() &&
                            confirmPassword.isNotBlank()

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
                            Text(text = "Reset password", fontSize = 16.sp, fontWeight = FontWeight.Bold)
                        }
                    }

                    Spacer(modifier = Modifier.height(10.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.Center
                    ) {
                        Text(
                            text = "Didn't get it?",
                            fontSize = 13.sp,
                            color = secondaryText
                        )
                        TextButton(
                            onClick = { resend() },
                            enabled = !isResending,
                            contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp)
                        ) {
                            if (isResending) {
                                CircularProgressIndicator(
                                    modifier = Modifier.size(14.dp),
                                    color = if (isDark) AccentGreenDark else ForestGreen,
                                    strokeWidth = 2.dp
                                )
                            } else {
                                Text(
                                    text = "Resend code",
                                    fontSize = 13.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    color = if (isDark) AccentGreenDark else ForestGreen
                                )
                            }
                        }
                    }

                    resendStatus?.let { status ->
                        Text(
                            text = status,
                            fontSize = 12.sp,
                            lineHeight = 16.sp,
                            color = secondaryText,
                            textAlign = TextAlign.Center,
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            TextButton(onClick = onNavigateToLogin) {
                Text(
                    text = "Cancel",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Medium,
                    color = secondaryText
                )
            }

            Spacer(modifier = Modifier.height(32.dp))
        }
    }
}