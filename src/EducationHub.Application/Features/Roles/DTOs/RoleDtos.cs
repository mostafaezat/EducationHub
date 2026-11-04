using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;

namespace EducationHub.Application.Features.Roles.DTOs;

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    string? DescriptionAr,
    bool IsSystemRole,
    bool IsActive,
    Guid? TenantId,
    IReadOnlyList<string> Permissions,
    int UserCount,
    DateTime CreatedAt)
{
    public static RoleDto FromEntity(
        ApplicationRole role,
        IReadOnlyList<string> permissions,
        int userCount)
        => new(
            role.Id,
            role.Name ?? string.Empty,
            role.Description,
            role.DescriptionAr,
            role.IsSystemRole,
            role.IsActive,
            role.TenantId == Guid.Empty ? null : role.TenantId,
            permissions,
            userCount,
            role.CreatedAt);
}

public sealed record CreateRoleRequest(
    string Name,
    string? Description,
    string? DescriptionAr,
    IReadOnlyList<string>? Permissions);

public sealed record UpdateRoleRequest(
    string? Description,
    string? DescriptionAr,
    bool? IsActive);

public sealed record AssignPermissionsRequest(IReadOnlyList<string> Permissions);
