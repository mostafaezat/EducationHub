using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Application.Features.Students.Services;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Students.Commands;

/// <summary>Shared checks for the student commands.</summary>
internal static class StudentCommandSupport
{
    public static string? NormalizeStudentNumber(string? value)
        => Normalize.Optional(value)?.ToUpperInvariant();

    /// <summary>
    /// True when another profile of the tenant (deleted ones included, matching the unique
    /// index) already carries <paramref name="studentNumber"/>.
    /// </summary>
    public static Task<bool> StudentNumberTakenAsync(
        IRepository<StudentProfile> students,
        Guid tenantId,
        string studentNumber,
        Guid? exceptId,
        CancellationToken cancellationToken)
        => students.Query
            .IgnoreQueryFilters()
            .AnyAsync(
                s => s.TenantId == tenantId
                    && s.StudentNumber == studentNumber
                    && (!exceptId.HasValue || s.Id != exceptId.Value),
                cancellationToken);

    /// <summary>Checks that referenced grade and branch exist in this tenant.</summary>
    public static async Task<Result<StudentProfileDto>?> CheckReferencesAsync(
        IRepository<GradeLevel> grades,
        IRepository<Branch> branches,
        Guid? gradeLevelId,
        Guid? branchId,
        CancellationToken cancellationToken)
    {
        if (gradeLevelId is { } gradeId
            && !await grades.ExistsAsync(gradeId, cancellationToken).ConfigureAwait(false))
        {
            return Result<StudentProfileDto>.Failure(
                "Students.UnknownGradeLevel",
                "The grade level does not exist in this tenant.");
        }

        if (branchId is { } bId
            && !await branches.ExistsAsync(bId, cancellationToken).ConfigureAwait(false))
        {
            return Result<StudentProfileDto>.Failure(
                "Students.UnknownBranch",
                "The branch does not exist in this tenant.");
        }

        return null;
    }
}

/// <summary>
/// Creates a student profile and, atomically, optionally links or creates its account.
/// </summary>
public sealed record CreateStudentCommand(CreateStudentRequest Request) : IRequest<Result<StudentProfileDto>>;

