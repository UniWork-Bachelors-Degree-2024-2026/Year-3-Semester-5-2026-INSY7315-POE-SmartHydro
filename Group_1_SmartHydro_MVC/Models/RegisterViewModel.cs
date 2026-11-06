using System.ComponentModel.DataAnnotations;

namespace Group_1_SmartHydro_MVC.Models
{
    public static class EmailFormat
    {
        public const string Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Enter your full name.")]
        [StringLength(100, ErrorMessage = "Full name must be 100 characters or fewer.")]
        [Display(Name = "Full name")]
        public string? FullName { get; set; }

        [Required(ErrorMessage = "Enter a name for your starting site.")]
        [StringLength(60, ErrorMessage = "Starting site name must be 60 characters or fewer.")]
        [Display(Name = "Starting site")]
        public string? FarmName { get; set; }

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [RegularExpression(EmailFormat.Pattern, ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email address")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Enter a password.")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string? Password { get; set; }

        [Required(ErrorMessage = "Confirm your password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}