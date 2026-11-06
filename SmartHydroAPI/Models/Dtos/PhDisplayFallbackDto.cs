namespace SmartHydroAPI.Models.Dtos;

// Historical display metadata. Never replaces the current sensor value.
public sealed record PhDisplayFallbackDto(decimal Value, DateTime RecordedAt, long AgeSeconds);
