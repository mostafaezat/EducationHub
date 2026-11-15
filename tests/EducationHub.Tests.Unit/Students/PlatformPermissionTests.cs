using EducationHub.Shared.Common.Constants;

namespace EducationHub.Tests.Unit.Students;

/// <summary>The platform role holds platform permissions only, never a tenant's business data.</summary>
public sealed class PlatformPermissionTests
{
    [Fact]
    public void Platform_permissions_cover_tenant_and_platform_administration()
    {
        var platform = PermissionConstants.GetPlatformPermissions();

        Assert.Contains(PermissionConstants.PlatformManageTenants, platform);
        Assert.Contains(PermissionConstants.UsersView, platform);
        Assert.Contains(PermissionConstants.RolesView, platform);
        Assert.Contains(PermissionConstants.AuditLogsView, platform);
        Assert.Contains(PermissionConstants.Login, platform);
    }

    [Theory]
    [InlineData("Students")]
    [InlineData("Parents")]
    [InlineData("Teachers")]
    [InlineData("Groups")]
    [InlineData("Sessions")]
    [InlineData("Attendance")]
    [InlineData("Fees")]
    [InlineData("Payments")]
    [InlineData("Finance")]
    [InlineData("Reports")]
    [InlineData("Homework")]
    [InlineData("Exams")]
    [InlineData("Files")]
    [InlineData("Branches")]
    [InlineData("Settings")]
    [InlineData("AcademicYears")]
    [InlineData("Grades")]
    [InlineData("Subjects")]
    public void Platform_permissions_exclude_tenant_business_areas(string area)
    {
        var platform = PermissionConstants.GetPlatformPermissions();

        Assert.DoesNotContain(platform, p => p.StartsWith(area + ".", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_platform_area_exists_in_the_catalog()
    {
        Assert.All(PermissionConstants.PlatformAreaNames, area => Assert.True(PermissionConstants.Areas.ContainsKey(area), area));
        Assert.All(PermissionConstants.GetPlatformPermissions(), p => Assert.Contains(p, PermissionConstants.GetAll()));
    }
}
