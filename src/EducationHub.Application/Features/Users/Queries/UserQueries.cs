using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Users.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EducationHub.Application.Features.Users.Queries;

/// <summary>
/// Pages the tenant's users.
/// </summary>
/// <remarks>
/// <see cref="ApplicationUser"/> is tenant-owned, so <c>UserManager.Users</c> is already
/// tenant-filtered at the database level. No handler-side tenant comparison is needed or
/// wanted here - adding one would suggest the filter might be missing.
/// </remarks>
public sealed record GetUsersQuery(
    PaginationRequest Pagination,
    bool ActiveOnly = false,
    string? Role = null,
    Guid? BranchId = null) : IRequest<Result<PaginatedList<UserDto>>>;

public sealed class GetUsersQueryHandler(
    UserManager<ApplicationUser> userManager,
    IUserAccessReader accessReader)
    : IRequestHandler<GetUsersQuery, Result<PaginatedList<UserDto>>>
{
    public async Task<Result<PaginatedList<UserDto>>> Handle(
        GetUsersQuery request,
        CancellationToken cancellationToken)
    {
        var query = userManager.Users;

        if (request.ActiveOnly)
        {
            query = query.Where(static u => u.IsActive);
        }

        if (request.BranchId.HasValue)
        {
            query = query.Where(u => u.BranchId == request.BranchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var memberIds = await accessReader
                .GetUserIdsByRoleAsync(request.Role.Trim(), cancellationToken)
                .ConfigureAwait(false);

            if (memberIds.Count == 0)
            {
                return Result<PaginatedList<UserDto>>.Success(
                    PaginatedList<UserDto>.Empty(request.Pagination.PageNumber, request.Pagination.PageSize));
            }

            // Intersected with the tenant-filtered user set, so the tenant filter stays the
            // authority and the whole predicate still translates to a single SQL statement.
            var memberSet = memberIds.ToHashSet();

            query = query.Where(u => memberSet.Contains(u.Id));
        }

        var page = await query
            .OrderBy(u => u.FullName)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var pageIds = page.Items.Select(static u => u.Id).ToArray();

        var roles = pageIds.Length == 0
            ? new Dictionary<Guid, IReadOnlyList<string>>()
            : await accessReader.GetRolesForUsersAsync(pageIds, cancellationToken).ConfigureAwait(false);

        var permissions = pageIds.Length == 0
            ? new Dictionary<Guid, IReadOnlyList<string>>()
            : await accessReader.GetPermissionsForUsersAsync(pageIds, cancellationToken).ConfigureAwait(false);

        var items = page.Items
            .Select(u => UserDto.FromEntity(
                u,
                roles.GetValueOrDefault(u.Id) ?? [],
                permissions.GetValueOrDefault(u.Id) ?? []))
            .ToArray();

        return Result<PaginatedList<UserDto>>.Success(
            new PaginatedList<UserDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetUserQuery(Guid Id) : IRequest<Result<UserDto>>;

public sealed class GetUserQueryHandler(
    UserManager<ApplicationUser> userManager,
    ITenantContext tenantContext,
    IUserAccessReader accessReader)
    : IRequestHandler<GetUserQuery, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        BranchCommandSupport.RequireTenantId(tenantContext);

        var user = await userManager.FindByIdAsync(request.Id.ToString()).ConfigureAwait(false);

        if (user is null || user.TenantId != tenantContext.TenantId)
        {
            return Result<UserDto>.Failure("Users.NotFound", "User not found.");
        }

        IReadOnlyList<string> roles = [.. await userManager.GetRolesAsync(user).ConfigureAwait(false)];

        var permissions = await accessReader
            .GetPermissionsForUsersAsync([user.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result<UserDto>.Success(
            UserDto.FromEntity(user, roles, permissions.GetValueOrDefault(user.Id) ?? []));
    }
}

/// <summary>Pages users assigned a single role. Used by the role editor's "members" panel.</summary>
public sealed record GetUsersByRoleQuery(string Role, PaginationRequest Pagination) : IRequest<Result<PaginatedList<UserDto>>>;

public sealed class GetUsersByRoleQueryHandler(
    UserManager<ApplicationUser> userManager,
    IUserAccessReader accessReader)
    : IRequestHandler<GetUsersByRoleQuery, Result<PaginatedList<UserDto>>>
{
    public async Task<Result<PaginatedList<UserDto>>> Handle(
        GetUsersByRoleQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return Result<PaginatedList<UserDto>>.Failure("Users.RoleRequired", "A role name is required.");
        }

        var role = request.Role.Trim();

        var memberIds = await accessReader
            .GetUserIdsByRoleAsync(role, cancellationToken)
            .ConfigureAwait(false);

        if (memberIds.Count == 0)
        {
            return Result<PaginatedList<UserDto>>.Success(
                PaginatedList<UserDto>.Empty(request.Pagination.PageNumber, request.Pagination.PageSize));
        }

        // The role set is intersected with the tenant-filtered user set BEFORE paging, so
        // TotalItems reflects the real membership count rather than the page size.
        var ids = memberIds.ToHashSet();

        var page = await userManager.Users
            .Where(u => ids.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var pageIds = page.Items.Select(static u => u.Id).ToArray();

        var roles = pageIds.Length == 0
            ? new Dictionary<Guid, IReadOnlyList<string>>()
            : await accessReader.GetRolesForUsersAsync(pageIds, cancellationToken).ConfigureAwait(false);

        var permissions = pageIds.Length == 0
            ? new Dictionary<Guid, IReadOnlyList<string>>()
            : await accessReader.GetPermissionsForUsersAsync(pageIds, cancellationToken).ConfigureAwait(false);

        var items = page.Items
            .Select(u => UserDto.FromEntity(
                u,
                roles.GetValueOrDefault(u.Id) ?? [],
                permissions.GetValueOrDefault(u.Id) ?? []))
            .ToArray();

        return Result<PaginatedList<UserDto>>.Success(
            new PaginatedList<UserDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}
