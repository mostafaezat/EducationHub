namespace EducationHub.Shared.Common.Constants;

/// <summary>
/// System role names. These are seeded per tenant (except <see cref="PlatformAdmin"/>,
/// which is platform-level with <c>TenantId = null</c>) and are referenced by
/// authorization policies, so they must not be renamed casually.
/// </summary>
public static class RoleNames
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string TenantOwner = "TenantOwner";
    public const string TenantAdmin = "TenantAdmin";
    public const string Teacher = "Teacher";
    public const string Assistant = "Assistant";
    public const string Accountant = "Accountant";
    public const string Parent = "Parent";
    public const string Student = "Student";

    /// <summary>Roles created for every tenant when it is provisioned.</summary>
    public static IReadOnlyList<string> TenantRoles { get; } =
    [
        TenantOwner,
        TenantAdmin,
        Teacher,
        Assistant,
        Accountant,
        Parent,
        Student
    ];

    public static bool IsTenantRole(string role)
        => TenantRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static bool IsPlatformRole(string role)
        => string.Equals(role, PlatformAdmin, StringComparison.OrdinalIgnoreCase);
}
