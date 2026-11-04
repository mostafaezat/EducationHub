using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Tenant isolation, verified against a real SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// The isolation guarantee rests on the global query filter in
/// <c>EducationHubDbContext.ConfigureQueryFilters</c>. These tests exist because that filter
/// is enforced by EF Core rather than by application code: a mistake in it produces no
/// compile error and no obvious runtime symptom - it simply returns another school's rows.
/// </para>
/// <para>
/// Every test writes through a platform-scope context and reads back through a
/// single-tenant context. That arrangement is the whole point: reading with the same scope
/// that wrote would pass whether or not the filter existed.
/// </para>
/// </remarks>
public sealed class TenantIsolationTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task SingleTenantContext_cannot_read_another_tenants_branches()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync(fixture);

        await using var context = fixture.CreateContext(new SingleTenantContext(tenantA));

        var visible = await context.Branches.ToListAsync();

        Assert.Contains(visible, b => b.TenantId == tenantA);
        Assert.DoesNotContain(visible, b => b.TenantId == tenantB);
    }

    [Fact]
    public async Task SingleTenantContext_cannot_see_another_tenants_users()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync(fixture);

        await using var context = fixture.CreateContext(new SingleTenantContext(tenantA));

        var visible = await context.Users.ToListAsync();

        Assert.Contains(visible, u => u.TenantId == tenantA);
        Assert.DoesNotContain(visible, u => u.TenantId == tenantB);
    }

    /// <summary>
    /// A request with no resolved tenant sees nothing at all.
    /// </summary>
    /// <remarks>
    /// The fail-closed default, and the reason this matters: if a missing tenant context
    /// returned everything, forgetting to resolve it would be a silent cross-tenant leak.
    /// Returning nothing turns the same mistake into an empty table and a visible bug report.
    /// </remarks>
    [Fact]
    public async Task NoTenantContext_sees_no_tenant_rows()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync(fixture);

        await using var platform = fixture.CreateContext(new PlatformTenantContext());

        Assert.True(
            await platform.Branches.AnyAsync(b => b.TenantId == tenantA),
            "The fixture must have created rows for this assertion to mean anything.");

        await using var noTenant = fixture.CreateContext(new NullTenantContext());

        Assert.Empty(await noTenant.Branches.ToListAsync());
    }

    /// <summary>Soft-deleted rows stay hidden even from the tenant that owns them.</summary>
    [Fact]
    public async Task SoftDeletedRow_is_hidden_from_its_own_tenant()
    {
        var (tenantA, _) = await SeedTwoTenantsAsync(fixture);

        await using (var write = fixture.CreateContext(new PlatformTenantContext()))
        {
            var branch = await write.Branches
                .FirstAsync(b => b.TenantId == tenantA);

            branch.IsDeleted = true;

            await write.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext(new SingleTenantContext(tenantA));

        Assert.Empty(
            await context.Branches.Where(b => b.TenantId == tenantA).ToListAsync());
    }

    /// <summary>
    /// A row cannot be moved between tenants by handing it to <c>Update()</c>.
    /// </summary>
    /// <remarks>
    /// The save-time half of the isolation guarantee. The query filters alone would not stop
    /// this: the row is already tracked in the right tenant's context, and only the
    /// <c>TenantId</c>-unchanged check turns the attempt into a loud error.
    /// </remarks>
    [Fact]
    public async Task TenantId_cannot_be_changed_by_an_update()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync(fixture);

        await using var context = fixture.CreateContext(new SingleTenantContext(tenantA));

        var branch = await context.Branches
            .FirstAsync(b => b.TenantId == tenantA);

        branch.TenantId = tenantB;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.SaveChangesAsync());

        Assert.Contains("cannot be changed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creating a row for another tenant is refused.
    /// </summary>
    [Fact]
    public async Task Insert_into_another_tenant_is_refused()
    {
        var (tenantA, tenantB) = await SeedTwoTenantsAsync(fixture);

        await using var context = fixture.CreateContext(new SingleTenantContext(tenantA));

        context.Branches.Add(new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            Name = "Smuggled",
            Status = BranchStatus.Active,
            IsMainBranch = false
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.SaveChangesAsync());
    }

    /// <summary>
    /// Two tenants, each with one branch and one user, written under platform scope.
    /// </summary>
    /// <remarks>
    /// Fresh ids on every call so tests within the class cannot see each other's rows. The
    /// slug is truncated to keep it inside the column, and is unique because it embeds the
    /// tenant's own GUID.
    /// </remarks>
    private static async Task<(Guid TenantA, Guid TenantB)> SeedTwoTenantsAsync(SqlServerFixture fixture)
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var context = fixture.CreateContext(new PlatformTenantContext());

        context.Tenants.AddRange(
            NewTenant(tenantA, "Alpha Academy"),
            NewTenant(tenantB, "Beta Academy"));

        context.Branches.AddRange(
            new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Name = "Alpha Main",
                Status = BranchStatus.Active,
                IsMainBranch = true
            },
            new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                Name = "Beta Main",
                Status = BranchStatus.Active,
                IsMainBranch = true
            });

        context.Users.Add(NewUser(tenantA, "Alpha Teacher"));

        await context.SaveChangesAsync();

        return (tenantA, tenantB);
    }

    private static Tenant NewTenant(Guid id, string name)
        => new()
        {
            Id = id,
            Slug = $"{name.Split(' ')[0].ToLowerInvariant()}-{id:N}"[..20],
            Name = name,
            Type = TenantType.EducationCenter,
            Status = TenantStatus.Active,
            Currency = "EGP",
            TimeZone = "Africa/Cairo",
            DefaultLanguage = "ar",
            IsActive = true,
            SubscriptionStartDate = DateTime.UtcNow,
            SubscriptionExpiryDate = DateTime.UtcNow.AddYears(1)
        };

    private static Domain.Entities.Identity.ApplicationUser NewUser(Guid tenantId, string fullName)
    {
        var email = $"user-{tenantId:N}@example.test";

        return new Domain.Entities.Identity.ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = fullName,
            PreferredLanguage = "ar",
            IsActive = true
        };
    }
}

/// <summary>
/// A tenant context with no tenant and no platform scope.
/// </summary>
/// <remarks>
/// The state a request is in when tenant resolution failed or was never attempted.
/// Deliberately distinct from <see cref="PlatformTenantContext"/>, whose cross-tenant access
/// must be granted explicitly rather than being the accidental default.
/// </remarks>
public sealed class NullTenantContext : Application.Common.Interfaces.ITenantContext
{
    public Guid? TenantId => null;

    public Guid? BranchId => null;

    public bool IsPlatformScope => false;
}