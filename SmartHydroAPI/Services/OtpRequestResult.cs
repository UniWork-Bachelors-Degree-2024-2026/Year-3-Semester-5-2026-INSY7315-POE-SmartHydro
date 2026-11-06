namespace SmartHydroAPI.Services
{
    public enum OtpRequestStatus
    {
        Sent,
        InvalidCredentials,
        LockedOut,
        PendingApproval,
        Disabled
    }

    public sealed record OtpRequestResult(OtpRequestStatus Status, TimeSpan RetryAfter, string ChallengeToken)
    {
        // Builds the result returned once the password is checked, carrying the challenge token the client must send back with the code
        public static OtpRequestResult Sent(string challengeToken) => new(OtpRequestStatus.Sent, TimeSpan.Zero, challengeToken);

        public static OtpRequestResult InvalidCredentials() => new(OtpRequestStatus.InvalidCredentials, TimeSpan.Zero, string.Empty);

        public static OtpRequestResult LockedOut(TimeSpan retryAfter) => new(OtpRequestStatus.LockedOut, retryAfter, string.Empty);

        public static OtpRequestResult PendingApproval() => new(OtpRequestStatus.PendingApproval, TimeSpan.Zero, string.Empty);

        // Builds the result returned when the password is right but an administrator has disabled the account
        public static OtpRequestResult Disabled() => new(OtpRequestStatus.Disabled, TimeSpan.Zero, string.Empty);
    }

    public enum PasswordResetStatus
    {
        Succeeded,
        InvalidToken,
        PasswordReused
    }

    public enum RegisterStatus
    {
        CodeSent,
        Failed
    }

    public sealed record RegistrationStartResult(RegisterStatus Status, string VerificationToken)
    {
        // Builds the result returned whenever a sign-up is accepted, whether or not a code was really sent, carrying the token the client must send back to verify
        public static RegistrationStartResult CodeSent(string verificationToken) => new(RegisterStatus.CodeSent, verificationToken);

        // Builds the result returned when the sign-up could not be queued
        public static RegistrationStartResult Failed() => new(RegisterStatus.Failed, string.Empty);
    }

    public enum RegistrationVerifyStatus
    {
        PendingReview,
        InvalidCode,
        Failed
    }
}