public sealed class CreateStudentCommandHandler(
    IRepository<StudentProfile> students,
    IRepository<GradeLevel> grades,
    IRepository<Branch> branches,
    ProfileAccountSupport accounts,
    StudentReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateStudentCommand, Result<StudentProfileDto>>
{
    public async Task<Result<StudentProfileDto>> Handle(
        CreateStudentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await unitOfWork.ExecuteInTransactionAsync(
            async ct => await CreateAsync(command.Request, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<StudentProfileDto>> CreateAsync(
        CreateStudentRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var fullName = Normalize.Required(request.FullName, string.Empty);

        var studentNumber = StudentCommandSupport.NormalizeStudentNumber(request.StudentNumber);

        if (studentNumber is not null
            && await StudentCommandSupport
                .StudentNumberTakenAsync(students, tenantId, studentNumber, null, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result<StudentProfileDto>.Failure(
                "Students.StudentNumberTaken",
                $"A student number '{studentNumber}' already exists in this tenant.");
        }

        var referenceFailure = await StudentCommandSupport
            .CheckReferencesAsync(grades, branches, request.GradeLevelId, request.BranchId, cancellationToken)
            .ConfigureAwait(false);

        if (referenceFailure is not null)
        {
            return referenceFailure;
        }

        AccountResolution account;

        if (request.UserId is { } userId)
        {
            account = await accounts.LinkAsync(userId, tenantId, "Students", cancellationToken).ConfigureAwait(false);

            if (account.Failure is null
                && await students.Query.AnyAsync(s => s.UserId == userId, cancellationToken).ConfigureAwait(false))
            {
                return Result<StudentProfileDto>.Failure(
                    "Students.ProfileAlreadyExists",
                    "This user already has a student profile.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.AccountEmail))
        {
            account = await accounts.CreateAsync(
                    request.AccountEmail,
                    request.AccountPassword ?? string.Empty,
                    fullName,
                    phoneNumber: null,
                    RoleNames.Student,
                    tenantId,
                    "Students",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            account = AccountResolution.None;
        }

        if (account.Failure is { } failure)
        {
            return failure.ToResult<StudentProfileDto>();
        }

        var profile = new StudentProfile
        {
            TenantId = tenantId,
            UserId = account.User?.Id,
            FullName = fullName,
            StudentNumber = studentNumber,
            DateOfBirth = request.DateOfBirth,
            GradeLevelId = request.GradeLevelId,
            BranchId = request.BranchId,
            EnrollmentDate = request.EnrollmentDate,
            Status = StudentStatus.Active,
            Notes = Normalize.Optional(request.Notes)
        };

        await students.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<StudentProfileDto>.Success(
            await readModel.BuildStudentAsync(profile, cancellationToken).ConfigureAwait(false),
            "Student created successfully.");
    }
}

/// <summary>Partially updates a student's profile. The linked account is not touched.</summary>
public sealed record UpdateStudentCommand(Guid Id, UpdateStudentRequest Request)
    : IRequest<Result<StudentProfileDto>>;

public sealed class UpdateStudentCommandHandler(
    IRepository<StudentProfile> students,
    IRepository<GradeLevel> grades,
    IRepository<Branch> branches,
    StudentReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateStudentCommand, Result<StudentProfileDto>>
{
    public async Task<Result<StudentProfileDto>> Handle(
        UpdateStudentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var profile = await students.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<StudentProfileDto>.Failure("Students.NotFound", "Student not found.");
        }

        var request = command.Request;

        if (request.StudentNumber is not null)
        {
            // "" clears the number, the same convention teachers use.
            var studentNumber = StudentCommandSupport.NormalizeStudentNumber(request.StudentNumber);

            if (studentNumber is not null
                && await StudentCommandSupport
                    .StudentNumberTakenAsync(students, tenantId, studentNumber, profile.Id, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Result<StudentProfileDto>.Failure(
                    "Students.StudentNumberTaken",
                    $"A student number '{studentNumber}' already exists in this tenant.");
            }

            profile.StudentNumber = studentNumber;
        }

        var referenceFailure = await StudentCommandSupport
            .CheckReferencesAsync(grades, branches, request.GradeLevelId, request.BranchId, cancellationToken)
            .ConfigureAwait(false);

        if (referenceFailure is not null)
        {
            return referenceFailure;
        }

        if (request.FullName is not null)
        {
            profile.FullName = Normalize.Required(request.FullName, profile.FullName);
        }

        if (request.DateOfBirth.HasValue)
        {
            profile.DateOfBirth = request.DateOfBirth;
        }

        if (request.GradeLevelId.HasValue)
        {
            profile.GradeLevelId = request.GradeLevelId;
        }

        if (request.BranchId.HasValue)
        {
            profile.BranchId = request.BranchId;
        }

        if (request.Status.HasValue)
        {
            profile.Status = request.Status.Value;
        }

        if (request.EnrollmentDate.HasValue)
        {
            profile.EnrollmentDate = request.EnrollmentDate;
        }

        if (request.Notes is not null)
        {
            profile.Notes = Normalize.Optional(request.Notes);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<StudentProfileDto>.Success(
            await readModel.BuildStudentAsync(profile, cancellationToken).ConfigureAwait(false),
            "Student updated successfully.");
    }
}

/// <summary>
/// Replaces a student's guardian links. At most one link may be primary.
/// </summary>
/// <remarks>
/// Old links are hard-removed (the junction's unique index has no <c>IsDeleted</c> filter),
/// matching how teacher subject links are replaced.
/// </remarks>
public sealed record SetStudentGuardiansCommand(Guid Id, SetStudentGuardiansRequest Request)
    : IRequest<Result<StudentProfileDto>>;

public sealed class SetStudentGuardiansCommandHandler(
    IRepository<StudentProfile> students,
    IRepository<ParentProfile> parents,
    IRepository<StudentGuardian> guardians,
    StudentReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SetStudentGuardiansCommand, Result<StudentProfileDto>>
{
    public async Task<Result<StudentProfileDto>> Handle(
        SetStudentGuardiansCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var profile = await students.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<StudentProfileDto>.Failure("Students.NotFound", "Student not found.");
        }

        var requested = command.Request.Guardians ?? [];

        if (requested.Count(static g => g.IsPrimary) > 1)
        {
            return Result<StudentProfileDto>.Failure(
                "Students.MultiplePrimaryGuardians",
                "Only one guardian can be the primary contact.");
        }

        var parentIds = requested.Select(static g => g.ParentProfileId).Distinct().ToArray();

        if (parentIds.Length > 0)
        {
            var known = await parents.Query
                .CountAsync(p => parentIds.Contains(p.Id), cancellationToken)
                .ConfigureAwait(false);

            if (known != parentIds.Length)
            {
                return Result<StudentProfileDto>.Failure(
                    "Students.UnknownParent",
                    "One or more parents do not exist in this tenant.");
            }
        }

        var existing = await guardians.Query
            .Where(g => g.StudentProfileId == profile.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var link in existing)
        {
            guardians.Remove(link);
        }

        foreach (var link in requested)
        {
            await guardians.AddAsync(
                    new StudentGuardian
                    {
                        TenantId = tenantId,
                        StudentProfileId = profile.Id,
                        ParentProfileId = link.ParentProfileId,
                        Relationship = link.Relationship,
                        IsPrimary = link.IsPrimary
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<StudentProfileDto>.Success(
            await readModel.BuildStudentAsync(profile, cancellationToken).ConfigureAwait(false),
            "Student guardians updated successfully.");
    }
}

/// <summary>
/// Soft-deletes a student and hard-removes their guardian links. The linked account (if any)
/// is left to the Users feature.
/// </summary>
public sealed record DeleteStudentCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteStudentCommandHandler(
    IRepository<StudentProfile> students,
    IRepository<StudentGuardian> guardians,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteStudentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteStudentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var profile = await students.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<bool>.Failure("Students.NotFound", "Student not found.");
        }

        var links = await guardians.Query
            .Where(g => g.StudentProfileId == profile.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var link in links)
        {
            guardians.Remove(link);
        }

        students.SoftRemove(profile);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Student deleted successfully.");
    }
}
