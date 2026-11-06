using System.ComponentModel.DataAnnotations;

namespace SmartHydroAPI.Models.Dtos
{
    public class PendingOperatorDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string FarmName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class OperatorReviewResponseDto
    {
        public string Message { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool NotificationSent { get; set; }
    }

    public class AdminUserDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? FarmName { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public int OwnedSiteCount { get; set; }
        public List<string> AssignedSites { get; set; } = new();
        public bool IsProtected { get; set; }
        public bool IsCurrentUser { get; set; }
        public List<string> AllowedActions { get; set; } = new();
        public List<StatusOptionDto> StatusOptions { get; set; } = new();
    }

    public class StatusOptionDto
    {
        public string Status { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
    }

    public record UpdateUserDto(
        [MaxLength(100)] string? FullName,
        [MaxLength(60)] string? FarmName,
        [Required][MaxLength(20)] string Role
    );

    public class AdminSiteDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? OwnerEmail { get; set; }
        public DateTime CreatedAt { get; set; }
        public int StructureCount { get; set; }
        public int WorkerCount { get; set; }
    }

    public record AssignSiteOwnerDto(
        [Required][EmailAddress][MaxLength(255)] string OwnerEmail
    );

    public class AccountActionResponseDto
    {
        public string Message { get; set; } = string.Empty;
        public int Id { get; set; }
        public string? Status { get; set; }
    }
}