namespace SmartHydroAPI.Models.Dtos;

public record ModeStateDto(string DeviceMac, string SystemType, int? RequestedMode, DateTime? RequestedAt,
    int? CommandId, int? ReportedMode, int? ReportedPreferredMode, int? ReportedModeReason,
    DateTime? LastNodeSeenAt, DateTime? LastGatewaySeenAt, int? LastGatewayRssi,
    string? Transport, string Status, DateTime ServerUtc);
