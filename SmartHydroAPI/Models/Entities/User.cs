using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartHydroAPI.Models.Entities
{
    [Table("users")]
    public class User
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [MaxLength(20)]
        [Column("role")]
        public string Role { get; set; } = UserRoles.Operator;

        [MaxLength(255)]
        [Column("password_hash")]
        public string? PasswordHash { get; set; }

        [MaxLength(60)]
        [Column("farm_name")]
        public string? FarmName { get; set; }

        [MaxLength(100)]
        [Column("full_name")]
        public string? FullName { get; set; }

        [Column("is_verified")]
        public bool IsVerified { get; set; }

        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = UserStatus.Approved;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("last_login_at")]
        public DateTime? LastLoginAt { get; set; }
    }

    public static class UserStatus
    {
        public const string PendingApproval = "PendingApproval";
        public const string Approved = "Approved";
        public const string Denied = "Denied";
        public const string Disabled = "Disabled";
    }

    public static class UserRoles
    {
        public const string Admin = "Admin";
        public const string Operator = "Operator";
        public const string Worker = "Worker";

        // Roles that may switch relays and add doses; changing how a box connects stays with Admin and Operator
        public const string EquipmentControl = Admin + "," + Operator + "," + Worker;
    }
}