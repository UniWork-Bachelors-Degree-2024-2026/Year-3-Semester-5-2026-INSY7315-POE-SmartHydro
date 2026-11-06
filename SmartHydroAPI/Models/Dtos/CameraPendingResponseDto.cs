namespace SmartHydroAPI.Models.Dtos
{
    // DTO returned when camera polls for pending commands (snapshots or ephemeral stream wakes)
    public class CameraPendingResponseDto
    {
        public string DeviceMac { get; set; } = string.Empty;
        public bool CapturePending { get; set; }
        public bool Flash { get; set; }
        public int? StructureId { get; set; }
        public bool StreamPending { get; set; }
        public int TimeoutSeconds { get; set; } = 90;
        public DateTime RequestedAt { get; set; }
    }
}
