using System.Linq.Expressions;
using System.Reflection;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Audit;
using EducationHub.Domain.Entities.Files;
using EducationHub.Domain.Entities.Finance;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Learning;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EducationHub.Infrastructure.Persistence;

/// <summary>
/// DbSets the Application layer is allowed to see.
/// </summary>
/// <remarks>
/// <c>Users</c> and <c>Roles</c> are deliberately absent: Identity already exposes them on
/// <see cref="IdentityDbContext{TTUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken}"/>,
/// and redeclaring them would only shadow the inherited members.
/// </remarks>
public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Branch> Branches { get; }
    DbSet<TenantSettings> TenantSettings { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LoginHistory> LoginHistory { get; }
    DbSet<AcademicYear> AcademicYears { get; }
    DbSet<GradeLevel> GradeLevels { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<StoredFile> StoredFiles { get; }
    DbSet<TeacherProfile> TeacherProfiles { get; }
    DbSet<TeacherSubject> TeacherSubjects { get; }
    DbSet<CompensationRule> CompensationRules { get; }
    DbSet<StudentProfile> StudentProfiles { get; }
    DbSet<ParentProfile> ParentProfiles { get; }
    DbSet<StudentGuardian> StudentGuardians { get; }
    DbSet<StudyGroup> StudyGroups { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<ScheduleSlot> ScheduleSlots { get; }
    DbSet<ClassSession> ClassSessions { get; }
    DbSet<SessionAttendance> SessionAttendances { get; }
    DbSet<FeeRule> FeeRules { get; }
    DbSet<Charge> Charges { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<CashAccount> CashAccounts { get; }
    DbSet<CashTransaction> CashTransactions { get; }
    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<TeacherPayment> TeacherPayments { get; }
    DbSet<SessionCompensation> SessionCompensations { get; }
    DbSet<Homework> Homeworks { get; }
    DbSet<HomeworkSubmission> HomeworkSubmissions { get; }
    DbSet<Exam> Exams { get; }
    DbSet<ExamResult> ExamResults { get; }
    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The one DbContext for the whole system: ASP.NET Core Identity plus EducationHub domain
/// data, sharing a single connection and a single transaction.
/// </summary>
/// <remarks>
/// <para>
/// This class is where the tenant isolation guarantee is actually implemented. Two
/// independent mechanisms enforce it, and both are here on purpose:
/// </para>
/// <list type="number">
/// <item>
/// <b>Global query filters</b> (<see cref="ConfigureQueryFilters"/>) hide rows belonging to
/// other tenants on every read.
/// </item>
/// <item>
/// <c>SaveChanges</c> auditing (<see cref="ApplyAuditAndTenantRules"/>) stamps
/// <c>TenantId</c> on insert and <b>rejects</b> attempts to change it on update.
/// </item>
/// </list>
/// <para>
/// The filters alone would be insufficient: a handler could bypass them with
/// <c>IgnoreQueryFilters()</c>, or hand a foreign entity to <c>Update()</c> and have it
/// attached under the wrong tenant. The save-time check closes that hole.
/// </para>
/// </remarks>
public class EducationHubDbContext(
    DbContextOptions<EducationHubDbContext> options,
    ITenantContext tenantContext,
    AuditSaveChangesInterceptor auditInterceptor)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IApplicationDbContext
{
    public const string UsersTable = "Users";
    public const string RolesTable = "Roles";
    public const string UserRolesTable = "UserRoles";
    public const string UserClaimsTable = "UserClaims";
    public const string UserLoginsTable = "UserLogins";
    public const string RoleClaimsTable = "RoleClaims";
    public const string UserTokensTable = "UserTokens";

    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly AuditSaveChangesInterceptor _auditInterceptor = auditInterceptor;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginHistory> LoginHistory => Set<LoginHistory>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<GradeLevel> GradeLevels => Set<GradeLevel>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<CompensationRule> CompensationRules => Set<CompensationRule>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<ParentProfile> ParentProfiles => Set<ParentProfile>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<StudyGroup> StudyGroups => Set<StudyGroup>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();
    public DbSet<ClassSession> ClassSessions => Set<ClassSession>();
    public DbSet<SessionAttendance> SessionAttendances => Set<SessionAttendance>();
    public DbSet<FeeRule> FeeRules => Set<FeeRule>();
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<CashAccount> CashAccounts => Set<CashAccount>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<TeacherPayment> TeacherPayments => Set<TeacherPayment>();
    public DbSet<SessionCompensation> SessionCompensations => Set<SessionCompensation>();
    public DbSet<Homework> Homeworks => Set<Homework>();
    public DbSet<HomeworkSubmission> HomeworkSubmissions => Set<HomeworkSubmission>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ApplyIdentityTableNames(builder);
        ConfigureQueryFilters(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(EducationHubDbContext).Assembly);

        // Must run after the configurations above: Identity's base registers a global
        // (NormalizedName) index on roles, and EF Core makes filtered indexes unique by
        // default, so that index is a unique index across the whole application - the one
        // Identity default that is wrong for this schema. Role names are unique per tenant
        // (enforced by IX_Roles_TenantId_NormalizedName), and a TenantAdmin in tenant B must
        // never collide with tenant A's TenantAdmin.
        RemoveGlobalRoleNameIndex(builder);
    }

    /// <summary>
    /// Drops the Identity-derived global unique role name index.
    /// </summary>
    /// <remarks>
    /// Indexes declared outside this method (in <see cref="ApplicationRoleConfiguration"/>)
    /// are preserved; only the single-column NormalizedName index that the Identity base
    /// model contributed is removed.
    /// </remarks>
    private static void RemoveGlobalRoleNameIndex(ModelBuilder builder)
    {
        var role = builder.Entity<ApplicationRole>();

        var globalRoleNameIndex = role.Metadata.GetIndexes()
            .SingleOrDefault(index => index.Properties.Count == 1
                && index.Properties[0].Name == nameof(ApplicationRole.NormalizedName));

        if (globalRoleNameIndex is not null)
        {
            role.Metadata.RemoveIndex(globalRoleNameIndex);
        }
    }

    /// <summary>
    /// Applies the isolation filter to every entity that opts in, by interface rather than
    /// by base class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ITenantOwned"/> and <see cref="ISoftDeletableEntity"/> are markers, and
    /// this method is the only consumer. A new tenant-scoped entity is therefore protected
    /// the moment it implements the interface - there is no list to update and no way to
    /// forget.
    /// </para>
    /// <para>
    /// The filter reads <see cref="PlatformScope"/> and <see cref="CurrentTenantId"/> as
    /// <i>member accesses on the context instance</i>. That is the mechanism EF Core
    /// parameterizes: each access becomes a query parameter, so the model is built once and
    /// re-evaluated per query, and a tenant switch mid-request (or a second context in a
    /// test host) is picked up without rebuilding the model. Capturing a local value instead
    /// would freeze the tenant at model-build time - every request after the first would
    /// silently read the wrong tenant.
    /// </para>
    /// <para>
    /// Note the asymmetry that this produces by design: <c>Tenant</c> is soft-deletable but
    /// not tenant-owned, so it gets only the deletion filter and remains visible to the
    /// platform administrator; <c>ApplicationUser</c> is tenant-owned but not soft-deletable,
    /// so it gets only the tenant filter and is hard-deleted. If <c>Tenant</c> were given a
    /// tenant filter it would filter itself out of existence, which is why it must not
    /// implement <see cref="ITenantOwned"/>.
    /// </para>
    /// </remarks>
    private void ConfigureQueryFilters(ModelBuilder builder)
    {
        var platformScope = typeof(EducationHubDbContext)
            .GetProperty(nameof(PlatformScope), BindingFlags.NonPublic | BindingFlags.Instance)!;

        var currentTenant = typeof(EducationHubDbContext)
            .GetProperty(nameof(CurrentTenantId), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            var isSoftDeletable = typeof(ISoftDeletableEntity).IsAssignableFrom(clrType);
            var isTenantOwned = typeof(ITenantOwned).IsAssignableFrom(clrType);

            if (!isSoftDeletable && !isTenantOwned)
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "e");

            var context = Expression.Constant(this);

            // ANDed rather than filtered twice. HasQueryFilter replaces whatever filter the
            // entity already has, so an entity carrying both markers would otherwise keep only
            // the tenant predicate and become readable again after a soft delete.
            Expression body = Expression.Constant(true);

            if (isSoftDeletable)
            {
                body = Expression.AndAlso(
                    body,
                    Expression.Not(Expression.Property(parameter, nameof(ISoftDeletableEntity.IsDeleted))));
            }

            if (isTenantOwned)
            {
                // Platform scope short-circuits to "everything"; otherwise the row's tenant
                // must match. Compared as nullable so that "no tenant context" yields
                // `TenantId = NULL`, which SQL never satisfies - the fail-closed default.
                var matchesTenant = Expression.Equal(
                    Expression.Convert(
                        Expression.Property(parameter, nameof(ITenantOwned.TenantId)),
                        typeof(Guid?)),
                    Expression.Property(context, currentTenant));

                body = Expression.AndAlso(
                    body,
                    Expression.OrElse(
                        Expression.Property(context, platformScope),
                        matchesTenant));
            }

            builder.Entity(clrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    /// <summary>Reads <c>IsPlatformScope</c> for the filter above. Read by EF per query.</summary>
    private bool PlatformScope => _tenantContext.IsPlatformScope;

    /// <summary>Reads the active tenant for the filter above. Read by EF per query.</summary>
    private Guid? CurrentTenantId => _tenantContext.TenantId;

    /// <summary>
    /// Explains, in one place, why the tenant predicate denies rather than allows when
    /// <c>TenantId</c> is <c>null</c>: SQL's <c>= NULL</c> is never true, so a request with
    /// no resolved tenant matches no rows at all. Forgetting to resolve a tenant therefore
    /// produces an empty result set and a visible bug report, never a cross-tenant leak.
    /// </remarks>

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantRules();

        await _auditInterceptor.AuditChangesAsync(this, cancellationToken).ConfigureAwait(false);

        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantRules();

        _auditInterceptor.AuditChanges(this);

        return base.SaveChanges();
    }

    /// <summary>
    /// Stamps audit and tenant fields, and refuses to move an entity between tenants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>TenantId</c> mutation check is the second half of the isolation guarantee. A
    /// handler that loads entity A and calls <c>Update(entityB)</c> would otherwise be able
    /// to re-parent a row into its own tenant; rejecting the change makes that a loud error
    /// instead of a silent data-corruption bug.
    /// </para>
    /// </remarks>
    private void ApplyAuditAndTenantRules()
    {
        var now = DateTime.UtcNow;
        var userId = _auditInterceptor.CurrentUserId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Entity is IAuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = auditable.CreatedAt == default ? now : auditable.CreatedAt;
                    auditable.CreatedById ??= userId;
                }
                else
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedById = userId;
                }
            }

            if (entry.Entity is ITenantOwned owned)
            {
                if (entry.State == EntityState.Added)
                {
                    owned.TenantId = StampTenantIdOnInsert(owned);
                }
                else if (TenantIdIsBeingChanged(entry))
                {
                    throw new InvalidOperationException(
                        $"TenantId of {entry.Entity.GetType().Name} ({owned.TenantId}) cannot be changed. " +
                        "Moving data between tenants is not supported; create a new record in the target tenant.");
                }
            }

            StampSoftDelete(entry, now, userId);
        }
    }

    /// <summary>
    /// Detects an actual tenant re-parenting.
    /// </summary>
    /// <remarks>
    /// Compares the stored value against the current one rather than reading
    /// <c>IsModified</c>, because <c>IsModified</c> means "marked for an UPDATE statement",
    /// not "the value changed". <c>DbSet.Update()</c> and Identity's own store both mark
    /// every property of an entity dirty as a way of forcing its state to <c>Modified</c>, so
    /// a plain role or user edit would be reported as a tenant move and rejected - a false
    /// positive on the happy path, which would be a denial of service dressed as a safety
    /// check. Comparing values keeps the check exactly as strict as it is meant to be while
    /// letting an unchanged <c>TenantId</c> be written back to itself.
    /// </remarks>
    private static bool TenantIdIsBeingChanged(EntityEntry entry)
    {
        var tenantProperty = entry.Property(nameof(ITenantOwned.TenantId));

        if (!tenantProperty.IsModified)
        {
            return false;
        }

        // `CurrentValue` and `OriginalValue` are both object, so the comparison is by value.
        // OriginalValue is the database value (or the value at load), which is what makes an
        // edit of some other column with TenantId untouched come out as equal.
        return !Equals(tenantProperty.OriginalValue, tenantProperty.CurrentValue);
    }

    /// <summary>
    /// Records who deleted a soft-deleted row and when.
    /// </summary>
    /// <remarks>
    /// Done here rather than in the repository because the repository only flips a flag: it
    /// has no access to the acting user, and every save path - handler, seeder, future
    /// background job - reaches this method, so the fields cannot be forgotten. Rows that
    /// were already deleted keep their original stamp, because overwriting it would destroy
    /// the only record of when the delete actually happened.
    /// </remarks>
    private static void StampSoftDelete(
        EntityEntry entry,
        DateTime now,
        Guid? userId)
    {
        if (entry.State != EntityState.Modified || entry.Entity is not ISoftDeletableEntity softDelete)
        {
            return;
        }

        if (!softDelete.IsDeleted || entry.Property(nameof(ISoftDeletableEntity.DeletedAt)).IsModified)
        {
            return;
        }

        softDelete.DeletedAt = now;
        softDelete.DeletedById = userId;
    }

    /// <summary>
    /// Assigns the current tenant on insert, refusing to invent one for a platform user.
    /// </summary>
    /// <remarks>
    /// A row that arrives with a tenant already set is left alone only when that tenant
    /// matches the request. A mismatched value is an error, not something to overwrite
    /// silently - overwriting would mask the bug that caused it.
    /// <para>
    /// Platform scope is checked <i>first</i>, before any of that. A platform administrator
    /// runs with <c>TenantId = Guid.Empty</c>, yet is precisely the caller who provisions
    /// tenant-scoped rows - the settings row of a tenant being created carries that tenant's
    /// id, which will never equal <c>Guid.Empty</c>. Refusing that insert would have broken
    /// tenant provisioning at the save. Platform-scoped writes are already trusted (every
    /// privilege boundary is re-checked inside its handler), so the save rule only shapes
    /// what a <i>tenant-scoped</i> request may do.
    /// </para>
    /// </remarks>
    private Guid StampTenantIdOnInsert(ITenantOwned owned)
    {
        if (_tenantContext.IsPlatformScope)
        {
            return owned.TenantId;
        }

        var currentTenant = _tenantContext.TenantId;

        if (currentTenant.HasValue && owned.TenantId == Guid.Empty)
        {
            return currentTenant.Value;
        }

        if (currentTenant.HasValue && owned.TenantId != currentTenant.Value)
        {
            throw new InvalidOperationException(
                $"Cannot insert a {owned.GetType().Name} into tenant {owned.TenantId} " +
                $"while the request is scoped to tenant {currentTenant.Value}.");
        }

        // No tenant context: only a platform administrator may create tenant-less rows.
        if (owned.TenantId == Guid.Empty && !_tenantContext.IsPlatformScope)
        {
            // Authentication artifacts are the one exception. Login and refresh run before
            // any tenant or principal exists, yet a platform administrator's own rows
            // legitimately carry Guid.Empty. The TenantId on these rows comes from the user
            // row read out of the database, never from the client, so it is trusted here.
            if (owned is EducationHub.Domain.Entities.Identity.RefreshToken
                or EducationHub.Domain.Entities.Identity.LoginHistory)
            {
                return owned.TenantId;
            }

            throw new InvalidOperationException(
                $"A {owned.GetType().Name} was added without a TenantId and no tenant context is active. " +
                "This is allowed only for platform administrators.");
        }

        return owned.TenantId;
    }

    /// <summary>
    /// Renames the seven Identity tables to the exact names in the specification.
    /// </summary>
    /// <remarks>
    /// Done in code rather than by hand-editing a generated migration, so the names are
    /// guaranteed to survive every future <c>add-migration</c>.
    /// </remarks>
    private static void ApplyIdentityTableNames(ModelBuilder builder)
    {
        // Mapped through the concrete types, not `IdentityUser<Guid>`/`IdentityRole<Guid>`.
        // `ApplicationUser` and `ApplicationRole` derive from those, so naming the bases here
        // would register a second entity type against the same table and the model would
        // fail to build for lack of a linking relationship.
        builder.Entity<ApplicationUser>().ToTable(UsersTable);
        builder.Entity<ApplicationRole>().ToTable(RolesTable);
        builder.Entity<IdentityUserRole<Guid>>().ToTable(UserRolesTable);
        builder.Entity<IdentityUserClaim<Guid>>().ToTable(UserClaimsTable);
        builder.Entity<IdentityUserLogin<Guid>>().ToTable(UserLoginsTable);
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable(RoleClaimsTable);
        builder.Entity<IdentityUserToken<Guid>>().ToTable(UserTokensTable);

        // Identity's own cascade deletes would silently remove roles a tenant is still
        // using; Restrict makes the constraint visible instead. The relationships are
        // declared by type rather than by navigation property, because Identity's join
        // entities expose their navigation members inconsistently across versions.
        builder.Entity<IdentityUserRole<Guid>>()
            .HasOne<ApplicationRole>()
            .WithMany()
            .HasForeignKey(nameof(IdentityUserRole<Guid>.RoleId))
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<IdentityUserRole<Guid>>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(nameof(IdentityUserRole<Guid>.UserId))
            .OnDelete(DeleteBehavior.Restrict);
    }
}
