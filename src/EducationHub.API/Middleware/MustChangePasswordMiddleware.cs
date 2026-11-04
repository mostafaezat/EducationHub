using System.Text.Json.Serialization;
using EducationHub.API.Localization;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;

namespace EducationHub.API.Middleware;

/// <summary>
/// Blocks every authenticated request while the caller carries the `MustChangePassword` flag,
/// except the small allowlist needed to resolve the obligation.
/// </summary>
/// <remarks>
/// <para>
/// The flag is a signed claim (written by the token service), so this check is a pure
/// request-local predicate: no database round trip and no way for a caller to clear it except
/// by actually changing the password, which re-issues a token without the claim.
/// </para>
/// <para>
/// Anonymous endpoints (login/refresh) are unaffected - they carry no principal and therefore
/// no claim, which is why refresh is still reachable to rotate a token while the flag is set.
/// </para>
/// </remarks>
public sealed class MustChangePasswordMiddleware(RequestDelegate next, JsonResourceStore store)
{
    /// <summary>
    /// Paths a forced-password-change caller may still hit: change the password, read their
    /// own identity/permissions for the UI, sign out, or refresh the session.
    /// </summary>
    private static readonly string[] AllowedPathPrefixes =
    [
        "/api/auth/change-password",
        "/api/auth/me",
        "/api/auth/permissions",
        "/api/auth/logout",
        "/api/auth/refresh"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.User;

        if (principal.Identity?.IsAuthenticated == true
            && principal.HasClaim(EducationHubClaimTypes.MustChangePassword, "true")
            && !IsAllowed(context.Request.Path))
        {
            await WriteProblemAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    BoundaryMessages.Resolve(
                        store,
                        context,
                        LocalizationKeys.AuthMustChangePassword,
                        "Change your password before continuing."))
                .ConfigureAwait(false);

            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsAllowed(PathString path)
    {
        foreach (var prefix in AllowedPathPrefixes)
        {
            // Segment-aware: /api/auth/me-anything does not match /api/auth/me.
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new Problem
        {
            Type = "about:blank",
            Title = "Auth.MustChangePassword",
            Status = statusCode,
            Detail = detail,
            Code = "Auth.MustChangePassword"
        }).ConfigureAwait(false);
    }

    private sealed class Problem
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("status")]
        public int? Status { get; init; }

        [JsonPropertyName("detail")]
        public string? Detail { get; init; }

        [JsonPropertyName("code")]
        public string? Code { get; init; }
    }
}