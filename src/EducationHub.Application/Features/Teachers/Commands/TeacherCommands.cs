using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Academic.DTOs;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Teachers.DTOs;
using EducationHub.Application.Features.Users.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Teachers.Commands;

/// <summary>
/// Creates a teacher: a profile row plus, atomically, either a brand-new Identity account
/// carrying the tenant's Teacher role or a link to an account that already exists.
/// </summary>
/// <remarks>
/// <para>
/// Everything runs inside one transaction, so a failure at any step leaves neither a
/// profile nor a half-created user behind - the same all-or-nothing contract tenant
/// provisioning uses. The request DTO carries no tenant id; the tenant comes from
/// <see cref="ITenantContext"/> and the save-time rule provides the second check.
/// </para>
/// <para>
/// The two creation modes are mutually exclusive: <c>UserId</c> links an existing account
/// (its owner keeps their identity data untouched), while <c>Email</c>/<c>Password</c>
/// (<c>FullName</c> etc.) builds a fresh account. The request validator enforces the split
/// so a client can never both link and create.
/// </para>
/// </remarks>
public sealed record CreateTeacherCommand(CreateTeacherRequest Request) : IRequest<Result<TeacherProfileDto>>;

public sealed class CreateTeacherCommandHandler(
    IRepository<TeacherProfile> profiles,
    IRepository<Subject> subjects,
    IRepository<ApplicationRole> roles,
    UserManager<ApplicationUser> userManager,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTeacherCommand, Result<TeacherProfileDto>>
{
    public async Task<Result<TeacherProfileDto>> Handle(
        CreateTeacherCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await unitOfWork.ExecuteInTransactionAsync(
            async ct => await CreateAsync(command.Request, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<TeacherProfileDto>> CreateAsync(
        CreateTeacherRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var resolved = await ResolveUserAsync(request, tenantId, cancellationToken).ConfigureAwait(false);

        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var account = resolved.User!;
        var employeeNumber = NormalizeOptionalEmployeeNumber(request.EmployeeNumber);

        if (employeeNumber is not null)
        {
            // IgnoreQueryFilters + explicit tenant: soft-deleted rows still occupy their
            // number, but another tenant's number is not this tenant's business.
            var numberTaken = await profiles.Query
                .IgnoreQueryFilters()
                .AnyAsync(
                    p => p.TenantId == tenantId && p.EmployeeNumber == employeeNumber,
                    cancellationToken)
                .ConfigureAwait(false);

            if (numberTaken)
            {
                return Result<TeacherProfileDto>.Failure(
                    "Teachers.EmployeeNumberTaken",
                    $"An employee number '{employeeNumber}' already exists in this tenant.");
            }
        }

        var subjectIds = request.SubjectIds?
            .Distinct()
            .ToArray() ?? [];

        List<Subject> subjectEntities;

        if (subjectIds.Length > 0)
        {
            subjectEntities = await subjects.Query
                .Where(s => subjectIds.Contains(s.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (subjectEntities.Count != subjectIds.Length)
            {
                return Result<TeacherProfileDto>.Failure(
                    "Teachers.UnknownSubject",
                    "One or more subjects do not exist in this tenant.");
            }
        }
        else
        {
            subjectEntities = [];
        }

        var profile = new TeacherProfile
        {
            TenantId = tenantId,
            UserId = account.Id,
            EmployeeNumber = employeeNumber,
            HireDate = request.HireDate,
            IsActive = true
        };

        foreach (var subject in subjectEntities)
        {
            profile.Subjects.Add(new TeacherSubject
            {
                TenantId = tenantId,
                TeacherProfileId = profile.Id,
                SubjectId = subject.Id
            });
        }

        await profiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<TeacherProfileDto>.Success(
            TeacherProfileDto.FromEntity(profile, account, subjectEntities),
            "Teacher created successfully.");
    }

    /// <summary>
    /// Loads the account to attach: either the linked existing user, or a newly created one
    /// (already given the Teacher role when this returns success).
    /// </summary>
    private async Task<LinkOrNewUser> ResolveUserAsync(
        CreateTeacherRequest request,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (request.UserId is { } linkedUserId)
        {
            var linked = await userManager
                .FindByIdAsync(linkedUserId.ToString())
                .ConfigureAwait(false);

            // ApplicationUser carries the tenant filter, so a miss already means "not found
            // for you"; the explicit TenantId check covers the platform-scope edge.
            if (linked is null || linked.TenantId != tenantId)
            {
                return LinkOrNewUser.Failed(
                    "Teachers.UserNotFound",
                    "The user to link was not found.");
            }

            var profileExists = await profiles.Query
                .AnyAsync(p => p.UserId == linked.Id, cancellationToken)
                .ConfigureAwait(false);

            if (profileExists)
            {
                return LinkOrNewUser.Failed(
                    "Teachers.ProfileAlreadyExists",
                    "This user already has a teacher profile.");
            }

            if (request.PhoneNumber is not null)
            {
                linked.PhoneNumber = Normalize.Optional(request.PhoneNumber);
            }

            return LinkOrNewUser.Of(linked);
        }

        var email = Normalize.Required(request.Email!, string.Empty);

        var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

        if (existing is not null)
        {
            // Same answer for a duplicate e-mail here and a clash with another tenant:
            // e-mail is globally unique, so confirming which would leak account existence.
            return LinkOrNewUser.Failed(
                "Users.EmailAlreadyExists",
                "A user with this email address already exists.");
        }

        var teacherRoleExists = await roles.Query
            .AnyAsync(r => r.TenantId == tenantId && r.Name == RoleNames.Teacher, cancellationToken)
            .ConfigureAwait(false);

        if (!teacherRoleExists)
        {
            return LinkOrNewUser.Failed(
                "Teachers.TeacherRoleMissing",
                "The Teacher role does not exist for this tenant.");
        }

        var user = new ApplicationUser
        {
            TenantId = tenantId,
            FullName = Normalize.Required(request.FullName!, string.Empty),
            Email = email,
            UserName = email,
            PhoneNumber = Normalize.Optional(request.PhoneNumber),
            PreferredLanguage = SupportedLanguages.Default,
            IsActive = true,
            EmailConfirmed = true
        };

        // Identity performs the hashing; there is no password code anywhere in this project.
        var created = await userManager.CreateAsync(user, request.Password!).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return LinkOrNewUser.Failed(
                UserCommandSupport.IdentityErrorsToErrors(created),
                "The teacher account could not be created.");
        }

        var assigned = await userManager.AddToRoleAsync(user, RoleNames.Teacher).ConfigureAwait(false);

        if (!assigned.Succeeded)
        {
            return LinkOrNewUser.Failed(
                UserCommandSupport.IdentityErrorsToErrors(assigned),
                "The teacher account was created but the Teacher role could not be assigned.");
        }

        return LinkOrNewUser.Of(user);
    }

    private static string? NormalizeOptionalEmployeeNumber(string? value)
    {
        var normalized = Normalize.Optional(value);
        return normalized?.ToUpperInvariant();
    }

    /// <summary>Either a resolved account or the failure that prevented resolving one.</summary>
    private readonly record struct LinkOrNewUser(ApplicationUser? User, Result<TeacherProfileDto>? Failure)
    {
        public static LinkOrNewUser Of(ApplicationUser user) => new(user, null);

        public static LinkOrNewUser Failed(string code, string message)
            => new(null, Result<TeacherProfileDto>.Failure(code, message));

        public static LinkOrNewUser Failed(IReadOnlyList<Error> errors, string message)
            => new(null, Result<TeacherProfileDto>.Failure(errors, message));
    }
}

/// <summary>
/// Partially updates a teacher's profile. Identity fields (name, e-mail) are not accepted
/// here; they belong to the Users feature.
/// </summary>
/// <remarks>
/// <c>PhoneNumber</c> is the one identity-adjacent exception: it lives on the account but
/// is edited from the teacher form, so the handler mutates the tracked user and lets the
/// single end-of-request save persist both rows.
/// </remarks>
public sealed record UpdateTeacherCommand(Guid Id, UpdateTeacherRequest Request)
    : IRequest<Result<TeacherProfileDto>>;

public sealed class UpdateTeacherCommandHandler(
    IRepository<TeacherProfile> profiles,
    IRepository<TeacherSubject> teacherSubjects,
    IRepository<Subject> subjects,
    UserManager<ApplicationUser> userManager,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateTeacherCommand, Result<TeacherProfileDto>>
{
    public async Task<Result<TeacherProfileDto>> Handle(
        UpdateTeacherCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var profile = await profiles.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<TeacherProfileDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        var request = command.Request;

        if (request.EmployeeNumber is not null)
        {
            // "" clears the number: Normalize.Optional maps it to null, which is assigned
            // as-is (the null guard is on the request, not the result) - the same clearing
            // convention grades and subjects use for their optional fields.
            var employeeNumber = Normalize.Optional(request.EmployeeNumber)?.ToUpperInvariant();

            if (employeeNumber is not null)
            {
                var numberTaken = await profiles.Query
                    .IgnoreQueryFilters()
                    .AnyAsync(
                        p => p.TenantId == tenantId
                            && p.Id != profile.Id
                            && p.EmployeeNumber == employeeNumber,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (numberTaken)
                {
                    return Result<TeacherProfileDto>.Failure(
                        "Teachers.EmployeeNumberTaken",
                        $"An employee number '{employeeNumber}' already exists in this tenant.");
                }
            }

            profile.EmployeeNumber = employeeNumber;
        }

        if (request.HireDate.HasValue)
        {
            profile.HireDate = request.HireDate;
        }

        if (request.IsActive.HasValue)
        {
            // Profile status only; the account's own active flag is toggled through Users.
            profile.IsActive = request.IsActive.Value;
        }

        if (request.Notes is not null)
        {
            profile.Notes = Normalize.Optional(request.Notes);
        }

        var user = await userManager.FindByIdAsync(profile.UserId.ToString()).ConfigureAwait(false);

        if (user is null)
        {
            return Result<TeacherProfileDto>.Failure(
                "Teachers.UserNotFound",
                "The linked user account no longer exists.");
        }

        if (request.PhoneNumber is not null)
        {
            user.PhoneNumber = Normalize.Optional(request.PhoneNumber);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var assigned = await LoadSubjectsAsync(profile.Id, cancellationToken).ConfigureAwait(false);

        return Result<TeacherProfileDto>.Success(
            TeacherProfileDto.FromEntity(profile, user, assigned),
            "Teacher updated successfully.");
    }

    private async Task<List<Subject>> LoadSubjectsAsync(Guid profileId, CancellationToken cancellationToken)
    {
        return await teacherSubjects.Query
            .Where(t => t.TeacherProfileId == profileId)
            .Join(subjects.Query, t => t.SubjectId, s => s.Id, (_, s) => s)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Replaces a teacher's assigned subjects.
/// </summary>
/// <remarks>
/// The old links are hard-removed rather than soft-deleted: the unique index on
/// (TenantId, TeacherProfileId, SubjectId) has no <c>IsDeleted</c> filter, so a soft-deleted
/// link would keep occupying its slot and re-assigning the same subject would fail. Link
/// rows carry no history worth keeping - the audit log already records the change.
/// </remarks>
public sealed record AssignTeacherSubjectsCommand(Guid Id, AssignTeacherSubjectsRequest Request)
    : IRequest<Result<TeacherProfileDto>>;

public sealed class AssignTeacherSubjectsCommandHandler(
    IRepository<TeacherProfile> profiles,
    IRepository<TeacherSubject> teacherSubjects,
    IRepository<Subject> subjects,
    UserManager<ApplicationUser> userManager,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AssignTeacherSubjectsCommand, Result<TeacherProfileDto>>
{
    public async Task<Result<TeacherProfileDto>> Handle(
        AssignTeacherSubjectsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var profile = await profiles.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<TeacherProfileDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        var user = await userManager.FindByIdAsync(profile.UserId.ToString()).ConfigureAwait(false);

        if (user is null)
        {
            return Result<TeacherProfileDto>.Failure(
                "Teachers.UserNotFound",
                "The linked user account no longer exists.");
        }

        var subjectIds = command.Request.SubjectIds?
            .Distinct()
            .ToArray() ?? [];

        List<Subject> subjectEntities;

        if (subjectIds.Length > 0)
        {
            subjectEntities = await subjects.Query
                .Where(s => subjectIds.Contains(s.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (subjectEntities.Count != subjectIds.Length)
            {
                return Result<TeacherProfileDto>.Failure(
                    "Teachers.UnknownSubject",
                    "One or more subjects do not exist in this tenant.");
            }
        }
        else
        {
            subjectEntities = [];
        }

        var existingLinks = await teacherSubjects.Query
            .Where(t => t.TeacherProfileId == profile.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var link in existingLinks)
        {
            teacherSubjects.Remove(link);
        }

        foreach (var subject in subjectEntities)
        {
            await teacherSubjects.AddAsync(
                    new TeacherSubject
                    {
                        TenantId = tenantId,
                        TeacherProfileId = profile.Id,
                        SubjectId = subject.Id
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<TeacherProfileDto>.Success(
            TeacherProfileDto.FromEntity(profile, user, subjectEntities),
            "Teacher subjects updated successfully.");
    }
}

/// <summary>
/// Soft-deletes a teacher profile together with its compensation rules and subject links.
/// </summary>
/// <remarks>
/// The Identity account is deliberately untouched: it may have sign-in history, refresh
/// tokens or other roles, and account lifecycle belongs to the Users feature. Removing the
/// profile is what stops the person from appearing in teacher-facing flows.
/// </remarks>
public sealed record DeleteTeacherCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteTeacherCommandHandler(
    IRepository<TeacherProfile> profiles,
    IRepository<TeacherSubject> teacherSubjects,
    IRepository<CompensationRule> compensationRules,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteTeacherCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteTeacherCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var profile = await profiles.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<bool>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        // Rules are money history -> soft-deleted with the profile. Subject links are
        // replaceable junctions -> hard-removed so the unique index does not keep their slots.
        var rules = await compensationRules.Query
            .Where(r => r.TeacherProfileId == profile.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var rule in rules)
        {
            compensationRules.SoftRemove(rule);
        }

        var links = await teacherSubjects.Query
            .Where(t => t.TeacherProfileId == profile.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var link in links)
        {
            teacherSubjects.Remove(link);
        }

        profiles.SoftRemove(profile);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Teacher deleted successfully.");
    }
}
