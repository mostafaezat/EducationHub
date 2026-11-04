using EducationHub.Domain.Enums;

namespace EducationHub.Application.Features.Students.DTOs;

/// <summary>One guardian link as seen from the student side.</summary>
public sealed record GuardianLinkDto(
    Guid ParentProfileId,
    string ParentFullName,
    string? PhoneNumber,
    GuardianRelationship Relationship,
    bool IsPrimary);

/// <summary>
/// A student as the UI needs them: profile fields, the grade and branch names for display,
/// whether a sign-in account is linked, and the guardians in one payload.
/// </summary>
public sealed record StudentProfileDto(
    Guid Id,
    Guid? UserId,
    string? AccountEmail,
    string FullName,
    string? StudentNumber,
    DateOnly? DateOfBirth,
    Guid? GradeLevelId,
    string? GradeLevelName,
    Guid? BranchId,
    string? BranchName,
    StudentStatus Status,
    DateOnly? EnrollmentDate,
    string? Notes,
    IReadOnlyList<GuardianLinkDto> Guardians,
    DateTime CreatedAt);

/// <summary>One linked student as seen from the parent side.</summary>
public sealed record ChildLinkDto(
    Guid StudentProfileId,
    string StudentFullName,
    string? StudentNumber,
    string? GradeLevelName,
    StudentStatus Status,
    GuardianRelationship Relationship,
    bool IsPrimary);

public sealed record ParentProfileDto(
    Guid Id,
    Guid? UserId,
    string? AccountEmail,
    string FullName,
    string? PhoneNumber,
    string? Email,
    string? Notes,
    IReadOnlyList<ChildLinkDto> Children,
    DateTime CreatedAt);

/// <summary>
/// Creates a student. Three account modes, mutually exclusive: no account (the default),
/// link an existing account via <see cref="UserId"/>, or create a new one from
/// <see cref="AccountEmail"/> + <see cref="AccountPassword"/>, which receives the tenant's
/// Student role in the same transaction as the profile.
/// </summary>
public sealed record CreateStudentRequest(
    string? FullName,
    string? StudentNumber,
    DateOnly? DateOfBirth,
    Guid? GradeLevelId,
    Guid? BranchId,
    DateOnly? EnrollmentDate,
    string? Notes,
    Guid? UserId,
    string? AccountEmail,
    string? AccountPassword);

/// <summary>Partial update: a null member leaves the field unchanged.</summary>
public sealed record UpdateStudentRequest(
    string? FullName,
    string? StudentNumber,
    DateOnly? DateOfBirth,
    Guid? GradeLevelId,
    Guid? BranchId,
    StudentStatus? Status,
    DateOnly? EnrollmentDate,
    string? Notes);

public sealed record GuardianLinkRequest(
    Guid ParentProfileId,
    GuardianRelationship Relationship,
    bool IsPrimary);

/// <summary>Replaces the student's guardian set. An empty list clears it.</summary>
public sealed record SetStudentGuardiansRequest(IReadOnlyList<GuardianLinkRequest>? Guardians);

/// <summary>
/// Creates a parent. Same three account modes as a student: none, link via
/// <see cref="UserId"/>, or create a new account signing in with <see cref="Email"/> and
/// <see cref="AccountPassword"/> (given the tenant's Parent role).
/// </summary>
public sealed record CreateParentRequest(
    string? FullName,
    string? PhoneNumber,
    string? Email,
    string? Notes,
    Guid? UserId,
    string? AccountPassword);

/// <summary>Partial update: a null member leaves the field unchanged.</summary>
public sealed record UpdateParentRequest(
    string? FullName,
    string? PhoneNumber,
    string? Email,
    string? Notes);
