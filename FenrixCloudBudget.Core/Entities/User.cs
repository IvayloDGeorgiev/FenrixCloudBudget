using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>An app user (multi-user/server mode).</summary>
public class User : EntityBase
{
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public UserRole Role { get; set; } = UserRole.Member;
    public UserStatus Status { get; set; } = UserStatus.Invited;
    public DateTimeOffset? LastLoginUtc { get; set; }
}
