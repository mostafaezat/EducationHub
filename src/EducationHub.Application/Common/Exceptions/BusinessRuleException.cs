using EducationHub.Shared.Common.Models;

namespace EducationHub.Application.Common.Exceptions;

/// <summary>
/// Signals an expected, reportable business failure - a suspended tenant, an expired
/// subscription, refresh-token reuse, a deactivated account.
/// </summary>
/// <remarks>
/// <para>
/// The distinction that matters: a <see cref="BusinessRuleException"/> becomes a clean
/// <c>4xx</c> response with a stable <see cref="Code"/>, while anything else propagates to
/// the global exception middleware and becomes a <c>500</c> with a correlation id. Code
/// therefore stays honest about what actually went wrong, and an unexpected fault is never
/// disguised as a normal business outcome.
/// </para>
/// <para>
/// <see cref="Code"/> is a machine-readable identifier the frontend can branch on, so it
/// must never be localized or reworded - only <see cref="Exception.Message"/> is.
/// </para>
/// </remarks>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message, IReadOnlyList<Error>? errors = null)
        : base(message)
    {
        Code = code;
        Errors = errors ?? [new Error(code, message)];
    }

    /// <summary>Stable machine-readable code, e.g. <c>Tenant.Inactive</c>.</summary>
    public string Code { get; }

    public IReadOnlyList<Error> Errors { get; }

    /// <summary>Convenience factories for the recurring rules.</summary>
    public static BusinessRuleException TenantInactive()
        => new("Tenant.Inactive", "The tenant is not active.");

    public static BusinessRuleException SubscriptionExpired()
        => new("Tenant.SubscriptionExpired", "The tenant subscription has expired.");

    public static BusinessRuleException AccountInactive()
        => new("Account.Inactive", "This account has been deactivated.");

    public static BusinessRuleException EmailNotConfirmed()
        => new("Account.EmailNotConfirmed", "Confirm your email address before signing in.");

    public static BusinessRuleException InvalidCredentials()
        => new("Auth.InvalidCredentials", "Invalid email or password.");

    public static BusinessRuleException ReuseDetected()
        => new("Auth.ReuseDetected",
            "This refresh token was already used. All sessions in the chain have been revoked.");
}
