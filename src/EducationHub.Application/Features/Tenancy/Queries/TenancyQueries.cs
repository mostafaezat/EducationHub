using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Tenancy.DTOs;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Tenancy.Queries;

/// <summary>Pages all tenants. Platform scope only.</summary>
public sealed record GetTenantsQuery(PaginationRequest Pagination) : IRequest<Result<PaginatedList<TenantDto>>>;

public sealed class GetTenantsQueryHandler(
    IRepository<Tenant> tenants,
    ITenantContext tenantContext,
    IDateTimeService dateTime)
    : IRequestHandler<GetTenantsQuery, Result<PaginatedList<TenantDto>>>
{
    public async Task<Result<PaginatedList<TenantDto>>> Handle(
        GetTenantsQuery request,
        CancellationToken cancellationToken)
    {
        // Without this check a tenant administrator could page through every tenant in the
        // platform, because Tenant carries no query filter of its own.
        if (!tenantContext.IsPlatformScope)
        {
            throw new BusinessRuleException(
                "Tenants.Forbidden",
                "Only a platform administrator can list all tenants.");
        }

        var page = await tenants.Query
            .OrderBy(t => t.Name)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items
            .Select(t => TenantDto.FromEntity(t, dateTime.UtcNow))
            .ToArray();

        return Result<PaginatedList<TenantDto>>.Success(
            new PaginatedList<TenantDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

/// <summary>Fetches one tenant, by id or by slug.</summary>
public sealed record GetTenantQuery(Guid? Id, string? Slug) : IRequest<Result<TenantDto>>;

public sealed class GetTenantQueryHandler(
    IRepository<Tenant> tenants,
    ITenantContext tenantContext,
    IDateTimeService dateTime)
    : IRequestHandler<GetTenantQuery, Result<TenantDto>>
{
    public async Task<Result<TenantDto>> Handle(GetTenantQuery request, CancellationToken cancellationToken)
    {
        var query = tenants.Query.AsQueryable();

        Tenant? tenant;

        if (request.Id.HasValue)
        {
            tenant = await query
                .FirstOrDefaultAsync(t => t.Id == request.Id.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var slug = request.Slug.Trim().ToLowerInvariant();

            tenant = await query
                .FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // No id and no slug: fall back to the caller's own tenant, which is what the
            // tenant settings screen needs.
            var ownTenant = tenantContext.TenantId
                ?? throw new BusinessRuleException("Errors.NoTenantContext", "No tenant context is active.");

            tenant = await query
                .FirstOrDefaultAsync(t => t.Id == ownTenant, cancellationToken)
                .ConfigureAwait(false);
        }

        if (tenant is null)
        {
            return Result<TenantDto>.Failure("Tenants.NotFound", "Tenant not found.");
        }

        // Tenant is not tenant-filtered, so ownership is enforced here instead: a tenant
        // administrator may only read their own tenant.
        if (!tenantContext.IsPlatformScope && tenantContext.TenantId != tenant.Id)
        {
            return Result<TenantDto>.Failure("Tenants.NotFound", "Tenant not found.");
        }

        return Result<TenantDto>.Success(TenantDto.FromEntity(tenant, dateTime.UtcNow));
    }
}

/// <summary>Pages the caller's own branches. The tenant filter scopes these automatically.</summary>
public sealed record GetBranchesQuery(PaginationRequest Pagination, bool ActiveOnly = false) : IRequest<Result<PaginatedList<BranchDto>>>;

public sealed class GetBranchesQueryHandler(
    IRepository<Branch> branches)
    : IRequestHandler<GetBranchesQuery, Result<PaginatedList<BranchDto>>>
{
    public async Task<Result<PaginatedList<BranchDto>>> Handle(
        GetBranchesQuery request,
        CancellationToken cancellationToken)
    {
        var query = branches.Query;

        if (request.ActiveOnly)
        {
            query = query.Where(b => b.Status == Domain.Enums.BranchStatus.Active);
        }

        // Main branch first, then alphabetical: the ordering users actually want, and
        // deterministic so paging does not shuffle.
        var page = await query
            .OrderByDescending(b => b.IsMainBranch)
            .ThenBy(b => b.Name)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items.Select(BranchDto.FromEntity).ToArray();

        return Result<PaginatedList<BranchDto>>.Success(
            new PaginatedList<BranchDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetBranchQuery(Guid Id) : IRequest<Result<BranchDto>>;

public sealed class GetBranchQueryHandler(IRepository<Branch> branches)
    : IRequestHandler<GetBranchQuery, Result<BranchDto>>
{
    public async Task<Result<BranchDto>> Handle(GetBranchQuery request, CancellationToken cancellationToken)
    {
        var branch = await branches.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return branch is null
            ? Result<BranchDto>.Failure("Branches.NotFound", "Branch not found.")
            : Result<BranchDto>.Success(BranchDto.FromEntity(branch));
    }
}

public sealed record GetTenantSettingsQuery : IRequest<Result<TenantSettingsDto>>;

public sealed class GetTenantSettingsQueryHandler(
    IRepository<TenantSettings> settingsRepository,
    IRepository<Tenant> tenants,
    ITenantContext tenantContext)
    : IRequestHandler<GetTenantSettingsQuery, Result<TenantSettingsDto>>
{
    public async Task<Result<TenantSettingsDto>> Handle(
        GetTenantSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        // TenantSettings uses TenantId as its primary key, so a lookup keyed on "Id" would
        // never match - fetch by the actual key through the filtered queryable instead.
        var settings = await settingsRepository.Query
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is null)
        {
            return Result<TenantSettingsDto>.Failure("Settings.NotFound", "Settings were not found for this tenant.");
        }

        var tenantName = await tenants.Query
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;

        return Result<TenantSettingsDto>.Success(TenantSettingsDto.FromEntity(settings, tenantName));
    }
}
