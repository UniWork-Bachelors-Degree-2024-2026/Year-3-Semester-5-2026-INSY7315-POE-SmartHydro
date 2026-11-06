using System.ComponentModel.DataAnnotations;
using Group_1_SmartHydro_MVC.Services;

namespace Group_1_SmartHydro_MVC.Models
{
    public class InviteWorkerViewModel
    {
        [Required(ErrorMessage = "Choose a site.")]
        [Display(Name = "Site")]
        public Guid? SiteId { get; set; }

        [Required(ErrorMessage = "Enter the worker's email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [RegularExpression(EmailFormat.Pattern, ErrorMessage = "Enter a valid email address.")]
        [StringLength(255, ErrorMessage = "Email address must be 255 characters or fewer.")]
        [Display(Name = "Worker email address")]
        public string? Email { get; set; }

        public List<OperatorSite> Sites { get; set; } = new();

        public List<TeamMember> Team { get; set; } = new();

        public bool TeamLoadFailed { get; set; }
    }
}