using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Domain.Enums;

namespace EducationHub.Application.Features.Registration;

/// <summary>What the public pre-registration form needs to render for one centre.</summary>
public sealed record PublicRegistrationFormDto(
    string TenantSlug,
    string CentreName,
    string? CentreNameAr,
    IReadOnlyList<PublicOptionDto> GradeLevels,
    IReadOnlyList<PublicOptionDto> Subjects);

public sealed record PublicOptionDto(Guid Id, string Name, string? NameAr);

/// <summary>
/// Body of <c>POST /api/public/registration/{tenantSlug}</c>. <see cref="Website"/> is a honeypot:
/// the form hides it, so only a bot fills it in.
/// </summary>
public sealed record SubmitRegistrationRequest(
    string? GuardianName,
    string? GuardianPhone,
    string? GuardianEmail,
    GuardianRelationship Relationship,
    string? StudentName,
    DateOnly? BirthDate,
    Guid? GradeLevelId,
    string? School,
    IReadOnlyList<Guid>? SubjectIds,
    string? Notes,
    string? Website = null);

public sealed record RegistrationSubmittedDto(string CentreName);

public sealed record RegistrationRequestDto(
    Guid Id,
    string GuardianName,
    string GuardianPhone,
    string? GuardianEmail,
    GuardianRelationship Relationship,
    string StudentName,
    DateOnly? BirthDate,
    Guid? GradeLevelId,
    string? GradeLevelName,
    string? School,
    string? PreferredSubjects,
    string? Notes,
    RegistrationRequestStatus Status,
    DateTime CreatedAt,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAt,
    string? RejectionReason,
    Guid? CreatedStudentProfileId);

/// <summary>
/// Approval of a request. Every member except <see cref="StudyGroupId"/> overrides what the
/// guardian typed; null keeps it.
/// </summary>
public sealed record ApproveRegistrationRequest(
    Guid? StudyGroupId = null,
    string? StudentName = null,
    DateOnly? BirthDate = null,
    Guid? GradeLevelId = null,
    string? GuardianName = null,
    string? GuardianPhone = null,
    string? GuardianEmail = null,
    GuardianRelationship? Relationship = null,
    bool CreateAccount = true,
    string? Notes = null);

public sealed record ApproveRegistrationResultDto(
    RegistrationRequestDto Request,
    StudentProfileDto Student,
    bool Enrolled);

public sealed record RejectRegistrationRequest(string? Reason);

/// <summary>The public page is <c>/register/{TenantSlug}</c> on the admin app's host.</summary>
public sealed record RegistrationLinkDto(string TenantSlug);
