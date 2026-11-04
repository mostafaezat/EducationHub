using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Academic.DTOs;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Academic.Commands;

public sealed record CreateGradeLevelCommand(CreateGradeLevelRequest Request)
    : IRequest<Result<GradeLevelDto>>;

/// <summary>
/// Creates a grade level for the caller's tenant.
/// </summary>
/// <remarks>
/// Grades are per-tenant and user-editable rather than a fixed platform list, because the
/// KG1..Grade 6 range differs between curricula and hard-coding it into business logic would
/// make another tenant unusable.
/// </remarks>
public sealed class CreateGradeLevelCommandHandler(
    IRepository<GradeLevel> grades,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateGradeLevelCommand, Result<GradeLevelDto>>
{
    public async Task<Result<GradeLevelDto>> Handle(
        CreateGradeLevelCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var request = command.Request;
        var code = Normalize.Required(request.Code, "UNNAMED").ToUpperInvariant();

        var codeTaken = await grades.Query
            .AnyAsync(g => g.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (codeTaken)
        {
            return Result<GradeLevelDto>.Failure(
                "Grades.CodeTaken",
                $"A grade level with code '{code}' already exists.");
        }

        var grade = new GradeLevel
        {
            TenantId = tenantId,
            Code = code,
            Name = Normalize.Required(request.Name, "Unnamed grade"),
            NameAr = Normalize.Optional(request.NameAr),
            Order = request.Order,
            IsActive = true,
            Description = Normalize.Optional(request.Description),
            DescriptionAr = Normalize.Optional(request.DescriptionAr)
        };

        await grades.AddAsync(grade, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<GradeLevelDto>.Success(
            GradeLevelDto.FromEntity(grade),
            "Grade level created successfully.");
    }
}

public sealed record UpdateGradeLevelCommand(Guid Id, UpdateGradeLevelRequest Request)
    : IRequest<Result<GradeLevelDto>>;

public sealed class UpdateGradeLevelCommandHandler(
    IRepository<GradeLevel> grades,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateGradeLevelCommand, Result<GradeLevelDto>>
{
    public async Task<Result<GradeLevelDto>> Handle(
        UpdateGradeLevelCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var grade = await grades.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (grade is null)
        {
            return Result<GradeLevelDto>.Failure("Grades.NotFound", "Grade level not found.");
        }

        if (command.Request.Name is not null)
        {
            grade.Name = Normalize.Required(command.Request.Name, grade.Name);
        }

        if (command.Request.NameAr is not null)
        {
            grade.NameAr = Normalize.Optional(command.Request.NameAr);
        }

        if (command.Request.Order.HasValue)
        {
            grade.Order = command.Request.Order.Value;
        }

        if (command.Request.IsActive.HasValue)
        {
            grade.IsActive = command.Request.IsActive.Value;
        }

        if (command.Request.Description is not null)
        {
            grade.Description = Normalize.Optional(command.Request.Description);
        }

        if (command.Request.DescriptionAr is not null)
        {
            grade.DescriptionAr = Normalize.Optional(command.Request.DescriptionAr);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<GradeLevelDto>.Success(
            GradeLevelDto.FromEntity(grade),
            "Grade level updated successfully.");
    }
}

/// <summary>
/// Soft-deletes a grade level.
/// </summary>
/// <remarks>
/// Soft rather than hard because students are enrolled against grades; a hard delete would
/// orphan that history the moment a tenant removed a level it had finished using.
/// </remarks>
public sealed record DeleteGradeLevelCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteGradeLevelCommandHandler(
    IRepository<GradeLevel> grades,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteGradeLevelCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteGradeLevelCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var grade = await grades.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (grade is null)
        {
            return Result<bool>.Failure("Grades.NotFound", "Grade level not found.");
        }

        grades.SoftRemove(grade);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Grade level deleted successfully.");
    }
}
