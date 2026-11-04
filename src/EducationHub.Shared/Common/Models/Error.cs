namespace EducationHub.Shared.Common.Models;

/// <summary>
/// A single machine-readable error. <see cref="Code"/> is stable and safe for the
/// frontend to branch on; <see cref="Message"/> is a human-readable, already-localized
/// string intended for direct display.
/// </summary>
/// <param name="Code">Stable identifier, e.g. <c>Tenant.Inactive</c>.</param>
/// <param name="Message">Localized human-readable description.</param>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
