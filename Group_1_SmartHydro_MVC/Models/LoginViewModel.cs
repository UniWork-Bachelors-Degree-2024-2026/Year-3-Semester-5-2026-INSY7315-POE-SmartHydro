using System.ComponentModel.DataAnnotations;

namespace Group_1_SmartHydro_MVC.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email or username")]
        public string? EmailOrUsername { get; set; }

        [Required(ErrorMessage = "Enter your password.")]
        [Display(Name = "Password")]
        public string? Password { get; set; }

        public string? OtpCode { get; set; }

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}