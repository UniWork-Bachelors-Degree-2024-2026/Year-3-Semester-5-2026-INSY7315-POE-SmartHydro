package com.example.smarthydro.screens

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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.models.ResendRegistrationCodeDto
import com.example.smarthydro.models.VerifyRegistrationDto
import com.example.smarthydro.ui.errorAnnouncement
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

@Composable
fun RegisterVerifyScreen(
    email: String,
    verificationToken: String,
    onVerified: () -> Unit,
    onCancel: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    var code by remember { mutableStateOf("") }
    var isLoading by remember { mutableStateOf(false) }
    var isResending by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var resendStatus by remember { mutableStateOf<String?>(null) }

    fun verify() {
        errorMessage = null
        isLoading = true
        scope.launch {
            try {
                val response = ApiClient.service.verifyRegistration(
                    VerifyRegistrationDto(email, verificationToken, code.trim())
                )
                if (response.isSuccessful) {
                    onVerified()
                } else {
                    errorMessage = "Invalid or expired code. Check the code and try again or request a new one."
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
                val response = ApiClient.service.resendRegistrationCode(
                    ResendRegistrationCodeDto(email, verificationToken)
                )
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
                .padding(horizontal = 28.dp)
                .verticalScroll(rememberScrollState()),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Spacer(modifier = Modifier.height(56.dp))

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

            Spacer(modifier = Modifier.height(20.dp))

            Text(
                text = "Verify your email",
                modifier = Modifier.screenHeading(),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )

            Spacer(modifier = Modifier.height(6.dp))

            Text(
                text = "Enter the 6-digit code we sent to",
                fontSize = 13.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                textAlign = TextAlign.Center
            )
            Text(
                text = email,
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )

            Spacer(modifier = Modifier.height(28.dp))

            OutlinedTextField(
                value = code,
                onValueChange = { if (it.length <= 6) { code = it; errorMessage = null } },
                label = { Text("6-digit code") },
                singleLine = true,
                keyboardActions = KeyboardActions(onDone = { if (!isLoading && code.length == 6) verify() }),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, imeAction = ImeAction.Done),
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(12.dp),
                colors = registerVerifyFieldColors(isDark)
            )

            errorMessage?.let { message ->
                Spacer(modifier = Modifier.height(10.dp))
                Text(
                    text = message,
                    modifier = Modifier.errorAnnouncement(),
                    fontSize = 13.sp,
                    color = if (isDark) StatusCautionTextDark else StatusCautionText,
                    textAlign = TextAlign.Center
                )
            }

            Spacer(modifier = Modifier.height(20.dp))

            Button(
                onClick = { verify() },
                enabled = code.length == 6 && !isLoading,
                modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                    contentColor = Color.White
                )
            ) {
                if (isLoading) {
                    CircularProgressIndicator(modifier = Modifier.size(22.dp), color = Color.White, strokeWidth = 2.dp)
                } else {
                    Text(text = "Verify Email", fontSize = 16.sp, fontWeight = FontWeight.Bold)
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            TextButton(onClick = onCancel) {
                Text(text = "Cancel", color = if (isDark) TextSecondaryDark else TextSecondary)
            }

            Spacer(modifier = Modifier.height(16.dp))

            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = "Didn't get it? ",
                    fontSize = 13.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary
                )
                TextButton(onClick = { resend() }, enabled = !isResending) {
                    Text(
                        text = "Resend code",
                        fontSize = 13.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = if (isDark) AccentGreenDark else ForestGreenDark
                    )
                }
            }

            resendStatus?.let { status ->
                Text(
                    text = status,
                    fontSize = 12.sp,
                    color = if (isDark) TextSecondaryDark else TextSecondary,
                    textAlign = TextAlign.Center
                )
            }

            Text(
                text = "Expires in 10 minutes. Check your spam folder",
                fontSize = 12.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                textAlign = TextAlign.Center
            )

            Spacer(modifier = Modifier.height(16.dp))

            Text(
                text = "Once your email is verified, your registration is sent to an administrator for review. " +
                        "Already have an account? Cancel and sign in instead.",
                fontSize = 12.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                textAlign = TextAlign.Center
            )

            Spacer(modifier = Modifier.height(24.dp))
        }
    }
}
// Field Colours
@Composable
private fun registerVerifyFieldColors(isDark: Boolean) = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = ForestGreen,
    unfocusedBorderColor = if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.7f),
    focusedContainerColor = MaterialTheme.colorScheme.surface,
    unfocusedContainerColor = MaterialTheme.colorScheme.surface,
    focusedTextColor = MaterialTheme.colorScheme.onSurface,
    unfocusedTextColor = MaterialTheme.colorScheme.onSurface
)