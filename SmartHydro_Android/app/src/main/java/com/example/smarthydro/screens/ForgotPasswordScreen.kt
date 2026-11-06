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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.models.ForgotPasswordDto
import com.example.smarthydro.ui.errorAnnouncement
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

private const val EMAIL_PATTERN = "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$"

@Composable
fun ForgotPasswordScreen(
    onCodeSent: (email: String) -> Unit,
    onNavigateToLogin: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    var email by remember { mutableStateOf("") }
    var isLoading by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText
    val dividerColor = if (isDark) CardDividerDark else CardDivider

    fun submit() {
        val trimmedEmail = email.trim()
        if (trimmedEmail.isEmpty() || !Regex(EMAIL_PATTERN).matches(trimmedEmail)) {
            errorMessage = "Enter a valid email address."
            return
        }
        errorMessage = null
        isLoading = true
        scope.launch {
            try {
                // The API always returns the same message
                val response = ApiClient.service.forgotPassword(ForgotPasswordDto(trimmedEmail))
                if (response.isSuccessful) {
                    onCodeSent(trimmedEmail.lowercase())
                } else {
                    errorMessage = extractAuthErrorMessage(response.errorBody()?.string())
                        ?: if (response.code() == 429) {
                            tooManyAttemptsMessage(response.headers()["Retry-After"])
                        } else {
                            "Could not send the code. Please try again."
                        }
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
                        text = "Forgot your password?",
                        modifier = Modifier.screenHeading(),
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold,
                        color = primaryText
                    )

                    Spacer(modifier = Modifier.height(6.dp))

                    Text(
                        text = "Enter your account email and we'll send you a 6-digit code to reset your password.",
                        fontSize = 13.sp,
                        lineHeight = 18.sp,
                        color = secondaryText
                    )

                    Spacer(modifier = Modifier.height(22.dp))

                    OutlinedTextField(
                        value = email,
                        onValueChange = { email = it; errorMessage = null },
                        label = { Text("Email") },
                        singleLine = true,
                        keyboardActions = KeyboardActions(onDone = { if (!isLoading && email.isNotBlank()) submit() }),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Done),
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = authFieldColors(isDark)
                    )

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

                    Button(
                        onClick = { submit() },
                        enabled = email.isNotBlank() && !isLoading,
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
                            Text(text = "Send code", fontSize = 16.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(20.dp))

            // Link back to sign in
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.Center
            ) {
                Text(
                    text = "Remembered it?",
                    fontSize = 14.sp,
                    color = secondaryText
                )
                TextButton(
                    onClick = onNavigateToLogin,
                    contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp)
                ) {
                    Text(
                        text = "Back to sign in",
                        fontSize = 14.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = if (isDark) AccentGreenDark else ForestGreen
                    )
                }
            }

            Spacer(modifier = Modifier.height(32.dp))
        }
    }
}

// Field colours shared by the auth screens
@Composable
internal fun authFieldColors(isDark: Boolean) = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = ForestGreen,
    unfocusedBorderColor = if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.5f),
    focusedLabelColor = ForestGreen,
    cursorColor = ForestGreen,
    focusedContainerColor = MaterialTheme.colorScheme.surface,
    unfocusedContainerColor = MaterialTheme.colorScheme.surface,
    focusedTextColor = MaterialTheme.colorScheme.onSurface,
    unfocusedTextColor = MaterialTheme.colorScheme.onSurface
)

// Rate-limit wording, using the Retry-After header
internal fun tooManyAttemptsMessage(retryAfterHeader: String?): String {
    val seconds = retryAfterHeader?.toLongOrNull()
    if (seconds == null || seconds <= 0) {
        return "Too many attempts. Please wait a few minutes and try again."
    }
    val minutes = (seconds + 59) / 60
    return "Too many attempts. Please try again in $minutes minute${if (minutes == 1L) "" else "s"}."
}

// Pulls the API's error body into plain text
internal fun extractAuthErrorMessage(body: String?): String? {
    if (body.isNullOrBlank()) return null
    return try {
        val obj = com.google.gson.JsonParser.parseString(body).asJsonObject
        obj.get("message")?.asString
    } catch (e: Exception) {
        null
    }
}