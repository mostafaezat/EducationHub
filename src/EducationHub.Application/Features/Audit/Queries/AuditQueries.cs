using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Audit.DTOs;
using EducationHub.Domain.Entities.Audit;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Audit.Queries;

/// <summary>
/// Pages audit entries.
/// </summary>
/// <remarks>
/// Deliberately read-only. There is no create, update or delete handler for audit logs
/// anywhere in the Application layer, so no amount of authorization can be bypassed to
/// rewrite history through the API - the only way to write an entry is the save-time
/// interceptor.
/// </remarks>
public sealed record GetAuditLogsQuery(
    PaginationRequest Pagination,
    DateTime? From,
    DateTime? To,
    Guid? UserId,
    AuditAction? Action,
    string? EntityName,
    string? EntityId)
    : IRequest<Result<PaginatedList<AuditLogDto>>>;

public sealed class GetAuditLogsQueryHandler(
    IRepository<AuditLog> logs,
    ITenantContext tenantContext)
    : IRequestHandler<GetAuditLogsQuery, Result<PaginatedList<AuditLogDto>>>
{
    public async Task<Result<PaginatedList<AuditLogDto>>> Handle(
        GetAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // No tenant context means no audit rows. AuditLog is tenant-owned, so the global query
        // filter already enforces that; this check turns an accidental call from outside a
        // tenant into a clear failure rather than an empty page that looks like "no
        // activity".
        if (tenantContext.TenantId is null && !tenantContext.IsPlatformScope)
        {
            return Result<PaginatedList<AuditLogDto>>.Failure(
                "AuditLogs.TenantContextRequired",
                "Audit history requires an active tenant context.");
        }

        var query = logs.Query.AsQueryable();

        if (request.From.HasValue)
        {
            query = query.Where(l => l.OccurredAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(l => l.OccurredAt <= request.To.Value);
        }

        if (request.UserId.HasValue)
        {
            query = query.Where(l => l.UserId == request.UserId.Value);
        }

        if (request.Action.HasValue)
        {
            query = query.Where(l => l.Action == request.Action.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityName))
        {
            var entityName = request.EntityName.Trim();

            query = query.Where(l => l.EntityName == entityName);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityId))
        {
            var entityId = request.EntityId.Trim();

            query = query.Where(l => l.EntityId == entityId);
        }

        var page = await query
            .OrderByDescending(static l => l.OccurredAt)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items.Select(AuditLogDto.FromEntity).ToArray();

        return Result<PaginatedList<AuditLogDto>>.Success(
            new PaginatedList<AuditLogDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

/// <summary>Fetches a single audit entry.</summary>
public sealed record GetAuditLogQuery(Guid Id) : IRequest<Result<AuditLogDto>>;

public sealed class GetAuditLogQueryHandler(
    IRepository<AuditLog> logs,
    ITenantContext tenantContext)
    : IRequestHandler<GetAuditLogQuery, Result<AuditLogDto>>
{
    public async Task<Result<AuditLogDto>> Handle(
        GetAuditLogQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tenantContext.TenantId is null && !tenantContext.IsPlatformScope)
        {
            return Result<AuditLogDto>.Failure(
                "AuditLogs.TenantContextRequired",
                "Audit history requires an active tenant context.");
        }

        var log = await logs.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return log is null
            ? Result<AuditLogDto>.Failure("AuditLogs.NotFound", "Audit entry not found.")
            : Result<AuditLogDto>.Success(AuditLogDto.FromEntity(log));
    }
}

/// <summary>
/// Aggregated activity, grouped by action and user.
/// </summary>
/// <remarks>
/// One grouped query rather than fetching rows and counting them in memory: the activity
/// dashboard covers a date range that can hold hundreds of thousands of entries.
/// </remarks>
public sealed record GetAuditSummaryQuery(DateTime? From, DateTime? To, int Top)
    : IRequest<Result<IReadOnlyList<AuditSummaryDto>>>;

public sealed class GetAuditSummaryQueryHandler(
    IRepository<AuditLog> logs,
    ITenantContext tenantContext)
    : IRequestHandler<GetAuditSummaryQuery, Result<IReadOnlyList<AuditSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<AuditSummaryDto>>> Handle(
        GetAuditSummaryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tenantContext.TenantId is null && !tenantContext.IsPlatformScope)
        {
            return Result<IReadOnlyList<AuditSummaryDto>>.Failure(
                "AuditLogs.TenantContextRequired",
                "Audit history requires an active tenant context.");
        }

        var query = logs.Query.AsQueryable();

        if (request.From.HasValue)
        {
            query = query.Where(l => l.OccurredAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(l => l.OccurredAt <= request.To.Value);
        }

        // Bounded so a caller cannot turn a summary endpoint into an unbounded scan.
        var top = request.Top is > 0 and <= 200 ? request.Top : 50;

        // The grouping itself must stay in SQL (the dashboard covers date ranges that can
        // hold hundreds of thousands of entries), but the DTO is constructed in memory after
        // materialisation: projecting a record constructor straight out of a GroupBy is not
        // something EF Core can translate. An anonymous type inside the query is.
        var grouped = await query
            .GroupBy(l => new { l.Action, l.UserId, l.UserName })
            .Select(g => new
            {
                g.Key.Action,
                g.Key.UserId,
                g.Key.UserName,
                Count = g.Count(),
                LastOccurredAt = g.Max(l => l.OccurredAt)
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.LastOccurredAt)
            .Take(top)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = grouped
            .Select(x => new AuditSummaryDto(x.Action, x.UserId, x.UserName, x.Count, x.LastOccurredAt))
            .ToList();

        return Result<IReadOnlyList<AuditSummaryDto>>.Success(rows);
    }
}

/// <summary>
/// Recent sign-in history for the tenant.
/// </summary>
/// <remarks>
/// Served from <c>LoginHistory</c> rather than the audit log because it answers a different
/// question: not "what changed in the data" but "who tried to get in". It is exposed here so
/// the frontend has a single audit-and-activity surface.
/// </remarks>
public sealed record GetLoginHistoryQuery(
    PaginationRequest Pagination,
    Guid? UserId,
    bool SucceededOnly,
    bool FailedOnly)
    : IRequest<Result<PaginatedList<LoginHistoryDto>>>;

public sealed class GetLoginHistoryQueryHandler(
    IRepository<LoginHistory> history,
    ITenantContext tenantContext)
    : IRequestHandler<GetLoginHistoryQuery, Result<PaginatedList<LoginHistoryDto>>>
{
    public async Task<Result<PaginatedList<LoginHistoryDto>>> Handle(
        GetLoginHistoryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tenantContext.TenantId is null && !tenantContext.IsPlatformScope)
        {
            return Result<PaginatedList<LoginHistoryDto>>.Failure(
                "AuditLogs.TenantContextRequired",
                "Sign-in history requires an active tenant context.");
        }

        var query = history.Query.AsQueryable();

        if (request.UserId.HasValue)
        {
            query = query.Where(h => h.UserId == request.UserId.Value);
        }

        if (request.SucceededOnly)
        {
            query = query.Where(static h => h.Succeeded);
        }

        if (request.FailedOnly)
        {
            query = query.Where(static h => !h.Succeeded);
        }

        var page = await query
            .OrderByDescending(static h => h.AttemptedAt)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items.Select(LoginHistoryDto.FromEntity).ToArray();

        return Result<PaginatedList<LoginHistoryDto>>.Success(
            new PaginatedList<LoginHistoryDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}
