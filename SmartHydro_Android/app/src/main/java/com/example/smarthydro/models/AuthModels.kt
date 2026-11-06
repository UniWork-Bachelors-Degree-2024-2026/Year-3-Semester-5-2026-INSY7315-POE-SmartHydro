package com.example.smarthydro.models

import com.google.gson.annotations.SerializedName

// 1. AUTHENTICATION MODELS (/api/Authentication)

// DTO for requesting an OTP sent to the user's email address.
// The API checks the password before it sends a code, so both are required here.
data class RequestOtpDto(
    @SerializedName("email") val email: String,
    @SerializedName("password") val password: String
)

// Response from request-otp, a challenge token ties the emailed code to this specific sign-in attempt
data class RequestOtpResponseDto(
    @SerializedName("message") val message: String,
    @SerializedName("challengeToken") val challengeToken: String
)

// DTO for submitting the 6-digit one-time code, the email and the challenge token
data class VerifyOtpDto(
    @SerializedName("email") val email: String,
    @SerializedName("code") val code: String,
    @SerializedName("challengeToken") val challengeToken: String
)

// DTO received upon successful verification containing the JWT bearer token
data class AuthResponseDto(
    @SerializedName("token") val token: String,
    @SerializedName("expiresAt") val expiresAt: String,
    @SerializedName("email") val email: String,
    @SerializedName("role") val role: String? = null
)

// DTO for resending an OTP code, tied to the same challenge token as the original request
data class ResendOtpDto(
    @SerializedName("email") val email: String,
    @SerializedName("challengeToken") val challengeToken: String
)

// DTO for requesting a password reset email
data class ForgotPasswordDto(
    @SerializedName("email") val email: String
)

// DTO for submitting a password reset code and new password
data class ResetPasswordDto(
    @SerializedName("email") val email: String,
    @SerializedName("code") val code: String,
    @SerializedName("newPassword") val newPassword: String
)

// 2. REGISTRATION MODELS

// DTO for registering as a farm Operator
data class RegisterDto(
    @SerializedName("fullName") val fullName: String,
    @SerializedName("email") val email: String,
    @SerializedName("password") val password: String,
    @SerializedName("farmName") val farmName: String
)

// Response from register. a verification token ties the emailed code to this specific sign-up
data class RegisterResponseDto(
    @SerializedName("message") val message: String,
    @SerializedName("verificationToken") val verificationToken: String
)

// DTO for confirming a sign-up with the emailed 6-digit code
data class VerifyRegistrationDto(
    @SerializedName("email") val email: String,
    @SerializedName("verificationToken") val verificationToken: String,
    @SerializedName("code") val code: String
)

data class VerifyRegistrationResponseDto(
    @SerializedName("message") val message: String,
    @SerializedName("status") val status: String
)

// DTO for requesting a new verification code for a sign-up already in progress
data class ResendRegistrationCodeDto(
    @SerializedName("email") val email: String,
    @SerializedName("verificationToken") val verificationToken: String
)

// 3. WORKER INVITE MODELS

// DTO for claiming a worker invitation with the 8-character access code emailed by the
// Operator who invited them. Creates the Worker account and returns a JWT, same as sign-in.
data class ClaimInviteCodeDto(
    @SerializedName("email") val email: String,
    @SerializedName("code") val code: String,
    @SerializedName("newPassword") val newPassword: String,
    @SerializedName("fullName") val fullName: String? = null
)