using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Roles.Commands;
using EducationHub.Application.Features.Roles.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Roles.Queries;

/// <summary>
/// Pages the current tenant's roles; for the platform administrator (no tenant) the
/// platform-level roles, which are the only ones a platform user can hold.
/// </summary>
public sealed record GetRolesQuery(PaginationRequest Pagination, string? Search, bool ActiveOnly)
    : IRequest<Result<PaginatedList<RoleDto>>>;

public sealed class GetRolesQueryHandler(
    RoleManager<ApplicationRole> roleManager,
    ITenantContext tenantContext,
    IUserAccessReader accessReader)
    : IRequestHandler<GetRolesQuery, Result<PaginatedList<RoleDto>>>
{
    public async Task<Result<PaginatedList<RoleDto>>> Handle(
        GetRolesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The platform scope sees every tenant's rows, so it is narrowed to the platform roles
        // (TenantId = Guid.Empty) - exactly the set user creation under that scope accepts.
        var query = tenantContext.IsPlatformScope
            ? roleManager.Roles.Where(static r => r.TenantId == Guid.Empty)
            : roleManager.Roles.Where(static r => r.TenantId != Guid.Empty);

        if (!tenantContext.IsPlatformScope)
        {
            RoleCommandSupport.RequireTenant(tenantContext);
        }

        if (request.ActiveOnly)
        {
            query = query.Where(static r => r.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            query = query.Where(r =>
                r.Name!.Contains(term)
                || (r.Description != null && r.Description.Contains(term)));
        }

        var page = await query
            .OrderBy(static r => r.Name)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        if (page.Items.Count == 0)
        {
            return Result<PaginatedList<RoleDto>>.Success(
                PaginatedList<RoleDto>.Empty(page.PageNumber, page.PageSize));
        }

        var ids = page.Items.Select(static r => r.Id).ToArray();

        // Permissions and user counts come from Identity's join tables in two batched
        // lookups, rather than two queries per role.
        var permissionsByRole = await accessReader
            .GetPermissionsByRoleIdsAsync(ids, cancellationToken)
            .ConfigureAwait(false);

        var countsByRole = await accessReader
            .GetUserCountsByRoleIdsAsync(ids, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items
            .Select(r => RoleDto.FromEntity(
                r,
                permissionsByRole.GetValueOrDefault(r.Id) ?? [],
                countsByRole.GetValueOrDefault(r.Id)))
            .ToArray();

        return Result<PaginatedList<RoleDto>>.Success(
            new PaginatedList<RoleDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

/// <summary>Fetches a single role of the current tenant.</summary>
public sealed record GetRoleQuery(Guid Id) : IRequest<Result<RoleDto>>;

public sealed class GetRoleQueryHandler(
    RoleManager<ApplicationRole> roleManager,
    ITenantContext tenantContext,
    IUserAccessReader accessReader)
    : IRequestHandler<GetRoleQuery, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(GetRoleQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await RoleCommandSupport
            .FindForTenantAsync(roleManager, tenantContext, request.Id, cancellationToken)
            .ConfigureAwait(false);

        var permissions = await accessReader
            .GetPermissionsByRoleIdsAsync([role.Id], cancellationToken)
            .ConfigureAwait(false);

        var counts = await accessReader
            .GetUserCountsByRoleIdsAsync([role.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result<RoleDto>.Success(
            RoleDto.FromEntity(
                role,
                permissions.GetValueOrDefault(role.Id) ?? [],
                counts.GetValueOrDefault(role.Id)));
    }
}

/// <summary>
/// The permission catalog, grouped by area.
/// </summary>
/// <remarks>
/// Served from <see cref="PermissionConstants"/> rather than the database, so the editor can
/// never offer a permission that authorization would then reject - and a permission added in
/// code appears immediately, with no migration. The platform administrator sees the platform
/// areas; a tenant sees every area except the platform-only ones (managing tenants, plans,
/// platform settings), which no tenant role can hold.
/// </remarks>
public sealed record GetPermissionCatalogQuery : IRequest<Result<IReadOnlyList<PermissionAreaDto>>>;

public sealed class GetPermissionCatalogQueryHandler(ITenantContext tenantContext)
    : IRequestHandler<GetPermissionCatalogQuery, Result<IReadOnlyList<PermissionAreaDto>>>
{
    public Task<Result<IReadOnlyList<PermissionAreaDto>>> Handle(
        GetPermissionCatalogQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var platform = tenantContext.IsPlatformScope;

        IReadOnlyList<PermissionAreaDto> areas =
        [
            .. PermissionConstants.Areas
                .Where(a => platform
                    ? PermissionConstants.PlatformAreaNames.Contains(a.Key)
                    : !PermissionConstants.PlatformOnlyAreaNames.Contains(a.Key))
                .OrderBy(static a => a.Key, StringComparer.Ordinal)
                .Select(area => new PermissionAreaDto(area.Key, area.Value))
        ];

        return Task.FromResult(Result<IReadOnlyList<PermissionAreaDto>>.Success(areas));
    }
}
