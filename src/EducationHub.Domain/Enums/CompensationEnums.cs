namespace EducationHub.Domain.Enums;

/// <summary>
/// What a <c>CompensationRule.Rate</c> is measured against.
/// </summary>
public enum CompensationRateBasis
{
    /// <summary>One delivered class session.</summary>
    PerSession = 0,

    /// <summary>One hour (scheduled or delivered).</summary>
    PerHour = 1
}
