using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    public record SiteDto(
        Guid Id,
        string Name,
        DateTime CreatedAt,
        int StructureCount,
        string? OwnerEmail = null
    );

    public record CreateSiteDto(
        Guid? Id,
        [Required][MaxLength(60)] string Name,
        [MaxLength(255)] string? OwnerEmail = null
    );

    public record UpdateSiteDto(
        [Required][MaxLength(60)] string Name
    );

    public record DeletedSiteDto(
        Guid Id,
        string Name,
        string? OwnerEmail,
        DateTime CreatedAt,
        DateTime? DeletedAt,
        string? DeletedByEmail,
        int StructureCount
    );

    public record DeleteSiteResultDto(
        bool Success,
        string Message,
        int LinkedStructuresCount
    );
}