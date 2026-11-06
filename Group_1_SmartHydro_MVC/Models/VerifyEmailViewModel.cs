using System.ComponentModel.DataAnnotations;

namespace Group_1_SmartHydro_MVC.Models
{
    public class VerifyEmailViewModel
    {
        public string? Email { get; set; }

        [Required(ErrorMessage = "Enter the 6-digit code from your email.")]
        [RegularExpression("^[0-9]{6}$", ErrorMessage = "Enter the 6-digit code from your email.")]
        [Display(Name = "Verification code")]
        public string? Code { get; set; }
    }
}