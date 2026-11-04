using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Academic.DTOs;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Academic.Commands;

public sealed record CreateSubjectCommand(CreateSubjectRequest Request)
    : IRequest<Result<SubjectDto>>;

/// <summary>
/// Creates a subject for the caller's tenant.
/// </summary>
/// <remarks>
/// Subjects are tenant-scoped by design (spec section 16). A global subject table would
/// make every tenant share one id space, so a subject one school adds would appear in
/// another's curriculum, and it could not be edited or deactivated per tenant.
/// </remarks>
public sealed class CreateSubjectCommandHandler(
    IRepository<Subject> subjects,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateSubjectCommand, Result<SubjectDto>>
{
    public async Task<Result<SubjectDto>> Handle(
        CreateSubjectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var request = command.Request;
        var code = Normalize.Required(request.Code, "UNNAMED").ToUpperInvariant();

        var codeTaken = await subjects.Query
            .AnyAsync(s => s.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (codeTaken)
        {
            return Result<SubjectDto>.Failure(
                "Subjects.CodeTaken",
                $"A subject with code '{code}' already exists.");
        }

        var subject = new Subject
        {
            TenantId = tenantId,
            Code = code,
            Name = Normalize.Required(request.Name, "Unnamed subject"),
            NameAr = Normalize.Required(request.NameAr, Normalize.Required(request.Name, "Unnamed subject")),
            Description = Normalize.Optional(request.Description),
            DescriptionAr = Normalize.Optional(request.DescriptionAr),
            IsActive = true,
            SortOrder = request.SortOrder
        };

        await subjects.AddAsync(subject, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<SubjectDto>.Success(
            SubjectDto.FromEntity(subject),
            "Subject created successfully.");
    }
}

public sealed record UpdateSubjectCommand(Guid Id, UpdateSubjectRequest Request)
    : IRequest<Result<SubjectDto>>;

public sealed class UpdateSubjectCommandHandler(
    IRepository<Subject> subjects,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateSubjectCommand, Result<SubjectDto>>
{
    public async Task<Result<SubjectDto>> Handle(
        UpdateSubjectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var subject = await subjects.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (subject is null)
        {
            return Result<SubjectDto>.Failure("Subjects.NotFound", "Subject not found.");
        }

        if (command.Request.Name is not null)
        {
            subject.Name = Normalize.Required(command.Request.Name, subject.Name);
        }

        if (command.Request.NameAr is not null)
        {
            subject.NameAr = Normalize.Required(command.Request.NameAr, subject.Name);
        }

        if (command.Request.Description is not null)
        {
            subject.Description = Normalize.Optional(command.Request.Description);
        }

        if (command.Request.DescriptionAr is not null)
        {
            subject.DescriptionAr = Normalize.Optional(command.Request.DescriptionAr);
        }

        if (command.Request.IsActive.HasValue)
        {
            subject.IsActive = command.Request.IsActive.Value;
        }

        if (command.Request.SortOrder.HasValue)
        {
            subject.SortOrder = command.Request.SortOrder.Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<SubjectDto>.Success(
            SubjectDto.FromEntity(subject),
            "Subject updated successfully.");
    }
}

/// <summary>
/// Soft-deletes a subject.
/// </summary>
/// <remarks>
/// Inactive is the normal way to retire a subject, because timetables and grade books
/// reference it; this endpoint exists for subjects that were entered by mistake and were
/// never used.
/// </remarks>
public sealed record DeleteSubjectCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteSubjectCommandHandler(
    IRepository<Subject> subjects,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteSubjectCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteSubjectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var subject = await subjects.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (subject is null)
        {
            return Result<bool>.Failure("Subjects.NotFound", "Subject not found.");
        }

        subjects.SoftRemove(subject);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Subject deleted successfully.");
    }
}
