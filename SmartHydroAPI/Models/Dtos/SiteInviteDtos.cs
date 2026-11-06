using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    public sealed class CreateSiteInviteDto
    {
        private string _email = string.Empty;

        [Required, EmailAddress, MaxLength(255)]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim().ToLowerInvariant() ?? string.Empty;
        }
    }

    public sealed class TeamMemberDto
    {
        public Guid SiteId { get; set; }
        public int? UserId { get; set; }
        public string SiteName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime Since { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public sealed class SiteInviteResponseDto
    {
        public string Message { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public sealed class ClaimInviteWebDto
    {
        private string _email = string.Empty;
        private string _token = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [MaxLength(255)]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        [Required(ErrorMessage = "The invitation link is incomplete.")]
        [MaxLength(128, ErrorMessage = "The invitation link is invalid.")]
        public string Token
        {
            get => _token;
            set => _token = value?.Trim() ?? string.Empty;
        }

        [Required(ErrorMessage = "Enter a password.")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        [MaxLength(128, ErrorMessage = "Password must be 128 characters or fewer.")]
        public string NewPassword { get; set; } = string.Empty;

        [MaxLength(100, ErrorMessage = "Full name must be 100 characters or fewer.")]
        public string? FullName { get; set; }
    }

    public sealed class ClaimInviteCodeDto
    {
        private string _email = string.Empty;
        private string _code = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [MaxLength(255)]
        public string Email
        {
            get => _email;
            set => _email = value?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        [Required(ErrorMessage = "Enter the access code from your invitation email.")]
        [MaxLength(16, ErrorMessage = "Enter the 8-character access code, for example HYDR-9X2W.")]
        public string Code
        {
            get => _code;
            set => _code = value?.Trim() ?? string.Empty;
        }

        [Required(ErrorMessage = "Enter a password.")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        [MaxLength(128, ErrorMessage = "Password must be 128 characters or fewer.")]
        public string NewPassword { get; set; } = string.Empty;

        [MaxLength(100, ErrorMessage = "Full name must be 100 characters or fewer.")]
        public string? FullName { get; set; }
    }
}