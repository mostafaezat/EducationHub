using EducationHub.Application.Features.Academic.DTOs;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Enums;

namespace EducationHub.Application.Features.Teachers.DTOs;

/// <summary>
/// A teacher as the UI needs them: profile fields, the linked account's identity data and
/// the assigned subjects in one payload.
/// </summary>
public sealed record TeacherProfileDto(
    Guid Id,
    Guid UserId,
    string? EmployeeNumber,
    string FullName,
    string Email,
    string? PhoneNumber,
    DateOnly? HireDate,
    bool IsActive,
    string? Notes,
    IReadOnlyList<SubjectDto> Subjects,
    DateTime CreatedAt)
{
    public static TeacherProfileDto FromEntity(
        TeacherProfile profile,
        ApplicationUser user,
        IEnumerable<Subject> subjects)
        => new(
            profile.Id,
            profile.UserId,
            profile.EmployeeNumber,
            user.FullName,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            profile.HireDate,
            profile.IsActive,
            profile.Notes,
            [.. subjects.OrderBy(static s => s.SortOrder).Select(SubjectDto.FromEntity)],
            profile.CreatedAt);
}

/// <summary>
/// Creates a teacher. Supply either <see cref="UserId"/> to link an existing account
/// (never used together with the credential fields) or the new account's
/// <see cref="Email"/>, <see cref="Password"/> and <see cref="FullName"/>, which are
/// created and given the tenant's Teacher role in the same transaction as the profile.
/// </summary>
public sealed record CreateTeacherRequest(
    string? EmployeeNumber,
    string? Email,
    string? Password,
    string? FullName,
    string? PhoneNumber,
    Guid? UserId,
    DateOnly? HireDate,
    IReadOnlyList<Guid>? SubjectIds);

/// <summary>Partial update. Profile fields only; identity fields belong to the Users feature.</summary>
public sealed record UpdateTeacherRequest(
    string? EmployeeNumber,
    string? PhoneNumber,
    DateOnly? HireDate,
    bool? IsActive,
    string? Notes);

/// <summary>Replaces the teacher's subject set. An empty list clears it.</summary>
public sealed record AssignTeacherSubjectsRequest(IReadOnlyList<Guid>? SubjectIds);

public sealed record CompensationRuleDto(
    Guid Id,
    Guid? TeacherProfileId,
    Guid? SubjectId,
    Guid? StudyGroupId,
    decimal Rate,
    CompensationRateBasis RateBasis,
    bool IsActive,
    DateTime CreatedAt)
{
    public static CompensationRuleDto FromEntity(CompensationRule rule)
        => new(
            rule.Id,
            rule.TeacherProfileId,
            rule.SubjectId,
            rule.StudyGroupId,
            rule.Rate,
            rule.RateBasis,
            rule.IsActive,
            rule.CreatedAt);
}

/// <summary>
/// Creates a compensation rule. The scope is the set of non-null dimensions; all null is
/// the tenant default. <see cref="StudyGroupId"/> must be null until the scheduling module
/// ships study groups.
/// </summary>
public sealed record CreateCompensationRuleRequest(
    Guid? TeacherProfileId,
    Guid? SubjectId,
    Guid? StudyGroupId,
    decimal Rate,
    CompensationRateBasis RateBasis);

/// <summary>Partial update. The scope of a rule is fixed after creation.</summary>
public sealed record UpdateCompensationRuleRequest(
    decimal? Rate,
    CompensationRateBasis? RateBasis,
    bool? IsActive);

/// <summary>
/// Outcome of resolving compensation for a teacher. <see cref="Rule"/> is null when no
/// active rule matches, and <see cref="Rank"/> is the 1-based precedence level of the
/// winning rule (1 = Group+Teacher … 6 = TenantDefault).
/// </summary>
public sealed record ResolvedCompensationDto(CompensationRuleDto? Rule, int? Rank);
