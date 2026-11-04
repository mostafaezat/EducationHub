namespace EducationHub.Application.Features.Auth.DTOs;

/// <summary>Credentials posted to <c>POST /api/auth/login</c>.</summary>
public sealed record LoginRequest(
    string Email,
    string Password,
    bool RememberMe = false);

/// <summary>
/// The token pair plus the identity the frontend needs to build its shell.
/// </summary>
/// <remarks>
/// <see cref="Permissions"/> is returned on login and on refresh so the SPA can gate menus
/// and routes without a second round trip, and so it survives a full page reload by
/// rehydrating from the refresh token.
/// </remarks>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string TokenType,
    UserProfileDto User);

public sealed record UserProfileDto(
    Guid Id,
    string FullName,
    string Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    Guid TenantId,
    Guid? BranchId,
    string? TenantName,
    string PreferredLanguage,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

public sealed record LogoutRequest(string? RefreshToken);

/// <summary>Email posted to <c>POST /api/auth/forgot-password</c> to start a password reset.</summary>
public sealed record ForgotPasswordRequest(string Email);

/// <summary>Submission for <c>POST /api/auth/reset-password</c> from the emailed link.</summary>
public sealed record ResetPasswordRequest(
    string Email,
    string ResetToken,
    string NewPassword,
    string ConfirmPassword);

/// <summary>Email-confirmation submission for <c>POST /api/auth/confirm-email</c>.</summary>
public sealed record ConfirmEmailRequest(string Email, string Token);

/// <summary>Email posted to <c>POST /api/auth/resend-confirmation</c>.</summary>
public sealed record ResendConfirmationRequest(string Email);
