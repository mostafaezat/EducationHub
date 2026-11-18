using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Messaging;
using EducationHub.Application.Features.Students.Commands;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Students.Services;

/// <summary>
/// The one path that registers a student: the profile, its optional account and the optional
/// guardian (an existing parent matched by e-mail or mobile number is reused; otherwise a parent
/// is created, with a sign-in account when asked). Used by <c>POST /api/students</c> and by the
/// approval of a pre-registration request.
/// </summary>
/// <remarks>
/// The caller owns the permission check and the transaction: <see cref="CreateAsync"/> must run
/// inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/> so a failed guardian rolls
/// the student back, and <see cref="AnnounceGuardianCredentialsAsync"/> must run after it committed.
/// </remarks>
public sealed class StudentRegistrationService(
    IRepository<StudentProfile> students,
    IRepository<GradeLevel> grades,
    IRepository<Branch> branches,
    IRepository<ParentProfile> parents,
    IRepository<StudentGuardian> guardians,
    ProfileAccountSupport accounts,
    StudentReadModel readModel,
    WhatsAppMessenger whatsApp,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
{
    /// <summary>Creates the student (and guardian); saves but does not commit.</summary>
    public async Task<Result<StudentProfileDto>> CreateAsync(
        CreateStudentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        NewGuardianAccountDto? newGuardian = null;

        if (request.Guardian is { } guardianRequest)
        {
            var guardianResult = await RegisterGuardianAsync(guardianRequest, profile, tenantId, cancellationToken).ConfigureAwait(false);

            if (guardianResult.Failure is { } guardianFailure)
            {
                // A failed result rolls the whole transaction back: no half-registered student.
                return guardianFailure.ToResult<StudentProfileDto>();
            }

            newGuardian = guardianResult.Guardian;
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var dto = await readModel.BuildStudentAsync(profile, cancellationToken).ConfigureAwait(false);

        return Result<StudentProfileDto>.Success(
            dto with { NewGuardian = newGuardian },
            "Student created successfully.");
    }

    /// <summary>
    /// Sends a newly created parent account's sign-in details by WhatsApp (best-effort) and marks
    /// the result as sent. Call only after the registration committed.
    /// </summary>
    public async Task<Result<StudentProfileDto>> AnnounceGuardianCredentialsAsync(
        Result<StudentProfileDto> result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Succeeded
            && result.Data is { NewGuardian: { TemporaryPassword: { } password } guardian } student
            && guardian.PhoneNumber is not null)
        {
            var sent = await whatsApp
                .SendGuardianCredentialsAsync(guardian.PhoneNumber, guardian.FullName, guardian.LoginName ?? guardian.PhoneNumber, password, cancellationToken)
                .ConfigureAwait(false);

            if (sent)
            {
                return Result<StudentProfileDto>.Success(
                    student with { NewGuardian = guardian with { WhatsAppSent = true } },
                    result.Message);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the tenant's parent with the guardian's e-mail, or creates one, optionally gives the
    /// parent a sign-in account with a temporary password, and links them as primary guardian.
    /// </summary>
    private async Task<(NewGuardianAccountDto? Guardian, AccountFailure? Failure)> RegisterGuardianAsync(
        CreateStudentGuardianRequest request,
        StudentProfile student,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var email = Normalize.Optional(request.Email);
        var phone = PhoneNumberFormat.ToCanonical(request.PhoneNumber);

        // Same parent registered again (a second child): matched by e-mail, or by mobile number
        // when the guardian has no e-mail.
        ParentProfile? parent = null;

        if (email is not null)
        {
            parent = await parents.Query
                .FirstOrDefaultAsync(p => p.Email != null && p.Email == email, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (phone is not null)
        {
            var variants = PhoneNumberFormat.LookupVariants(phone).ToArray();

            parent = await parents.Query
                .FirstOrDefaultAsync(p => p.PhoneNumber != null && variants.Contains(p.PhoneNumber), cancellationToken)
                .ConfigureAwait(false);
        }

        var linkedExisting = parent is not null;

        if (parent is null)
        {
            parent = new ParentProfile
            {
                TenantId = tenantId,
                FullName = Normalize.Required(request.FullName, string.Empty),
                PhoneNumber = phone ?? Normalize.Optional(request.PhoneNumber),
                Email = email
            };

            await parents.AddAsync(parent, cancellationToken).ConfigureAwait(false);
        }

        string? temporaryPassword = null;
        var accountCreated = false;

        var accountPhone = PhoneNumberFormat.ToCanonical(parent.PhoneNumber);
        var accountEmail = parent.Email ?? email;

        if (request.CreateAccount && parent.UserId is null)
        {
            if (accountEmail is null && accountPhone is null)
            {
                return (null, new AccountFailure(
                    "Parents.AccountEmailRequired",
                    "An e-mail address or a mobile number is required to create the parent's sign-in account.",
                    null));
            }

            // Identity needs a unique e-mail; a parent who signs in with a mobile number only
            // gets an internal address that can never receive mail.
            var signInEmail = accountEmail ?? PhoneNumberFormat.PlaceholderEmail(accountPhone!);

            var (account, password) = await accounts
                .CreateWithTemporaryPasswordAsync(signInEmail, parent.FullName, parent.PhoneNumber, RoleNames.Parent, tenantId, "Parents", cancellationToken)
                .ConfigureAwait(false);

            if (account.Failure is { } failure)
            {
                return (null, failure);
            }

            parent.UserId = account.User!.Id;
            temporaryPassword = password;
            accountCreated = true;
        }

        await guardians.AddAsync(
                new StudentGuardian
                {
                    TenantId = tenantId,
                    StudentProfileId = student.Id,
                    ParentProfileId = parent.Id,
                    Relationship = request.Relationship,
                    IsPrimary = true
                },
                cancellationToken)
            .ConfigureAwait(false);

        return (new NewGuardianAccountDto(
                parent.Id,
                parent.FullName,
                parent.Email,
                accountPhone,
                accountEmail ?? accountPhone,
                linkedExisting,
                accountCreated,
                temporaryPassword),
            null);
    }
}
