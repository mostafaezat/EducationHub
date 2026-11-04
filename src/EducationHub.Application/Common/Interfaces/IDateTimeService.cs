namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// Time source. Injected rather than calling <see cref="DateTime.UtcNow"/> directly so
/// that token expiry, lockout windows, attendance marking and fee generation are all
/// testable at a fixed instant instead of "now".
/// </summary>
public interface IDateTimeService
{
    DateTime UtcNow { get; }

    DateOnly UtcToday { get; }

    /// <summary>Current time in the tenant's configured IANA/Windows time zone.</summary>
    DateTime GetTenantNow();

    DateOnly GetTenantToday();

    /// <summary>Converts a UTC instant into the tenant's local time.</summary>
    DateTime ToTenantTime(DateTime utcDateTime);
}
