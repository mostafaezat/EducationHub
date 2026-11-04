using System.Security.Claims;
using EducationHub.Shared.Common.Constants;
using Microsoft.AspNetCore.Authorization;

namespace EducationHub.API.Auth;

/// <summary>
/// Requires a specific EducationHub permission on the access token.
/// </summary>
/// <remarks>
/// <para>
/// Permissions are stored as Identity role claims with
/// <see cref="EducationHubClaimTypes.PermissionClaimType"/> and copied into the access
/// token at login, so authorization needs no database round trip.
/// </para>
/// <para>
/// The trade-off is a 15-minute window in which a revoked permission still authorizes. That
/// is accepted deliberately: shortening it means a database hit on every request, and
/// lengthening it would be a security problem. Handlers that guard a privilege <i>boundary</i>
/// - granting roles, granting permissions - re-read permissions from the database instead of
/// trusting these claims, so escalation cannot be laundered through a stale token.
/// </para>
/// </remarks>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (string.IsNullOrWhiteSpace(requirement.Permission))
        {
            return Task.CompletedTask;
        }

        // Every permission travels as one claim whose *type* is "Permission" and whose
        // *value* is the code ("Users.Create"), never as a claim of type "Users.Create".
        // Matching on claim Type would therefore never match anything - it would deny every
        // request while every other permission check in the system passed, which is the
        // failure mode of looking in the wrong half of the claim.
        var hasPermission = context.User.HasClaim(
            EducationHubClaimTypes.PermissionClaimType,
            requirement.Permission);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
        else
        {
            // Logged, never returned: a 403 body naming the missing permission would help
            // an attacker enumerate what a target account is missing.
            logger.LogWarning(
                "Authorization denied: {UserName} lacks permission {Permission}",
                context.User.Identity?.Name ?? "(anonymous)",
                requirement.Permission);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Requires the caller to be a platform administrator.
/// </summary>
/// <remarks>
/// A separate requirement rather than a permission check, because platform scope is not a
/// capability a tenant can grant itself: it is asserted by the signed role claim on the
/// token, and the same check backs the one narrow query-filter bypass in the system.
/// </remarks>
public sealed class PlatformAdminRequirement : IAuthorizationRequirement;

public sealed class PlatformAdminAuthorizationHandler : AuthorizationHandler<PlatformAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAdminRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated == true
            && context.User.IsInRole(RoleNames.PlatformAdmin)
            && IsPlatformTenantClaim(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// A platform administrator is minted with <c>TenantId = Guid.Empty</c> in the signed
    /// token. Requiring it here (in addition to the role) closes the hole that existed while
    /// a tenant could define a role named <c>PlatformAdmin</c>: that role would pass
    /// <c>IsInRole</c> but the claim would still carry the tenant's own id.
    /// </summary>
    private static bool IsPlatformTenantClaim(ClaimsPrincipal principal)
        => Guid.TryParse(
                principal.FindFirst(EducationHubClaimTypes.TenantId)?.Value,
                out var tenantId)
            && tenantId == Guid.Empty;
}