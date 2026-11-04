using EducationHub.Application.Common.Interfaces;
using EducationHub.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EducationHub.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> construct the context outside the application.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EducationHubDbContext"/> takes <c>ITenantContext</c> and
/// <c>AuditSaveChangesInterceptor</c> through its constructor, and the interceptor in turn
/// takes <c>ICurrentUser</c> and <c>IDiagnosticContext</c>. None of those are resolvable by
/// the EF tools, which would try to build them from the application's DI container. Without
/// this factory every <c>dotnet ef migrations</c> command fails during tool startup.
/// </para>
/// <para>
/// The stubs below are safe because scaffolding only needs the model. The tenant context is
/// put in platform scope so no global query filter hides a table while the snapshot is
/// generated, and neither the user nor the diagnostics context is ever read: migrations
/// never call <c>SaveChanges</c>, so the interceptor has nothing to do.
/// </para>
/// <para>
/// The connection string is a placeholder. Only the SQL Server provider matters at
/// scaffold time; the real connection comes from configuration when a command actually runs,
/// so this deliberately does not point at the development database.
/// </para>
/// <para>
/// Design-time infrastructure only - this type is never registered in DI.
/// </para>
/// </remarks>
public sealed class EducationHubDbContextFactory : IDesignTimeDbContextFactory<EducationHubDbContext>
{
    public EducationHubDbContext CreateDbContext(string[] args)
    {
        var tenantContext = new DesignTimeTenantContext();

        var interceptor = new AuditSaveChangesInterceptor(
            new DesignTimeCurrentUser(),
            new DesignTimeDiagnosticContext());

        var options = new DbContextOptionsBuilder<EducationHubDbContext>()
            .UseSqlServer(
                "Server=(design-time);Database=EducationHub_DesignTime;" +
                "Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new EducationHubDbContext(options, tenantContext, interceptor);
    }

    /// <summary>
    /// Platform scope, so the global query filters do not exclude tables from the snapshot.
    /// </summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;

        public Guid? BranchId => null;

        public bool IsPlatformScope => true;
    }

    /// <summary>No signed-in user exists at design time; audit fields are never stamped.</summary>
    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid? UserId => null;

        public string? UserName => null;

        public string? FullName => null;

        public Guid? TenantId => null;

        public Guid? BranchId => null;

        public string? IpAddress => null;

        public string? PreferredLanguage => null;

        public bool IsPlatformAdmin => false;

        public IReadOnlyCollection<string> Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public bool HasPermission(string permission) => false;
    }

    /// <summary>No request exists at design time; a fixed placeholder keeps audit columns non-null.</summary>
    private sealed class DesignTimeDiagnosticContext : IDiagnosticContext
    {
        public string CorrelationId => "design-time";

        public string? IpAddress => null;
    }
}