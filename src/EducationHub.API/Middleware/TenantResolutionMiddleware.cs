using System.Security.Claims;
using EducationHub.API.Localization;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;

namespace EducationHub.API.Middleware;

/// <summary>
/// Resolves the tenant, branch and language for the request, and rejects contradictions.
/// </summary>
/// <remarks>
/// <para>
/// Every tenant rule lives here so it can be reviewed in one place. Nothing else in the
/// request pipeline decides which tenant a request belongs to.
/// </para>
/// <para>
/// The signed JWT <c>TenantId</c> claim is authoritative. <c>X-Tenant-Id</c> is accepted
/// only as a cross-check, and a mismatch is a 403 - never a switch to the header's value.
/// Honouring the header instead would let any authenticated caller read any tenant simply by
/// editing a header, which is the single most important thing this middleware exists to
/// prevent.
/// </para>
/// <para>
/// When there is no claim, the request proceeds with <i>no tenant context</i> rather than
/// being rejected. The global query filters then match no tenant rows at all, so the
/// fail-closed outcome is an empty result set instead of a cross-tenant leak. Anonymous
/// endpoints (login, refresh) depend on being allowed through.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger, JsonResourceStore store)
{
    /// <summary>Cross-check header. Never a source of truth.</summary>
    public const string TenantHeaderName = "X-Tenant-Id";

    /// <summary>Branch selector, same rules as <see cref="TenantHeaderName"/>.</summary>
    public const string BranchHeaderName = "X-Branch-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.User;

        if (principal.Identity?.IsAuthenticated == true)
        {
            var claimTenant = ReadGuid(principal, EducationHubClaimTypes.TenantId);
            var headerTenant = ReadHeaderGuid(context, TenantHeaderName);

            if (claimTenant.HasValue && headerTenant.HasValue && claimTenant != headerTenant)
            {
                logger.LogWarning(
                    "Tenant mismatch for {UserName}: token {ClaimTenant} vs header {HeaderTenant}",
                    principal.Identity?.Name ?? "(unknown)",
                    claimTenant.Value,
                    headerTenant.Value);

                await WriteProblemAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        BoundaryMessages.Resolve(
                            store,
                            context,
                            LocalizationKeys.ErrorTenantMismatch,
                            "The supplied X-Tenant-Id does not match the authenticated tenant."))
                    .ConfigureAwait(false);

                return;
            }

            var claimBranch = ReadGuid(principal, EducationHubClaimTypes.BranchId);
            var headerBranch = ReadHeaderGuid(context, BranchHeaderName);

            if (claimBranch.HasValue && headerBranch.HasValue && claimBranch != headerBranch)
            {
                await WriteProblemAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        BoundaryMessages.Resolve(
                            store,
                            context,
                            LocalizationKeys.BranchMismatch,
                            "The supplied X-Branch-Id does not match the authenticated branch."))
                    .ConfigureAwait(false);

                return;
            }
        }

        context.Items[TenantItemKey] = ResolveTenant(context, principal);
        context.Items[BranchItemKey] = ResolveBranch(context, principal);
        context.Items[LanguageItemKey] = RequestLanguageResolver.Resolve(context);

        await next(context).ConfigureAwait(false);
    }

    internal const string TenantItemKey = "EducationHub.TenantId";
    internal const string BranchItemKey = "EducationHub.BranchId";
    internal const string LanguageItemKey = "EducationHub.Language";

    /// <summary>Reads the effective tenant for diagnostics and for the response header.</summary>
    public static Guid? CurrentTenant(HttpContext context)
        => context.Items.TryGetValue(TenantItemKey, out var value) ? value as Guid? : null;

    public static Guid? CurrentBranch(HttpContext context)
        => context.Items.TryGetValue(BranchItemKey, out var value) ? value as Guid? : null;

    private static Guid? ResolveTenant(HttpContext context, ClaimsPrincipal principal)
    {
        var fromClaim = ReadGuid(principal, EducationHubClaimTypes.TenantId);

        if (fromClaim.HasValue)
        {
            return fromClaim;
        }

        // Anonymous or platform call: a header may still describe which tenant is being
        // addressed, and nothing downstream trusts it - the query filter reads the signed
        // claim through ITenantContext, not this value.
        return ReadHeaderGuid(context, TenantHeaderName);
    }

    private static Guid? ResolveBranch(HttpContext context, ClaimsPrincipal principal)
    {
        var fromClaim = ReadGuid(principal, EducationHubClaimTypes.BranchId);

        return fromClaim ?? ReadHeaderGuid(context, BranchHeaderName);
    }

    // Language resolution moved to shared RequestLanguageResolver to ensure a single
    // precedence (Accept-Language → PreferredLanguage claim → Arabic) used by both the
    // middleware and the request localization pipeline.

    private static Guid? ReadGuid(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirst(claimType)?.Value;

        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static Guid? ReadHeaderGuid(HttpContext context, string headerName)
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var values))
        {
            return null;
        }

        return Guid.TryParse(values.ToString(), out var parsed) ? parsed : null;
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = statusCode == StatusCodes.Status403Forbidden ? "Forbidden" : "Error",
            status = statusCode,
            detail
        }).ConfigureAwait(false);
    }
}