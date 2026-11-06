using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    // DTO representing registered IoT device details
    public record DeviceDto(
        string MacAddress,
        int? StructureId,
        string DeviceName,
        string DeviceType,
        string? StructureType,
        string? LocalIp,
        bool IsOnline,
        bool IsOfflineMode,
        bool PendingOfflineTransition,
        DateTime LastSeenAt,
        DateTime CreatedAt
    );

    // DTO for registering a new hardware device
    public class CreateDeviceDto
    {
        [Required]
        [MaxLength(17)]
        public string MacAddress { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string DeviceName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string DeviceType { get; set; } = "SENSOR-NODE"; // 'SENSOR-NODE', 'CAMERA-NODE', 'CONTROLLER-NODE'

        [MaxLength(50)]
        public string? StructureType { get; set; } // 'VERTICAL-TOWER' or 'FODDER-SYSTEM'

        public int? StructureId { get; set; }

        [MaxLength(45)]
        public string? LocalIp { get; set; }
    }

    // DTO for updating existing device metadata
    public class UpdateDeviceDto
    {
        [MaxLength(100)]
        public string? DeviceName { get; set; }

        [MaxLength(50)]
        public string? DeviceType { get; set; }

        [MaxLength(50)]
        public string? StructureType { get; set; }

        public int? StructureId { get; set; }

        [MaxLength(45)]
        public string? LocalIp { get; set; }
    }

    public record RenameDeviceDto(string Name);

    public record AssignStructureDto(int? StructureId);

    public record SetStructureTypeDto(
        [Required, MaxLength(50)] string StructureType
    );
}
