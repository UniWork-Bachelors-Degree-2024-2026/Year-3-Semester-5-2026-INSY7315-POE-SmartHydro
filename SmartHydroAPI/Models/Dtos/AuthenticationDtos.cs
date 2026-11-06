using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    public class RequestOtpDto
    {
        private string _email = string.Empty;

        [Required, EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        [Required, MaxLength(128)]
        public string Password { get; set; } = string.Empty;
    }

    public class RequestOtpResponseDto
    {
        public string Message { get; set; } = string.Empty;

        public string ChallengeToken { get; set; } = string.Empty;
    }

    public class ResendOtpDto
    {
        private string _email = string.Empty;
        private string _challengeToken = string.Empty;

        [Required, EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        [Required, MaxLength(128)]
        public string ChallengeToken
        {
            get => _challengeToken;
            set => _challengeToken = value?.Trim() ?? string.Empty;
        }
    }

    public class VerifyOtpDto
    {
        private string _email = string.Empty;
        private string _code = string.Empty;
        private string _challengeToken = string.Empty;

        [Required, EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        [Required, MaxLength(6), MinLength(6)]
        public string Code
        {
            get => _code;
            set => _code = value?.Trim() ?? string.Empty;
        }

        [Required, MaxLength(128)]
        public string ChallengeToken
        {
            get => _challengeToken;
            set => _challengeToken = value?.Trim() ?? string.Empty;
        }

        // True when the person ticked Remember me, which asks for a longer-lived token
        public bool RememberMe { get; set; }
    }

    public class ForgotPasswordDto
    {
        private string _email = string.Empty;

        [Required, EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }
    }

    public class VerifyResetCodeDto
    {
        private string _email = string.Empty;
        private string _code = string.Empty;

        [Required, EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        [Required, MaxLength(6), MinLength(6)]
        public string Code
        {
            get => _code;
            set => _code = value?.Trim() ?? string.Empty;
        }
    }

    public class VerifyResetCodeResponseDto
    {
        public string ResetToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class ResetPasswordDto : IValidatableObject
    {
        private string _email = string.Empty;
        private string _resetToken = string.Empty;
        private string _code = string.Empty;

        [Required, EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        [MaxLength(128)]
        public string ResetToken
        {
            get => _resetToken;
            set => _resetToken = value?.Trim() ?? string.Empty;
        }

        [MaxLength(6)]
        public string Code
        {
            get => _code;
            set => _code = value?.Trim() ?? string.Empty;
        }

        [Required, MinLength(8), MaxLength(128)]
        public string NewPassword { get; set; } = string.Empty;

        // Requires either the reset token from the web flow or the 6-digit emailed code the Android app sends
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!string.IsNullOrWhiteSpace(ResetToken))
                yield break;

            if (Code.Length == 6 && Code.All(char.IsDigit))
                yield break;

            yield return new ValidationResult("Enter the 6-digit code from your email.", new[] { nameof(Code) });
        }
    }

    public class AuthResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class RegisterDto
    {
        private string _email = string.Empty;

        private const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [RegularExpression(EmailPattern, ErrorMessage = "Enter a valid email address.")]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        [Required(ErrorMessage = "Enter a password.")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        [MaxLength(128, ErrorMessage = "Password must be 128 characters or fewer.")]
        public string Password { get; set; } = string.Empty;

        private string _fullName = string.Empty;

        [Required(ErrorMessage = "Enter your full name.")]
        [MaxLength(100, ErrorMessage = "Full name must be 100 characters or fewer.")]
        public string FullName
        {
            get => _fullName;
            set => _fullName = value?.Trim() ?? string.Empty;
        }

        private string _farmName = string.Empty;

        [Required(ErrorMessage = "Enter a name for your starting site.")]
        [MaxLength(60, ErrorMessage = "Starting site name must be 60 characters or fewer.")]
        public string FarmName
        {
            get => _farmName;
            set => _farmName = value?.Trim() ?? string.Empty;
        }

        private string? _role;

        [MaxLength(20)]
        public string? Role
        {
            get => _role;
            set => _role = value?.Trim();
        }
    }

    public class VerifyRegistrationDto
    {
        private string _email = string.Empty;
        private string _verificationToken = string.Empty;
        private string _code = string.Empty;

        [Required, EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        [Required, MaxLength(128)]
        public string VerificationToken
        {
            get => _verificationToken;
            set => _verificationToken = value?.Trim() ?? string.Empty;
        }

        [Required, MaxLength(6), MinLength(6), RegularExpression("^[0-9]{6}$")]
        public string Code
        {
            get => _code;
            set => _code = value?.Trim() ?? string.Empty;
        }
    }

    public class ResendRegistrationCodeDto
    {
        private string _email = string.Empty;
        private string _verificationToken = string.Empty;

        [Required, EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        [Required, MaxLength(128)]
        public string VerificationToken
        {
            get => _verificationToken;
            set => _verificationToken = value?.Trim() ?? string.Empty;
        }
    }
}