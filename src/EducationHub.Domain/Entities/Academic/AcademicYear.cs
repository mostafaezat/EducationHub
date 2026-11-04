using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Academic;

/// <summary>
/// An academic year such as 2026/2027. Historical years are archived, never deleted, so
/// enrolment, attendance, exam and fee history stays queryable (spec sections 12 and 16).
/// </summary>
public class AcademicYear : TenantOwnedEntity
{
    /// <summary>Display form, e.g. <c>2026/2027</c>.</summary>
    public string Name { get; set; } = string.Empty;

    public string? NameAr { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public AcademicYearStatus Status { get; set; } = AcademicYearStatus.Upcoming;

    /// <summary>Only one year per tenant may be <see cref="AcademicYearStatus.Current"/>.</summary>
    public bool IsCurrent { get; set; }

    public bool IsArchived { get; set; }

    public bool IsWithinRange(DateOnly date)
        => date >= StartDate && date <= EndDate;

    public bool Contains(DateOnly date)
        => !IsArchived && IsWithinRange(date);

    public void Validate()
    {
        if (EndDate < StartDate)
        {
            throw new InvalidOperationException(
                "Academic year end date cannot be earlier than its start date.");
        }
    }
}
