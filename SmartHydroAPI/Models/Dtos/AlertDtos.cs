using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    public class CreateAlertDto
    {
        [Required]
        public int StructureId { get; set; }

        [MaxLength(17)]
        public string? DeviceMac { get; set; }

        [Required]
        [MaxLength(50)]
        public string Component { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Severity { get; set; } = "WARNING"; // 'INFO', 'WARNING', 'CRITICAL'

        [MaxLength(50)]
        public string? Value { get; set; }

        [MaxLength(300)]
        public string? Detail { get; set; }
    }

    public class ResolveAlertDto
    {
        [MaxLength(255)]
        public string ClearedBy { get; set; } = "User";
    }

    public class AlertResponseDto
    {
        public long Id { get; set; }
        public int StructureId { get; set; }
        public string? StructureName { get; set; }
        public string? DeviceMac { get; set; }
        public string? DeviceName { get; set; }
        public string Component { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string? Value { get; set; }
        public string? Detail { get; set; }
        public DateTime OccurredAt { get; set; }
        public bool IsResolved { get; set; }
        public string? ClearedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}
