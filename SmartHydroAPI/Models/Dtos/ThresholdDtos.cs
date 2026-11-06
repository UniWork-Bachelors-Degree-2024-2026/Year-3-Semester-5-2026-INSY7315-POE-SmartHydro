using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    public class UpdateThresholdDto
    {
        [Required]
        public int StructureId { get; set; }

        [Required]
        [MaxLength(30)]
        public string SensorType { get; set; } = "PH";

        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
    }

    public class ThresholdResponseDto
    {
        public int Id { get; set; } // 0 while the structure is still on the healthy default for this reading
        public int StructureId { get; set; }
        public string StructureType { get; set; } = string.Empty; // 'VERTICAL_TOWER' or 'FODDER_TRAY'
        public string SensorType { get; set; } = string.Empty;
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        public bool IsDefault { get; set; }
    }

    // One structure with the safe range of each reading it has, saved or default
    public class StructureThresholdsDto
    {
        public int StructureId { get; set; }
        public string StructureName { get; set; } = string.Empty;
        public string StructureType { get; set; } = string.Empty;
        public List<ThresholdResponseDto> Thresholds { get; set; } = new();
    }
}
