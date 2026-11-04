using EducationHub.Shared.Common.Constants;

namespace EducationHub.Application.Features.Users.DTOs;

public sealed record UserDto(
    Guid Id,
    string FullName,
    string Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    Guid TenantId,
    Guid? BranchId,
    string PreferredLanguage,
    bool IsActive,
    bool MustChangePassword,
    bool IsLockedOut,
    DateTime? LockedOutUntil,
    DateTime? LastLoginAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    DateTime CreatedAt)
{
    public static UserDto FromEntity(
        Domain.Entities.Identity.ApplicationUser user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions)
        => new(
            user.Id,
            user.FullName,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TenantId,
            user.BranchId,
            user.PreferredLanguage,
            user.IsActive,
            user.MustChangePassword,
            user.IsLockedOut,
            user.LockedOutUntil,
            user.LastLoginAt,
            roles,
            permissions,
            user.CreatedAt);
}

public sealed record CreateUserRequest(
    string FullName,
    string Email,
    string Password,
    string? PhoneNumber,
    Guid? BranchId,
    string? PreferredLanguage,
    bool MustChangePassword,
    IReadOnlyList<string> Roles,
    bool SendEmailConfirmation = false);

public sealed record UpdateUserRequest(
    string? FullName,
    string? PhoneNumber,
    Guid? BranchId,
    string? PreferredLanguage,
    bool? IsActive,
    string? Notes);

/// <summary>
/// Replaces a user's password on the caller's behalf (admin reset).
/// </summary>
/// <remarks>
/// Named <c>AdminResetPasswordRequest</c> rather than <c>ResetPasswordRequest</c> because the
/// Auth feature already owns a <c>ResetPasswordRequest</c>; two DTOs sharing one simple name
/// made Swashbuckle emit a duplicate OpenAPI schema id and fail the whole document (P0-2).
/// </remarks>
public sealed record AdminResetPasswordRequest(
    string NewPassword,
    bool MustChangePassword = true);

public sealed record AssignRolesRequest(IReadOnlyList<string> Roles);

public sealed record ChangeUserStatusRequest(bool IsActive, string? Reason);
