using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Teachers.DTOs;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Teachers.Commands;

/// <summary>
/// Creates a compensation rule.
/// </summary>
/// <remarks>
/// <para>
/// The scope is exactly the set of non-null dimensions; all three null makes the tenant
/// default rule, of which there may be only one. Duplicate scopes are rejected with a 409
/// before the filtered unique indexes ever see them, so the caller gets a message instead
/// of a constraint violation.
/// </para>
/// <para>
/// A non-null <c>StudyGroupId</c> must name a study group of the caller's tenant (Mission
/// Phase 3 added the entity and the foreign key).
/// </para>
/// </remarks>
public sealed record CreateCompensationRuleCommand(CreateCompensationRuleRequest Request)
    : IRequest<Result<CompensationRuleDto>>;

public sealed class CreateCompensationRuleCommandHandler(
    IRepository<CompensationRule> rules,
    IRepository<TeacherProfile> profiles,
    IRepository<Subject> subjects,
    IRepository<EducationHub.Domain.Entities.Scheduling.StudyGroup> groups,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCompensationRuleCommand, Result<CompensationRuleDto>>
{
    public async Task<Result<CompensationRuleDto>> Handle(
        CreateCompensationRuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var request = command.Request;

        if (request.StudyGroupId is { } groupId
            && !await groups.ExistsAsync(groupId, cancellationToken).ConfigureAwait(false))
        {
            return Result<CompensationRuleDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        if (request.TeacherProfileId is { } profileId
            && !await profiles.ExistsAsync(profileId, cancellationToken).ConfigureAwait(false))
        {
            return Result<CompensationRuleDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        if (request.SubjectId is { } subjectId
            && !await subjects.ExistsAsync(subjectId, cancellationToken).ConfigureAwait(false))
        {
            return Result<CompensationRuleDto>.Failure("Subjects.NotFound", "Subject not found.");
        }

        // Null-safe on the wire: EF translates a nullable parameter compared to null as
        // IS NULL, so the all-null (tenant default) scope compares correctly.
        var scopeTaken = await rules.Query
            .IgnoreQueryFilters()
            .AnyAsync(
                r => r.TenantId == tenantId
                    && r.TeacherProfileId == request.TeacherProfileId
                    && r.SubjectId == request.SubjectId
                    && r.StudyGroupId == request.StudyGroupId,
                cancellationToken)
            .ConfigureAwait(false);

        if (scopeTaken)
        {
            return Result<CompensationRuleDto>.Failure(
                "Compensation.RuleAlreadyExists",
                "A compensation rule with this scope already exists.");
        }

        var rule = new CompensationRule
        {
            TenantId = tenantId,
            TeacherProfileId = request.TeacherProfileId,
            SubjectId = request.SubjectId,
            StudyGroupId = request.StudyGroupId,
            Rate = request.Rate,
            RateBasis = request.RateBasis,
            IsActive = true
        };

        await rules.AddAsync(rule, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CompensationRuleDto>.Success(
            CompensationRuleDto.FromEntity(rule),
            "Compensation rule created successfully.");
    }
}

/// <summary>
/// Partially updates a compensation rule. The scope (teacher/subject/group dimensions) is
/// immutable after creation - a different scope is a different rule.
/// </summary>
public sealed record UpdateCompensationRuleCommand(Guid Id, UpdateCompensationRuleRequest Request)
    : IRequest<Result<CompensationRuleDto>>;

public sealed class UpdateCompensationRuleCommandHandler(
    IRepository<CompensationRule> rules,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCompensationRuleCommand, Result<CompensationRuleDto>>
{
    public async Task<Result<CompensationRuleDto>> Handle(
        UpdateCompensationRuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var rule = await rules.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return Result<CompensationRuleDto>.Failure(
                "Compensation.NotFound",
                "Compensation rule not found.");
        }

        var request = command.Request;

        if (request.Rate.HasValue)
        {
            rule.Rate = request.Rate.Value;
        }

        if (request.RateBasis.HasValue)
        {
            rule.RateBasis = request.RateBasis.Value;
        }

        if (request.IsActive.HasValue)
        {
            rule.IsActive = request.IsActive.Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CompensationRuleDto>.Success(
            CompensationRuleDto.FromEntity(rule),
            "Compensation rule updated successfully.");
    }
}

/// <summary>Soft-deletes a compensation rule, keeping the rate history for audits.</summary>
public sealed record DeleteCompensationRuleCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteCompensationRuleCommandHandler(
    IRepository<CompensationRule> rules,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCompensationRuleCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteCompensationRuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var rule = await rules.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return Result<bool>.Failure(
                "Compensation.NotFound",
                "Compensation rule not found.");
        }

        rules.SoftRemove(rule);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Compensation rule deleted successfully.");
    }
}
