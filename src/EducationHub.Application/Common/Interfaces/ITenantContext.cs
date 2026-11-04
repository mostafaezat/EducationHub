using Microsoft.AspNetCore.Http;

namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// The tenant the current request is scoped to, resolved once per request.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>read-only</b> view of request scope. It is intentionally dumb: it reports
/// what the caller proved, it does not decide anything. The rules live in the API layer
/// (<c>TenantResolutionMiddleware</c>) so they can be reviewed in one place.
/// </para>
/// <para>
/// <see cref="TenantId"/> is nullable and <c>null</c> is a valid, meaningful state: it means
/// "no tenant context". Callers must treat <c>null</c> as "deny", never as "all tenants".
/// The repository's fail-closed behaviour depends on that interpretation.
/// </para>
/// </remarks>
public interface ITenantContext
{
    /// <summary>Resolved tenant, or <c>null</c> when there is no tenant context.</summary>
    Guid? TenantId { get; }

    /// <summary>Resolved branch, when the user is scoped to one.</summary>
    Guid? BranchId { get; }

    /// <summary>
    /// True only for <see cref="Shared.Common.Constants.RoleNames.PlatformAdmin"/>.
    /// This is the single, deliberately narrow bypass that lets a platform administrator
    /// read across tenants. It must never be inferred from a header or a query string.
    /// </summary>
    bool IsPlatformScope { get; }

    bool HasTenant => TenantId.HasValue;
}

/// <summary>Authenticated caller identity, used for audit stamping and ownership checks.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? UserName { get; }

    string? FullName { get; }

    /// <summary>Owning tenant as asserted by the signed access token.</summary>
    Guid? TenantId { get; }

    Guid? BranchId { get; }

    string? IpAddress { get; }

    string? PreferredLanguage { get; }

    bool IsPlatformAdmin { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>
    /// Permission check against the claims embedded in the access token. Endpoint policies
    /// remain the primary mechanism; this exists for conditional checks inside handlers.
    /// </summary>
    bool HasPermission(string permission);
}
