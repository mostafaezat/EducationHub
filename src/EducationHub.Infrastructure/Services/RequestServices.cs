using System.Security.Claims;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Infrastructure.Services;

/// <summary>
/// The request's tenant scope, read from the authenticated principal.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately does no resolution work of its own. Deciding which tenant a request belongs
/// to - claim versus header, and what happens when they disagree - lives in
/// <c>TenantResolutionMiddleware</c> in the API layer, so those rules can be read in one
/// place instead of being spread across every consumer of this interface.
/// </para>
/// <para>
/// The tenant comes from the <b>signed</b> token claim only. The <c>X-Tenant-Id</c> header
/// is never consulted here: a header is attacker-controlled, so honouring it would let any
/// caller read any tenant by editing a request.
/// </para>
/// </remarks>
public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? TenantId => ClaimReader.ReadGuid(Principal, EducationHubClaimTypes.TenantId);

    public Guid? BranchId => ClaimReader.ReadGuid(Principal, EducationHubClaimTypes.BranchId);

    /// <summary>
    /// Platform scope is asserted by the signed <c>role</c> claim set, never by a header or
    /// query string. This is the single narrow bypass in the isolation model.
    /// </summary>
    public bool IsPlatformScope
        => PlatformScopeOverride.IsActive || ClaimReader.IsPlatformAdmin(Principal);

    public bool HasTenant => TenantId.HasValue;
}

/// <summary>
/// Shared claim parsing for the two request-context implementations.
/// </summary>
/// <remarks>
/// Both <see cref="ITenantContext"/> and <see cref="ICurrentUser"/> read the same claims, and
/// they must agree: if they parsed differently, the tenant a query filter enforces could
/// differ from the tenant a handler checks ownership against, which would silently defeat
/// tenant isolation. Hence one parser rather than two.
/// </remarks>
internal static class ClaimReader
{
    internal static Guid? ReadGuid(ClaimsPrincipal? principal, string claimType)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var value = principal.FindFirst(claimType)?.Value;

        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    internal static string? ReadText(ClaimsPrincipal? principal, string claimType)
    {
        var value = principal?.FindFirst(claimType)?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    internal static bool IsPlatformAdmin(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var hasRole = principal.IsInRole(RoleNames.PlatformAdmin)
            // IsInRole depends on the role claim type configured on the token, which differs
            // between JwtBearer ("role") and the default (ClaimTypes.Role). Checking the raw
            // claims too means a misconfigured RoleClaimType fails closed on the safe side: the
            // platform admin is denied cross-tenant reads rather than being granted them.
            || principal.Claims.Any(c =>
                (c.Type == ClaimTypes.Role || c.Type == "role")
                && string.Equals(c.Value, RoleNames.PlatformAdmin, StringComparison.OrdinalIgnoreCase));

        if (!hasRole)
        {
            return false;
        }

        // The role alone is not enough. Platform admins are minted with TenantId = Guid.Empty
        // by JwtTokenService, and the signed tenant claim cannot be forged - so requiring it
        // here denies any token whose "PlatformAdmin" role came from a tenant-defined role
        // created before the create-role reservation existed.
        return ReadGuid(principal, EducationHubClaimTypes.TenantId) == Guid.Empty;
    }
}
/// <summary>
/// The authenticated caller, read from the HTTP context.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private static readonly string[] Empty = [];

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ClaimReader.ReadGuid(Principal, ClaimTypes.NameIdentifier);

    public string? UserName => Principal?.Identity?.Name;

    public string? FullName => ClaimReader.ReadText(Principal, EducationHubClaimTypes.FullName);

    public Guid? TenantId => ClaimReader.ReadGuid(Principal, EducationHubClaimTypes.TenantId);

    public Guid? BranchId => ClaimReader.ReadGuid(Principal, EducationHubClaimTypes.BranchId);

    public string? IpAddress
    {
        get
        {
            var context = httpContextAccessor.HttpContext;

            return context is null ? null : context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? PreferredLanguage
        => ClaimReader.ReadText(Principal, EducationHubClaimTypes.PreferredLanguage);

    public bool IsPlatformAdmin => ClaimReader.IsPlatformAdmin(Principal);

    public IReadOnlyCollection<string> Roles
        => Principal is null
            ? Empty
            :
            [
                .. Principal.Claims
                    .Where(static c => c.Type is ClaimTypes.Role or "role")
                    .Select(static c => c.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];

    public IReadOnlyCollection<string> Permissions
        => Principal is null
            ? Empty
            :
            [
                .. Principal.Claims
                    .Where(static c => c.Type == EducationHubClaimTypes.PermissionClaimType)
                    .Select(static c => c.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];

    /// <summary>
    /// Checks a permission from the token's claims.
    /// </summary>
    /// <remarks>
    /// Convenience for conditional checks inside handlers, where an endpoint-level policy
    /// would be too coarse. Claims are a 15-minute snapshot, so this must never be the only
    /// thing standing between a caller and a privileged write - handlers that enforce
    /// privilege boundaries re-read permissions from the database instead.
    /// </remarks>
    public bool HasPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        var principal = Principal;

        if (principal is null)
        {
            return false;
        }

        return principal.HasClaim(
            EducationHubClaimTypes.PermissionClaimType,
            permission);
    }
}

/// <summary>
/// The system clock, aware of the tenant's time zone.
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped, which is what makes the per-request zone cache correct: one request
/// belongs to exactly one tenant, so the zone is looked up once and reused.
/// </para>
/// <para>
/// Every stored instant is UTC. This type exists to convert to a tenant's local time at the
/// moment something is <i>displayed</i> or when a calendar day is being decided - never to
/// store a local time.
/// </para>
/// </remarks>
public sealed class TenantDateTimeService(
    EducationHubDbContext context,
    ITenantContext tenantContext) : IDateTimeService
{
    private bool _resolved;
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;

    /// <summary>
    /// Always UTC.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTime.Now"/> is deliberately unused. Storing local times in a table
    /// shared by tenants in different zones would make "yesterday's attendance" a different
    /// instant for each school, and would silently shift again for any zone observing
    /// daylight saving.
    /// </remarks>
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);

    public DateTime GetTenantNow() => ToTenantTime(UtcNow);

    public DateOnly GetTenantToday() => DateOnly.FromDateTime(GetTenantNow());

    public DateTime ToTenantTime(DateTime utcDateTime)
    {
                EnsureZoneResolved();

        return TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
            _zone);
    }

    private void EnsureZoneResolved()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            // No tenant: UTC rather than an exception. Background jobs and platform-level
            // code legitimately run without a tenant and must not fail on a display concern.
            return;
        }

        var timeZoneId = context.Tenants
            .Where(t => t.Id == tenantId)
            .Select(static t => t.TimeZone)
            .FirstOrDefault();

        _zone = TryFindZone(timeZoneId);
    }

    /// <summary>
    /// Resolves an IANA id, falling back to UTC for anything unusable.
    /// </summary>
    /// <remarks>
    /// The cast to Windows ids plus <c>InvariantCulture</c> lookup is what keeps tenants
    /// portable across Windows and Linux hosts: the same stored id must resolve on both, or
    /// "today" would differ depending on where the app is deployed.
    /// </remarks>
    private static TimeZoneInfo TryFindZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        var id = timeZoneId.Trim();

        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return zone;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out var fromWindows))
        {
            return fromWindows;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var fromIana))
        {
            return fromIana;
        }

        return TimeZoneInfo.Utc;
    }
}
