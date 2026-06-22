using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>An emailed invitation carrying a one-time 6-digit code (stored as a hash only).</summary>
public class Invitation : EntityBase
{
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;

    /// <summary>Hash of the 6-digit code. Never store the code itself.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresUtc { get; set; }
    public int AttemptCount { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
}
