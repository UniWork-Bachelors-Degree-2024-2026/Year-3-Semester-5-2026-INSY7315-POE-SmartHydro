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
import com.example.smarthydro.data.CacheManager
import com.example.smarthydro.models.RequestOtpDto
import com.example.smarthydro.models.ResendOtpDto
import com.example.smarthydro.models.VerifyOtpDto
import com.example.smarthydro.ui.errorAnnouncement
import com.example.smarthydro.ui.statusAnnouncement
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import retrofit2.Response

// The two-factor sign-in flow: email and password, then the emailed 6-digit code.
// Only the dev and research team sign in. Farmers use "Continue Offline" and control
// their boxes directly, so signing in is optional, never required.
private enum class LoginStep {
    CREDENTIALS,
    CODE
}

// How long to wait before asking for another sign-in code, the same as on the website
private const val RESEND_COOLDOWN_SECONDS = 60

@Composable
fun LoginScreen(
    onSignedIn: () -> Unit,
    onContinueWithoutSigningIn: () -> Unit,
    onNavigateToRegister: () -> Unit,
    onNavigateToForgotPassword: () -> Unit,
    onNavigateToClaimInvite: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    var step by remember { mutableStateOf(LoginStep.CREDENTIALS) }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }
    var challengeToken by remember { mutableStateOf("") }
    var isPasswordVisible by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var isResending by remember { mutableStateOf(false) }
    var resendStatus by remember { mutableStateOf<String?>(null) }

    // Seconds left before another code can be asked for, so the button cannot be tapped over and over
    var resendCooldown by remember { mutableStateOf(0) }

    LaunchedEffect(resendCooldown) {
        if (resendCooldown > 0) {
            delay(1000)
            resendCooldown -= 1
        }
    }

    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText
    val dividerColor = if (isDark) CardDividerDark else CardDivider

    fun sendCode() {
        errorMessage = null
        isLoading = true
        scope.launch {
            try {
                val response = ApiClient.service.requestOtp(RequestOtpDto(email.trim(), password))
                val body = response.body()
                if (response.isSuccessful && body != null) {
                    challengeToken = body.challengeToken
                    code = ""
                    resendStatus = null
                    resendCooldown = RESEND_COOLDOWN_SECONDS
                    step = LoginStep.CODE
                } else {
                    errorMessage = loginErrorMessage(
                        response = response,
                        unauthorizedMessage = "Invalid email or password.",
                        fallback = "Something went wrong. Please try again."
                    )
                }
            } catch (e: Exception) {
                errorMessage = "Couldn't reach the server. Check your connection and try again."
            } finally {
                isLoading = false
            }
        }
    }

    // Asks for a new code for this sign-in, the same way the registration screen does
    fun resendCode() {
        if (isResending || resendCooldown > 0) return

        errorMessage = null
        resendStatus = null
        isResending = true
        scope.launch {
            try {
                val response = ApiClient.service.resendOtp(ResendOtpDto(email.trim(), challengeToken))
                if (response.isSuccessful) {
                    code = ""
                    resendCooldown = RESEND_COOLDOWN_SECONDS
                    resendStatus = "A new code has been sent to your email. Check your spam folder if you don't see it."
                } else {
                    errorMessage = loginErrorMessage(
                        response = response,
                        unauthorizedMessage = "Could not resend the code. Please try again.",
                        fallback = "Could not resend the code. Please try again."
                    )
                }
            } catch (e: Exception) {
                errorMessage = "Couldn't reach the server. Check your connection and try again."
            } finally {
                isResending = false
            }
        }
    }

    fun verifyCode() {
        errorMessage = null
        isLoading = true
        scope.launch {
            try {
                val response = ApiClient.service.verifyOtp(VerifyOtpDto(email.trim(), code.trim(), challengeToken))
                val result = response.body()
                if (response.isSuccessful && result != null) {
                    CacheManager.onUserAuthenticated(result.email)
                    ApiClient.setAuthToken(result.token, result.expiresAt, result.email, result.role)
                    onSignedIn()
                } else {
                    errorMessage = loginErrorMessage(
                        response = response,
                        unauthorizedMessage = "Invalid or expired code.",
                        fallback = "Invalid or expired code."
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
            Spacer(modifier = Modifier.height(48.dp))

            // Soft halo ring around the logo badge
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
                        text = if (step == LoginStep.CREDENTIALS) "Sign In" else "Check your email",
                        modifier = Modifier.screenHeading(),
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold,
                        color = primaryText
                    )

                    Spacer(modifier = Modifier.height(6.dp))

                    Text(
                        text = if (step == LoginStep.CREDENTIALS) {
                            "For the development and research team. Farmers don't need this."
                        } else {
                            "Enter the 6-digit code we emailed to $email."
                        },
                        fontSize = 13.sp,
                        lineHeight = 18.sp,
                        color = secondaryText
                    )

                    Spacer(modifier = Modifier.height(22.dp))

                    if (step == LoginStep.CREDENTIALS) {
                        OutlinedTextField(
                            value = email,
                            onValueChange = { email = it; errorMessage = null },
                            label = { Text("Email") },
                            singleLine = true,
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next),
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(12.dp),
                            colors = loginFieldColors(isDark)
                        )

                        Spacer(modifier = Modifier.height(14.dp))

                        OutlinedTextField(
                            value = password,
                            onValueChange = { password = it; errorMessage = null },
                            label = { Text("Password") },
                            singleLine = true,
                            keyboardActions = KeyboardActions(onDone = { if (!isLoading && email.isNotBlank() && password.isNotBlank()) sendCode() }),
                            visualTransformation = if (isPasswordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                            trailingIcon = {
                                IconButton(onClick = { isPasswordVisible = !isPasswordVisible }) {
                                    Icon(
                                        // When hidden, show eye-slash. When revealed, show open eye
                                        painter = painterResource(id = if (isPasswordVisible) R.drawable.ic_eye else R.drawable.ic_eye_slash),
                                        contentDescription = if (isPasswordVisible) "Hide password" else "Show password",
                                        tint = secondaryText,
                                        modifier = Modifier.size(20.dp)
                                    )
                                }
                            },
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(12.dp),
                            colors = loginFieldColors(isDark)
                        )

                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.End
                        ) {
                            TextButton(
                                onClick = onNavigateToForgotPassword,
                                contentPadding = PaddingValues(horizontal = 4.dp, vertical = 0.dp)
                            ) {
                                Text(
                                    text = "Forgot password?",
                                    fontSize = 13.sp,
                                    fontWeight = FontWeight.Medium,
                                    color = if (isDark) AccentGreenDark else ForestGreen
                                )
                            }
                        }
                    } else {
                        OutlinedTextField(
                            value = code,
                            onValueChange = { input ->
                                val digits = input.filter { it.isDigit() }.take(6)
                                code = digits
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
                            keyboardActions = KeyboardActions(onDone = { if (!isLoading && code.length == 6) verifyCode() }),
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, imeAction = ImeAction.Done),
                            textStyle = LocalTextStyle.current.copy(
                                fontSize = 22.sp,
                                fontWeight = FontWeight.SemiBold,
                                letterSpacing = 8.sp,
                                textAlign = TextAlign.Center
                            ),
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(12.dp),
                            colors = loginFieldColors(isDark)
                        )

                        Spacer(modifier = Modifier.height(8.dp))

                        Text(
                            text = "Codes expire after 10 minutes. Check your spam folder if it hasn't arrived.",
                            fontSize = 12.sp,
                            lineHeight = 16.sp,
                            color = secondaryText,
                            textAlign = TextAlign.Center,
                            modifier = Modifier.fillMaxWidth()
                        )
                    }

                    // Error banner
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

                    val canSubmit = if (step == LoginStep.CREDENTIALS) {
                        email.isNotBlank() && password.isNotBlank()
                    } else {
                        code.length == 6
                    }

                    Button(
                        onClick = { if (step == LoginStep.CREDENTIALS) sendCode() else verifyCode() },
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
                            Text(
                                text = if (step == LoginStep.CREDENTIALS) "Send code" else "Verify",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.Bold
                            )
                        }
                    }

                    if (step == LoginStep.CODE) {
                        Spacer(modifier = Modifier.height(6.dp))
                        Row(
                            modifier = Modifier.align(Alignment.CenterHorizontally),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(
                                text = "Didn't get it? ",
                                fontSize = 14.sp,
                                color = secondaryText
                            )
                            TextButton(onClick = { resendCode() }, enabled = !isResending && !isLoading && resendCooldown == 0) {
                                Text(
                                    text = when {
                                        isResending -> "Sending…"
                                        resendCooldown > 0 -> "Resend code in ${resendCooldown}s"
                                        else -> "Resend code"
                                    },
                                    fontSize = 14.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    color = primaryText
                                )
                            }
                        }

                        resendStatus?.let { status ->
                            Text(
                                text = status,
                                modifier = Modifier
                                    .align(Alignment.CenterHorizontally)
                                    .statusAnnouncement(),
                                fontSize = 13.sp,
                                color = secondaryText,
                                textAlign = androidx.compose.ui.text.style.TextAlign.Center
                            )
                        }

                        TextButton(
                            onClick = {
                                step = LoginStep.CREDENTIALS
                                errorMessage = null
                                resendStatus = null
                                resendCooldown = 0
                                challengeToken = ""
                                code = ""
                            },
                            modifier = Modifier.align(Alignment.CenterHorizontally)
                        ) {
                            Text(
                                text = "Use a different email",
                                fontSize = 14.sp,
                                color = secondaryText
                            )
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(24.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                HorizontalDivider(modifier = Modifier.weight(1f), color = dividerColor)
                Text(
                    text = "or",
                    fontSize = 12.sp,
                    color = secondaryText,
                    modifier = Modifier.padding(horizontal = 12.dp)
                )
                HorizontalDivider(modifier = Modifier.weight(1f), color = dividerColor)
            }

            Spacer(modifier = Modifier.height(20.dp))

            // The default path for farmers. no account, no password.
            OutlinedButton(
                onClick = onContinueWithoutSigningIn,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(50.dp),
                shape = RoundedCornerShape(12.dp),
                border = BorderStroke(1.5.dp, if (isDark) AccentGreenDark else ForestGreen),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = if (isDark) TextPrimaryDark else ForestGreenDark)
            ) {
                Text(
                    text = "Continue Offline",
                    fontSize = 15.sp,
                    fontWeight = FontWeight.SemiBold
                )
            }

            // Link to register
            if (step == LoginStep.CREDENTIALS) {
                Spacer(modifier = Modifier.height(12.dp))
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.Center
                ) {
                    Text(
                        text = "Running a site?",
                        fontSize = 14.sp,
                        color = secondaryText
                    )
                    TextButton(
                        onClick = onNavigateToRegister,
                        contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp)
                    ) {
                        Text(
                            text = "Register as an Operator",
                            fontSize = 14.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = if (isDark) AccentGreenDark else ForestGreen
                        )
                    }
                }

                Spacer(modifier = Modifier.height(4.dp))

                TextButton(onClick = onNavigateToClaimInvite) {
                    Text(
                        text = "Have an invite code?",
                        fontSize = 14.sp,
                        color = secondaryText
                    )
                }
            }

            Spacer(modifier = Modifier.height(32.dp))
        }
    }
}

