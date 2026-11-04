namespace EducationHub.Shared.Common.Constants;

/// <summary>
/// Names of the claims EducationHub puts into its own access tokens.
/// The JWT <c>sub</c> / <c>jti</c> / <c>exp</c> claims come from
/// <see cref="System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames"/> and are
/// deliberately NOT redefined here, to avoid two sources of truth for the same claim.
/// </summary>
public static class EducationHubClaimTypes
{
    /// <summary>Tenant the authenticated user belongs to. Server-issued at login.</summary>
    public const string TenantId = "TenantId";

    /// <summary>Branch the user is currently scoped to, when the tenant has branches.</summary>
    public const string BranchId = "BranchId";

    public const string UserId = "UserId";
    public const string FullName = "FullName";

    /// <summary>
    /// Presence of this claim means the account must change its password before any other
    /// authenticated action is allowed (server-side gate; see MustChangePasswordMiddleware).
    /// </summary>
    public const string MustChangePassword = "MustChangePassword";

    /// <summary>ISO 639-1 code (<c>ar</c> / <c>en</c>) used for server-side localization.</summary>
    public const string PreferredLanguage = "PreferredLanguage";

    /// <summary>Authorization policy name of the form <c>{Area}.{Action}</c>.</summary>
    public const string Permission = "Permission";

    /// <summary>Claim type used when persisting permissions in Identity role claims.</summary>
    public const string PermissionClaimType = "Permission";
}
