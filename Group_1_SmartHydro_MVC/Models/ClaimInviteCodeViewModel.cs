using System.ComponentModel.DataAnnotations;

namespace Group_1_SmartHydro_MVC.Models
{
    public class ClaimInviteCodeViewModel
    {
        [Required(ErrorMessage = "Enter the email address your invite was sent to.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email address")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Enter the access code from your invitation email.")]
        [StringLength(16, ErrorMessage = "Enter the 8-character access code, for example HYDR-9X2W.")]
        [Display(Name = "Access code")]
        public string? Code { get; set; }

        [Required(ErrorMessage = "Enter your full name.")]
        [StringLength(100, ErrorMessage = "Full name must be 100 characters or fewer.")]
        [Display(Name = "Full name")]
        public string? FullName { get; set; }

        [Required(ErrorMessage = "Enter a password.")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string? Password { get; set; }

        [Required(ErrorMessage = "Confirm your password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}
