using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    public class CameraTriggerRequestDto
    {
        [Required]
        [MaxLength(17)]
        public string DeviceMac { get; set; } = string.Empty;

        public bool Flash { get; set; } = false;
    }
}
