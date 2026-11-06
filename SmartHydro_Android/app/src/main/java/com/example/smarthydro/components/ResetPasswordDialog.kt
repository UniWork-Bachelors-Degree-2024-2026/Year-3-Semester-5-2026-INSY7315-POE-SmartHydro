package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.smarthydro.ui.theme.*

/**
 * Custom dialog card for password reset.
 * Supports Step 1 (Request 6-digit code to email) and Step 2 (Submit code and new password).
 */
@Composable
fun ResetPasswordDialog(
    initialEmail: String = "",
    isDark: Boolean,
    isLoading: Boolean,
    errorMessage: String? = null,
    step: Int = 1,
    onDismiss: () -> Unit,
    onRequestCode: (email: String) -> Unit,
    onResetPassword: (email: String, code: String, newPassword: String) -> Unit
) {
    var emailInput by remember { mutableStateOf(initialEmail) }
    var codeInput by remember { mutableStateOf("") }
    var newPasswordInput by remember { mutableStateOf("") }
    var confirmPasswordInput by remember { mutableStateOf("") }
    var isNewPasswordVisible by remember { mutableStateOf(false) }
    var isConfirmPasswordVisible by remember { mutableStateOf(false) }
    var localValidationMessage by remember { mutableStateOf<String?>(null) }

    Dialog(onDismissRequest = { if (!isLoading) onDismiss() }) {
        Card(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 16.dp),
            shape = RoundedCornerShape(24.dp),
            colors = CardDefaults.cardColors(
                containerColor = if (isDark) SurfaceDark else SurfaceWhite
            ),
            border = androidx.compose.foundation.BorderStroke(
                1.dp,
                if (isDark) CardBorderDark else CardBorder
            ),
            elevation = CardDefaults.cardElevation(defaultElevation = 8.dp)
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                // Header Row
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp),
                        modifier = Modifier.weight(1f)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(40.dp)
                                .clip(CircleShape)
                                .background((if (isDark) ForestGreenLight else ForestGreen).copy(alpha = 0.15f)),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = Icons.Filled.LockReset,
                                contentDescription = null,
                                tint = if (isDark) ForestGreenLight else ForestGreen,
                                modifier = Modifier.size(20.dp)
                            )
                        }

                        Column {
                            Text(
                                text = if (step == 1) "Reset Password" else "Enter Code & Password",
                                fontWeight = FontWeight.Bold,
                                fontSize = 17.sp,
                                color = if (isDark) TextPrimaryDark else TextPrimary
                            )
                            Text(
                                text = if (step == 1) "Step 1 of 2: Verification" else "Step 2 of 2: New Password",
                                fontSize = 11.sp,
                                color = if (isDark) TextSecondaryDark else TextSecondary
                            )
                        }
                    }

                    IconButton(
                        onClick = onDismiss,
                        enabled = !isLoading,
                        modifier = Modifier
                            .size(32.dp)
                            .clip(CircleShape)
                            .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
                    ) {
                        Icon(
                            imageVector = Icons.Filled.Close,
                            contentDescription = "Close",
                            tint = if (isDark) TextSecondaryDark else TextSecondary,
                            modifier = Modifier.size(18.dp)
                        )
                    }
                }

                HorizontalDivider(
                    color = if (isDark) CardBorderDark else CardBorder,
                    thickness = 1.dp
                )

                // Error message banner
                val activeError = localValidationMessage ?: errorMessage
                if (activeError != null) {
                    Surface(
                        shape = RoundedCornerShape(10.dp),
                        color = if (isDark) StatusCautionBgDark else StatusCautionBg,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Row(
                            modifier = Modifier.padding(10.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Icon(
                                imageVector = Icons.Filled.ErrorOutline,
                                contentDescription = null,
                                tint = if (isDark) StatusCautionTextDark else StatusCautionText,
                                modifier = Modifier.size(16.dp)
                            )
                            Text(
                                text = activeError,
                                color = if (isDark) StatusCautionTextDark else StatusCautionText,
                                fontSize = 12.sp,
                                lineHeight = 16.sp
                            )
                        }
                    }
                }

                // Step 1: Email Input
                if (step == 1) {
                    Text(
                        text = "Enter your registered email address. We will send a 6-digit confirmation code to your inbox.",
                        fontSize = 13.sp,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        lineHeight = 18.sp
                    )

                    OutlinedTextField(
                        value = emailInput,
                        onValueChange = {
                            emailInput = it
                            localValidationMessage = null
                        },
                        label = { Text("Email Address") },
                        placeholder = { Text("e.g. name@smarthydro.com") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                        leadingIcon = {
                            Icon(
                                imageVector = Icons.Filled.Email,
                                contentDescription = null,
                                tint = if (isDark) ForestGreenLight else ForestGreen,
                                modifier = Modifier.size(20.dp)
                            )
                        },
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = ForestGreen,
                            focusedLabelColor = ForestGreen,
                            cursorColor = ForestGreen
                        ),
                        shape = RoundedCornerShape(12.dp),
                        modifier = Modifier.fillMaxWidth()
                    )
                } else {
                    // Step 2: Code & New Password Input
                    Text(
                        text = "A reset code was sent to ${emailInput.trim()}. Enter the code along with your new password below.",
                        fontSize = 13.sp,
                        color = if (isDark) TextSecondaryDark else TextSecondary,
                        lineHeight = 18.sp
                    )

                    OutlinedTextField(
                        value = codeInput,
                        onValueChange = {
                            codeInput = it
                            localValidationMessage = null
                        },
                        label = { Text("6-Digit Verification Code") },
                        placeholder = { Text("e.g. 123456") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Next),
                        leadingIcon = {
                            Icon(
                                imageVector = Icons.Filled.Pin,
                                contentDescription = null,
                                tint = if (isDark) ForestGreenLight else ForestGreen,
                                modifier = Modifier.size(20.dp)
                            )
                        },
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = ForestGreen,
                            focusedLabelColor = ForestGreen,
                            cursorColor = ForestGreen
                        ),
                        shape = RoundedCornerShape(12.dp),
                        modifier = Modifier.fillMaxWidth()
                    )

                    OutlinedTextField(
                        value = newPasswordInput,
                        onValueChange = {
                            newPasswordInput = it
                            localValidationMessage = null
                        },
                        label = { Text("New Password") },
                        singleLine = true,
                        visualTransformation = if (isNewPasswordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                        leadingIcon = {
                            Icon(
                                imageVector = Icons.Filled.Lock,
                                contentDescription = null,
                                tint = if (isDark) ForestGreenLight else ForestGreen,
                                modifier = Modifier.size(20.dp)
                            )
                        },
                        trailingIcon = {
                            IconButton(onClick = { isNewPasswordVisible = !isNewPasswordVisible }) {
                                Icon(
                                    imageVector = if (isNewPasswordVisible) Icons.Filled.VisibilityOff else Icons.Filled.Visibility,
                                    contentDescription = if (isNewPasswordVisible) "Hide password" else "Show password",
                                    tint = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }
                        },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Next),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = ForestGreen,
                            focusedLabelColor = ForestGreen,
                            cursorColor = ForestGreen
                        ),
                        shape = RoundedCornerShape(12.dp),
                        modifier = Modifier.fillMaxWidth()
                    )

                    OutlinedTextField(
                        value = confirmPasswordInput,
                        onValueChange = {
                            confirmPasswordInput = it
                            localValidationMessage = null
                        },
                        label = { Text("Confirm New Password") },
                        singleLine = true,
                        visualTransformation = if (isConfirmPasswordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                        leadingIcon = {
                            Icon(
                                imageVector = Icons.Filled.Lock,
                                contentDescription = null,
                                tint = if (isDark) ForestGreenLight else ForestGreen,
                                modifier = Modifier.size(20.dp)
                            )
                        },
                        trailingIcon = {
                            IconButton(onClick = { isConfirmPasswordVisible = !isConfirmPasswordVisible }) {
                                Icon(
                                    imageVector = if (isConfirmPasswordVisible) Icons.Filled.VisibilityOff else Icons.Filled.Visibility,
                                    contentDescription = if (isConfirmPasswordVisible) "Hide password" else "Show password",
                                    tint = if (isDark) TextSecondaryDark else TextSecondary
                                )
                            }
                        },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = ForestGreen,
                            focusedLabelColor = ForestGreen,
                            cursorColor = ForestGreen
                        ),
                        shape = RoundedCornerShape(12.dp),
                        modifier = Modifier.fillMaxWidth()
                    )
                }

                HorizontalDivider(
                    color = if (isDark) CardBorderDark else CardBorder,
                    thickness = 1.dp
                )

                // Buttons: Cancel & Submit (identical height and padding)
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedButton(
                        onClick = onDismiss,
                        enabled = !isLoading,
                        shape = RoundedCornerShape(12.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp),
                        border = BorderStroke(1.dp, if (isDark) CardBorderDark else CardBorder),
                        colors = ButtonDefaults.outlinedButtonColors(
                            contentColor = if (isDark) TextPrimaryDark else TextPrimary
                        ),
                        modifier = Modifier
                            .weight(1f)
                            .height(48.dp)
                    ) {
                        Text(
                            text = "Cancel",
                            fontWeight = FontWeight.SemiBold,
                            color = if (isDark) TextPrimaryDark else TextPrimary
                        )
                    }

                    Button(
                        onClick = {
                            if (step == 1) {
                                if (emailInput.isBlank()) {
                                    localValidationMessage = "Please enter your email address."
                                    return@Button
                                }
                                onRequestCode(emailInput.trim())
                            } else {
                                if (codeInput.isBlank() || newPasswordInput.isBlank()) {
                                    localValidationMessage = "Please provide both code and new password."
                                    return@Button
                                }
                                if (newPasswordInput != confirmPasswordInput) {
                                    localValidationMessage = "Passwords do not match."
                                    return@Button
                                }
                                if (newPasswordInput.length < 6) {
                                    localValidationMessage = "Password should be at least 6 characters."
                                    return@Button
                                }
                                onResetPassword(emailInput.trim(), codeInput.trim(), newPasswordInput)
                            }
                        },
                        enabled = !isLoading,
                        shape = RoundedCornerShape(12.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                            contentColor = Color.White
                        ),
                        modifier = Modifier
                            .weight(1f)
                            .height(48.dp)
                    ) {
                        if (isLoading) {
                            CircularProgressIndicator(
                                color = Color.White,
                                modifier = Modifier.size(18.dp),
                                strokeWidth = 2.dp
                            )
                        } else {
                            Text(
                                text = if (step == 1) "Send Code" else "Reset Password",
                                fontWeight = FontWeight.Bold,
                                color = Color.White
                            )
                        }
                    }
                }
            }
        }
    }
}
