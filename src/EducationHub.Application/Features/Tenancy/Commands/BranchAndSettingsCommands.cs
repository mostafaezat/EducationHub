using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.DTOs;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Tenancy.Commands;

public sealed record CreateBranchCommand(CreateBranchRequest Request) : IRequest<Result<BranchDto>>;

/// <summary>
/// Creates a branch for the caller's own tenant.
/// </summary>
/// <remarks>
/// <see cref="Branch"/> IS tenant-owned, so the global query filter scopes reads
/// automatically. The insert path is safe too: <c>EducationHubDbContext.SaveChanges</c>
/// stamps <c>TenantId</c> from the request context and rejects a value that contradicts it,
/// so a client-supplied tenant id cannot be used to write into another tenant. This handler
/// therefore never reads a tenant id from the request at all.
/// </remarks>
public sealed class CreateBranchCommandHandler(
    IRepository<Branch> branches,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateBranchCommand, Result<BranchDto>>
{
    public async Task<Result<BranchDto>> Handle(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var request = command.Request;
        var code = Normalize.Optional(request.Code);

        if (code is not null)
        {
            var codeTaken = await branches.Query
                .AnyAsync(b => b.Code == code, cancellationToken)
                .ConfigureAwait(false);

            if (codeTaken)
            {
                return Result<BranchDto>.Failure(
                    "Branches.CodeTaken",
                    $"A branch with code '{code}' already exists.");
            }
        }

        var branch = new Branch
        {
            TenantId = tenantId,
            Name = Normalize.Required(request.Name, "Unnamed branch"),
            NameAr = Normalize.Optional(request.NameAr),
            Code = code,
            Email = Normalize.Email(request.Email),
            Phone = Normalize.Optional(request.Phone),
            Address = Normalize.Optional(request.Address),
            AddressAr = Normalize.Optional(request.AddressAr),
            City = Normalize.Optional(request.City),
            Status = BranchStatus.Active,
            IsMainBranch = request.IsMainBranch
        };

        await branches.AddAsync(branch, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<BranchDto>.Success(BranchDto.FromEntity(branch), "Branch created successfully.");
    }
}

public sealed record UpdateBranchCommand(Guid Id, UpdateBranchRequest Request) : IRequest<Result<BranchDto>>;

public sealed class UpdateBranchCommandHandler(
    IRepository<Branch> branches,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateBranchCommand, Result<BranchDto>>
{
    public async Task<Result<BranchDto>> Handle(UpdateBranchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        // The tenant filter already excludes other tenants' rows, so a miss here means "does
        // not exist for you" and no separate ownership check is needed.
        var branch = await branches.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (branch is null)
        {
            return Result<BranchDto>.Failure("Branches.NotFound", "Branch not found.");
        }

        var request = command.Request;

        branch.Name = Normalize.Optional(request.Name) ?? branch.Name;
        branch.NameAr = Normalize.Optional(request.NameAr) ?? branch.NameAr;
        branch.Email = Normalize.Email(request.Email) ?? branch.Email;
        branch.Phone = Normalize.Optional(request.Phone) ?? branch.Phone;
        branch.Address = Normalize.Optional(request.Address) ?? branch.Address;
        branch.AddressAr = Normalize.Optional(request.AddressAr) ?? branch.AddressAr;
        branch.City = Normalize.Optional(request.City) ?? branch.City;

        if (request.Code is not null)
        {
            var code = Normalize.Optional(request.Code);

            if (code != branch.Code)
            {
                var codeTaken = await branches.Query
                    .AnyAsync(b => b.Code == code && b.Id != branch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (codeTaken)
                {
                    return Result<BranchDto>.Failure(
                        "Branches.CodeTaken",
                        $"A branch with code '{code}' already exists.");
                }

                branch.Code = code;
            }
        }

        if (request.Status.HasValue)
        {
            branch.Status = request.Status.Value;
        }

        if (request.IsMainBranch.HasValue)
        {
            // Only one main branch per tenant: demote the incumbent so "main" stays
            // unambiguous. A filtered unique index enforces this at the database too.
            if (request.IsMainBranch.Value && !branch.IsMainBranch)
            {
                var previousMain = await branches.Query
                    .Where(b => b.IsMainBranch && b.Id != branch.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var other in previousMain)
                {
                    other.IsMainBranch = false;
                }
            }

            branch.IsMainBranch = request.IsMainBranch.Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<BranchDto>.Success(BranchDto.FromEntity(branch), "Branch updated successfully.");
    }
}

public sealed record DeleteBranchCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteBranchCommandHandler(
    IRepository<Branch> branches,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteBranchCommand, Result>
{
    public async Task<Result> Handle(DeleteBranchCommand command, CancellationToken cancellationToken)
    {
        BranchCommandSupport.RequireTenantId(tenantContext);

        var branch = await branches.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (branch is null)
        {
            return Result.Failure("Branches.NotFound", "Branch not found.");
        }

        branches.SoftRemove(branch);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success("Branch deleted successfully.");
    }
}

public sealed record UpdateTenantSettingsCommand(UpdateTenantSettingsRequest Request) : IRequest<Result<TenantSettingsDto>>;

public sealed class UpdateTenantSettingsCommandHandler(
    IRepository<TenantSettings> settingsRepository,
    IRepository<Tenant> tenants,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateTenantSettingsCommand, Result<TenantSettingsDto>>
{
    public async Task<Result<TenantSettingsDto>> Handle(
        UpdateTenantSettingsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        // TenantSettings uses TenantId as its primary key, so GetByIdAsync (keyed on "Id")
        // can never return the row - fetch by the actual key through the filtered queryable.
        var entity = await settingsRepository.Query
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return Result<TenantSettingsDto>.Failure(
                "Settings.NotFound",
                "Settings were not found for this tenant.");
        }

        var request = command.Request;

        entity.NotifyParentOnAbsence = request.NotifyParentOnAbsence ?? entity.NotifyParentOnAbsence;
        entity.NotifyParentOnSessionCancellation = request.NotifyParentOnSessionCancellation ?? entity.NotifyParentOnSessionCancellation;
        entity.LateThresholdMinutes = request.LateThresholdMinutes ?? entity.LateThresholdMinutes;
        entity.RequireExcuseReason = request.RequireExcuseReason ?? entity.RequireExcuseReason;
        entity.AutoGenerateMonthlyFees = request.AutoGenerateMonthlyFees ?? entity.AutoGenerateMonthlyFees;
        entity.FeeDueDayOfMonth = request.FeeDueDayOfMonth ?? entity.FeeDueDayOfMonth;
        entity.GracePeriodDays = request.GracePeriodDays ?? entity.GracePeriodDays;
        entity.SiblingDiscountPercentage = request.SiblingDiscountPercentage ?? entity.SiblingDiscountPercentage;
        entity.DefaultPassingPercentage = request.DefaultPassingPercentage ?? entity.DefaultPassingPercentage;
        entity.UseCustomGradingScale = request.UseCustomGradingScale ?? entity.UseCustomGradingScale;
        entity.NotifyParentOnFeeDue = request.NotifyParentOnFeeDue ?? entity.NotifyParentOnFeeDue;
        entity.NotifyParentOnPaymentReceived = request.NotifyParentOnPaymentReceived ?? entity.NotifyParentOnPaymentReceived;
        entity.NotifyParentOnHomeworkAssigned = request.NotifyParentOnHomeworkAssigned ?? entity.NotifyParentOnHomeworkAssigned;
        entity.NotifyParentOnExamResultPublished = request.NotifyParentOnExamResultPublished ?? entity.NotifyParentOnExamResultPublished;
        entity.NotifyParentOnMakeupClassScheduled = request.NotifyParentOnMakeupClassScheduled ?? entity.NotifyParentOnMakeupClassScheduled;
        entity.DateFormat = Normalize.Optional(request.DateFormat) ?? entity.DateFormat;
        entity.CurrencySymbol = Normalize.Optional(request.CurrencySymbol) ?? entity.CurrencySymbol;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var tenantName = await tenants.Query
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;

        return Result<TenantSettingsDto>.Success(
            TenantSettingsDto.FromEntity(entity, tenantName),
            "Settings updated successfully.");
    }
}

/// <summary>
/// Returns the caller's tenant id, failing loudly when there is none.
/// </summary>
/// <remarks>
/// Every tenant-owned command starts with this. It exists as one shared method so that
/// "what do we do with no tenant context?" is answered in exactly one place - by failing,
/// never by falling back to a default tenant.
/// </remarks>
internal static class BranchCommandSupport
{
    public static Guid RequireTenantId(ITenantContext tenantContext)
        => tenantContext.TenantId
            ?? throw new BusinessRuleException(
                "Errors.NoTenantContext",
                "This operation requires an active tenant context.");
}
