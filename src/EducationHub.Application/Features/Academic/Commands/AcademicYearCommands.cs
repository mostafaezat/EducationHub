using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Academic.DTOs;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Academic.Commands;

/// <summary>
/// Shared rules for the academic-structure commands.
/// </summary>
internal static class AcademicCommandSupport
{
    /// <summary>
    /// Clears the current-year flag from every other year of the tenant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly one year per tenant may be current: the whole academic side of the product -
    /// enrolment, attendance, fees, exams - resolves against it. Two current years would
    /// make every one of those queries ambiguous.
    /// </para>
    /// <para>
    /// This runs in the same unit of work as the caller's own write, so the flip and the
    /// insert or update land together and a failure leaves no half-applied state.
    /// </para>
    /// </remarks>
    internal static async Task ClearCurrentAsync(
        IQueryable<AcademicYear> query,
        Guid exceptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var others = await query
            .Where(y => y.IsCurrent && y.Id != exceptId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (others.Count == 0)
        {
            return;
        }

        foreach (var year in others)
        {
            year.IsCurrent = false;

            // Status follows the flag, otherwise the row would claim to be archived while
            // still marked as the live year.
            if (year.Status == AcademicYearStatus.Current)
            {
                year.Status = AcademicYearStatus.Upcoming;
            }
        }
    }

    /// <summary>Rejects overlapping academic years within the same tenant.</summary>
    /// <remarks>
    /// Two years covering the same date would make "which year contains 2026-10-01?"
    /// ambiguous, and that question is asked constantly by the reporting and enrolment code.
    /// </remarks>
    internal static async Task<IReadOnlyList<string>> FindOverlapsAsync(
        IQueryable<AcademicYear> query,
        DateOnly start,
        DateOnly end,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var overlaps = await query
            .Where(y => y.StartDate <= end && y.EndDate >= start
                && (exceptId == null || y.Id != exceptId.Value))
            .OrderBy(static y => y.StartDate)
            .Select(static y => y.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return overlaps;
    }

    /// <summary>Refuses edits that would break the archived history contract.</summary>
    internal static void EnsureMutable(AcademicYear year, string action)
    {
        ArgumentNullException.ThrowIfNull(year);

        if (year.IsArchived)
        {
            throw new BusinessRuleException(
                "AcademicYears.Archived",
                $"An archived academic year cannot be {action}. Archived years are kept intact so " +
                "historical enrolment, attendance and fee records stay meaningful.");
        }
    }
}

public sealed record CreateAcademicYearCommand(CreateAcademicYearRequest Request)
    : IRequest<Result<AcademicYearDto>>;

public sealed class CreateAcademicYearCommandHandler(
    IRepository<AcademicYear> years,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateAcademicYearCommand, Result<AcademicYearDto>>
{
    public async Task<Result<AcademicYearDto>> Handle(
        CreateAcademicYearCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var request = command.Request;

        if (request.EndDate < request.StartDate)
        {
            return Result<AcademicYearDto>.Failure(
                "AcademicYears.InvalidRange",
                "The end date cannot be earlier than the start date.");
        }

        var overlaps = await AcademicCommandSupport
            .FindOverlapsAsync(years.Query, request.StartDate, request.EndDate, null, cancellationToken)
            .ConfigureAwait(false);

        if (overlaps.Count > 0)
        {
            return Result<AcademicYearDto>.Failure(
                "AcademicYears.OverlappingDates",
                $"These dates overlap an existing academic year: {string.Join(", ", overlaps)}.");
        }

        var year = new AcademicYear
        {
            TenantId = tenantId,
            Name = Normalize.Required(request.Name, "Unnamed academic year"),
            NameAr = Normalize.Optional(request.NameAr),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = request.IsCurrent ? AcademicYearStatus.Current : AcademicYearStatus.Upcoming,
            IsCurrent = request.IsCurrent,
            IsArchived = false
        };

        if (request.IsCurrent)
        {
            await AcademicCommandSupport
                .ClearCurrentAsync(years.Query, year.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        await years.AddAsync(year, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<AcademicYearDto>.Success(
            AcademicYearDto.FromEntity(year),
            "Academic year created successfully.");
    }
}

public sealed record UpdateAcademicYearCommand(Guid Id, UpdateAcademicYearRequest Request)
    : IRequest<Result<AcademicYearDto>>;

public sealed class UpdateAcademicYearCommandHandler(
    IRepository<AcademicYear> years,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateAcademicYearCommand, Result<AcademicYearDto>>
{
    public async Task<Result<AcademicYearDto>> Handle(
        UpdateAcademicYearCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var year = await years.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (year is null)
        {
            return Result<AcademicYearDto>.Failure("AcademicYears.NotFound", "Academic year not found.");
        }

        if (year.IsArchived)
        {
            return Result<AcademicYearDto>.Failure(
                "AcademicYears.Archived",
                "An archived academic year cannot be edited.");
        }

        var start = command.Request.StartDate ?? year.StartDate;
        var end = command.Request.EndDate ?? year.EndDate;

        if (end < start)
        {
            return Result<AcademicYearDto>.Failure(
                "AcademicYears.InvalidRange",
                "The end date cannot be earlier than the start date.");
        }

        if (start != year.StartDate || end != year.EndDate)
        {
            var overlaps = await AcademicCommandSupport
                .FindOverlapsAsync(years.Query, start, end, year.Id, cancellationToken)
                .ConfigureAwait(false);

            if (overlaps.Count > 0)
            {
                return Result<AcademicYearDto>.Failure(
                    "AcademicYears.OverlappingDates",
                    $"These dates overlap an existing academic year: {string.Join(", ", overlaps)}.");
            }
        }

        if (command.Request.Name is not null)
        {
            year.Name = Normalize.Required(command.Request.Name, year.Name);
        }

        if (command.Request.NameAr is not null)
        {
            year.NameAr = Normalize.Optional(command.Request.NameAr);
        }

        year.StartDate = start;
        year.EndDate = end;

        if (command.Request.Status is { } status)
        {
            if (status == AcademicYearStatus.Current)
            {
                year.IsCurrent = true;

                await AcademicCommandSupport
                    .ClearCurrentAsync(years.Query, year.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                year.IsCurrent = false;

                // Archiving is the point at which the year stops being open for new
                // enrolment, so the flag and the status must agree.
                year.IsArchived = status == AcademicYearStatus.Archived;
                year.Status = status;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<AcademicYearDto>.Success(
            AcademicYearDto.FromEntity(year),
            "Academic year updated successfully.");
    }
}

/// <summary>
/// Sets a year as the tenant's current one, clearing the flag everywhere else.
/// </summary>
public sealed record SetCurrentAcademicYearCommand(Guid Id) : IRequest<Result<AcademicYearDto>>;

public sealed class SetCurrentAcademicYearCommandHandler(
    IRepository<AcademicYear> years,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SetCurrentAcademicYearCommand, Result<AcademicYearDto>>
{
    public async Task<Result<AcademicYearDto>> Handle(
        SetCurrentAcademicYearCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var year = await years.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (year is null)
        {
            return Result<AcademicYearDto>.Failure("AcademicYears.NotFound", "Academic year not found.");
        }

        if (year.IsArchived)
        {
            return Result<AcademicYearDto>.Failure(
                "AcademicYears.Archived",
                "An archived academic year cannot become the current year.");
        }

        year.IsCurrent = true;
        year.Status = AcademicYearStatus.Current;

        await AcademicCommandSupport
            .ClearCurrentAsync(years.Query, year.Id, cancellationToken)
            .ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<AcademicYearDto>.Success(
            AcademicYearDto.FromEntity(year),
            "Current academic year updated successfully.");
    }
}

/// <summary>
/// Soft-deletes an academic year.
/// </summary>
/// <remarks>
/// Soft, not hard: reports and enrolment history reference these rows, and a hard delete
/// would either fail on the foreign key or silently orphan history.
/// </remarks>
public sealed record DeleteAcademicYearCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteAcademicYearCommandHandler(
    IRepository<AcademicYear> years,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteAcademicYearCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteAcademicYearCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var year = await years.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (year is null)
        {
            return Result<bool>.Failure("AcademicYears.NotFound", "Academic year not found.");
        }

        if (year.IsCurrent)
        {
            // Deleting the live year would break every current-period calculation until
            // somebody noticed and created a replacement.
            return Result<bool>.Failure(
                "AcademicYears.IsCurrent",
                "The current academic year cannot be deleted. Set another year as current first.");
        }

        years.SoftRemove(year);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Academic year deleted successfully.");
    }
}
