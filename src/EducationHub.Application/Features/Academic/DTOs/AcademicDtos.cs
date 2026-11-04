using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Enums;

namespace EducationHub.Application.Features.Academic.DTOs;

public sealed record AcademicYearDto(
    Guid Id,
    string Name,
    string? NameAr,
    DateOnly StartDate,
    DateOnly EndDate,
    AcademicYearStatus Status,
    bool IsCurrent,
    bool IsArchived,
    DateTime CreatedAt)
{
    public static AcademicYearDto FromEntity(AcademicYear year)
        => new(
            year.Id,
            year.Name,
            year.NameAr,
            year.StartDate,
            year.EndDate,
            year.Status,
            year.IsCurrent,
            year.IsArchived,
            year.CreatedAt);
}

public sealed record CreateAcademicYearRequest(
    string Name,
    string? NameAr,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsCurrent);

public sealed record UpdateAcademicYearRequest(
    string? Name,
    string? NameAr,
    DateOnly? StartDate,
    DateOnly? EndDate,
    AcademicYearStatus? Status);

public sealed record GradeLevelDto(
    Guid Id,
    string Code,
    string Name,
    string? NameAr,
    int Order,
    bool IsActive,
    string? Description,
    string? DescriptionAr,
    DateTime CreatedAt)
{
    public static GradeLevelDto FromEntity(GradeLevel grade)
        => new(
            grade.Id,
            grade.Code,
            grade.Name,
            grade.NameAr,
            grade.Order,
            grade.IsActive,
            grade.Description,
            grade.DescriptionAr,
            grade.CreatedAt);
}

public sealed record CreateGradeLevelRequest(
    string Code,
    string Name,
    string? NameAr,
    int Order,
    string? Description,
    string? DescriptionAr);

public sealed record UpdateGradeLevelRequest(
    string? Name,
    string? NameAr,
    int? Order,
    bool? IsActive,
    string? Description,
    string? DescriptionAr);

public sealed record SubjectDto(
    Guid Id,
    string Code,
    string Name,
    string NameAr,
    string? Description,
    string? DescriptionAr,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt)
{
    public static SubjectDto FromEntity(Subject subject)
        => new(
            subject.Id,
            subject.Code,
            subject.Name,
            subject.NameAr,
            subject.Description,
            subject.DescriptionAr,
            subject.IsActive,
            subject.SortOrder,
            subject.CreatedAt);
}

public sealed record CreateSubjectRequest(
    string Code,
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    int SortOrder);

public sealed record UpdateSubjectRequest(
    string? Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    bool? IsActive,
    int? SortOrder);
