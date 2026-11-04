using EducationHub.Domain.Common;

namespace EducationHub.Domain.Entities.Identity;

/// <summary>One row per authentication attempt, successful or not (spec section 9).</summary>
public class LoginHistory : TenantOwnedEntity
{
    public Guid UserId { get; set; }

    public string? UserName { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>Credential validation failure, account deactivated, locked, and so on.</summary>
    public string? FailureReason { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}