// Prefers the API's own message, falling back to one based on the status code.
// The rate limiter returns 429 with no body, only a Retry-After header.
internal fun loginErrorMessage(
    response: Response<*>,
    unauthorizedMessage: String,
    fallback: String
): String {
    extractAuthErrorMessage(response.errorBody()?.string())?.let { return it }

    return when (response.code()) {
        401 -> unauthorizedMessage
        403 -> "Your account can't sign in right now. Contact your administrator."
        429 -> {
            val seconds = response.headers()["Retry-After"]?.toLongOrNull()
            if (seconds != null && seconds > 0) {
                val minutes = (seconds + 59) / 60
                "Too many attempts. Please try again in $minutes minute${if (minutes == 1L) "" else "s"}."
            } else {
                "Too many attempts. Please wait a few minutes and try again."
            }
        }
        else -> fallback
    }
}

@Composable
private fun loginFieldColors(isDark: Boolean) = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = ForestGreen,
    unfocusedBorderColor = if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.5f),
    focusedLabelColor = ForestGreen,
    cursorColor = ForestGreen,
    focusedContainerColor = MaterialTheme.colorScheme.surface,
    unfocusedContainerColor = MaterialTheme.colorScheme.surface,
    focusedTextColor = MaterialTheme.colorScheme.onSurface,
    unfocusedTextColor = MaterialTheme.colorScheme.onSurface
)

// Reference Lists:
// FontAwesome, 2026a. Eye. [online] Available at: <https://fontawesome.com/icons/eye> [Accessed 27 September 2026].
// FontAwesome, 2026b. Eye slash. [online] Available at: <https://fontawesome.com/icons/eye-slash> [Accessed 27 September 2026].