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
import com.example.smarthydro.models.RegisterDto
import com.example.smarthydro.ui.errorAnnouncement
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*
import kotlinx.coroutines.launch

private const val EMAIL_PATTERN = "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$"
private const val MAX_FULL_NAME_LENGTH = 100

@Composable
fun RegisterScreen(
    onCodeSent: (email: String, verificationToken: String) -> Unit,
    onNavigateToLogin: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current
    val scope = rememberCoroutineScope()

    var fullName by remember { mutableStateOf("") }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var confirmPassword by remember { mutableStateOf("") }
    var farmName by remember { mutableStateOf("") }
    var isPasswordVisible by remember { mutableStateOf(false) }
    var isConfirmPasswordVisible by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }

    val primaryText = if (isDark) TextPrimaryDark else ForestGreenDark
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary
    val cautionText = if (isDark) StatusCautionTextDark else StatusCautionText
    val dividerColor = if (isDark) CardDividerDark else CardDivider

    fun validate(): String? {
        val trimmedFullName = fullName.trim()
        if (trimmedFullName.isEmpty()) {
            return "Enter your full name."
        }
        if (trimmedFullName.length > MAX_FULL_NAME_LENGTH) {
            return "Full name must be 100 characters or fewer."
        }
        val trimmedEmail = email.trim()
        if (trimmedEmail.isEmpty() || !Regex(EMAIL_PATTERN).matches(trimmedEmail)) {
            return "Enter a valid email address."
        }
        if (password.length < 8 || password.length > 128) {
            return "Password must be at least 8 characters."
        }
        if (confirmPassword != password) {
            return "Passwords do not match."
        }
        val trimmedFarmName = farmName.trim()
        if (trimmedFarmName.isEmpty()) {
            return "Enter a name for your starting site."
        }
        if (trimmedFarmName.length > 60) {
            return "Starting site name must be 60 characters or fewer."
        }
        return null
    }

    fun submit() {
        val validationError = validate()
        if (validationError != null) {
            errorMessage = validationError
            return
        }
        val trimmedEmail = email.trim().lowercase()
        errorMessage = null
        isLoading = true
        scope.launch {
            try {
                val response = ApiClient.service.registerOperator(
                    RegisterDto(
                        fullName = fullName.trim(),
                        email = trimmedEmail,
                        password = password,
                        farmName = farmName.trim()
                    )
                )
                val body = response.body()
                if (response.isSuccessful && body != null) {
                    onCodeSent(trimmedEmail, body.verificationToken)
                } else {
                    errorMessage = extractAuthErrorMessage(response.errorBody()?.string())
                        ?: "Could not submit your registration. Please try again."
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
                        text = "Register your site",
                        modifier = Modifier.screenHeading(),
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold,
                        color = primaryText
                    )

                    Spacer(modifier = Modifier.height(6.dp))

                    Text(
                        text = "Sign up as a site Operator. We'll email you a code to verify your address, then an administrator will review your request before you can log in.",
                        fontSize = 13.sp,
                        lineHeight = 18.sp,
                        color = secondaryText
                    )

                    Spacer(modifier = Modifier.height(22.dp))

                    // Fullname Field
                    OutlinedTextField(
                        value = fullName,
                        onValueChange = { fullName = it.take(MAX_FULL_NAME_LENGTH); errorMessage = null },
                        label = { Text("Full name") },
                        placeholder = { Text("e.g. Jane Doe") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Words, imeAction = ImeAction.Next),
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = registerFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(14.dp))
                    // Email Field
                    OutlinedTextField(
                        value = email,
                        onValueChange = { email = it; errorMessage = null },
                        label = { Text("Email address") },
                        placeholder = { Text("e.g. you@smarthydro.com") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next),
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = registerFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(14.dp))
                    // Password Field
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
                        colors = registerFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(14.dp))
                    // Confirm Password Field
                    OutlinedTextField(
                        value = confirmPassword,
                        onValueChange = { confirmPassword = it; errorMessage = null },
                        label = { Text("Confirm password") },
                        placeholder = { Text("Re-enter your password") },
                        singleLine = true,
                        visualTransformation = if (isConfirmPasswordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Next),
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
                        colors = registerFieldColors(isDark)
                    )

                    Spacer(modifier = Modifier.height(14.dp))
                    // Site Name Field
                    OutlinedTextField(
                        value = farmName,
                        onValueChange = { farmName = it; errorMessage = null },
                        label = { Text("Starting site") },
                        placeholder = { Text("e.g. Emeris NMB") },
                        supportingText = {
                            Text(
                                text = "Your first site. You can create more once you're logged in.",
                                color = secondaryText
                            )
                        },
                        singleLine = true,
                        keyboardActions = KeyboardActions(onDone = { if (!isLoading) submit() }),
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(12.dp),
                        colors = registerFieldColors(isDark)
                    )

                    // Error banner
                    errorMessage?.let { message ->
                        Spacer(modifier = Modifier.height(8.dp))
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

                    val canSubmit = fullName.isNotBlank() &&
                            email.isNotBlank() &&
                            password.isNotBlank() &&
                            confirmPassword.isNotBlank() &&
                            farmName.isNotBlank()

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
                            Text(text = "Continue", fontSize = 16.sp, fontWeight = FontWeight.Bold)
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
                    text = "Already have an account?",
                    fontSize = 14.sp,
                    color = secondaryText
                )
                TextButton(
                    onClick = onNavigateToLogin,
                    contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp)
                ) {
                    Text(
                        text = "Sign in",
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

// Field colours
@Composable
private fun registerFieldColors(isDark: Boolean) = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = ForestGreen,
    unfocusedBorderColor = if (isDark) CardBorderDark else ForestGreen.copy(alpha = 0.5f),
    focusedLabelColor = ForestGreen,
    cursorColor = ForestGreen,
    focusedContainerColor = MaterialTheme.colorScheme.surface,
    unfocusedContainerColor = MaterialTheme.colorScheme.surface,
    focusedTextColor = MaterialTheme.colorScheme.onSurface,
    unfocusedTextColor = MaterialTheme.colorScheme.onSurface
)