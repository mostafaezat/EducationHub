using EducationHub.Domain.Entities.Audit;
using EducationHub.Domain.Entities.Files;
using EducationHub.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationHub.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable(EducationHubDbContext.UsersTable);

        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(30);
        builder.Property(u => u.Notes).HasMaxLength(1000);
        builder.Property(u => u.PreferredLanguage).HasMaxLength(5).IsRequired();
        builder.Property(u => u.LastLoginIp).HasMaxLength(45);

        // Email is the login identity and is unique across the WHOLE platform, not within a
        // tenant. The user chooses no tenant at login, so a per-tenant unique index would
        // make the same address ambiguous for two different tenants.
        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL")
            .HasDatabaseName("IX_Users_NormalizedEmail");

        builder.HasIndex(u => u.TenantId).HasDatabaseName("IX_Users_TenantId");

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.LoginHistory)
            .WithOne(h => h.User)
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(u => u.IsLockedOut);
    }
}

public sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.ToTable(EducationHubDbContext.RolesTable);

        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.DescriptionAr).HasMaxLength(500);

        // Role names are unique per tenant. A platform role carries Guid.Empty, so the
        // seeded system roles of different tenants never collide.
        builder.HasIndex(r => new { r.TenantId, r.NormalizedName }).IsUnique()
            .HasDatabaseName("IX_Roles_TenantId_NormalizedName");

        builder.HasIndex(r => r.TenantId).HasDatabaseName("IX_Roles_TenantId");
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.RevokedReason).HasMaxLength(50);
        builder.Property(t => t.CreatedByIp).HasMaxLength(45);
        builder.Property(t => t.UserAgent).HasMaxLength(500);

        builder.Ignore(t => t.IssuedAt);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("IX_RefreshTokens_TokenHash");

        // Rotation looks a token up by hash on every refresh, and the family revoke walks
        // the whole chain, so both need their own index.
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("IX_RefreshTokens_FamilyId");

        builder.HasIndex(t => new { t.UserId, t.IsDeleted })
            .HasDatabaseName("IX_RefreshTokens_UserId_IsDeleted");
    }
}

public sealed class LoginHistoryConfiguration : IEntityTypeConfiguration<LoginHistory>
{
    public void Configure(EntityTypeBuilder<LoginHistory> builder)
    {
        builder.ToTable("LoginHistory");

        builder.Property(h => h.UserName).HasMaxLength(256);
        builder.Property(h => h.FailureReason).HasMaxLength(100);
        builder.Property(h => h.IpAddress).HasMaxLength(45);
        builder.Property(h => h.UserAgent).HasMaxLength(500);

        // One query per user answers both "recent activity" and "lockout threshold" checks.
        builder.HasIndex(h => new { h.UserId, h.AttemptedAt })
            .HasDatabaseName("IX_LoginHistory_UserId_AttemptedAt");

        builder.HasIndex(h => new { h.TenantId, h.AttemptedAt })
            .HasDatabaseName("IX_LoginHistory_TenantId_AttemptedAt");
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.Property(l => l.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(l => l.EntityId).HasMaxLength(100);
        builder.Property(l => l.UserName).HasMaxLength(256);
        builder.Property(l => l.CorrelationId).HasMaxLength(100);
        builder.Property(l => l.IpAddress).HasMaxLength(45);
        builder.Property(l => l.Description).HasMaxLength(1000);

        builder.HasIndex(l => new { l.TenantId, l.OccurredAt })
            .HasDatabaseName("IX_AuditLogs_TenantId_OccurredAt");

        builder.HasIndex(l => new { l.EntityName, l.EntityId })
            .HasDatabaseName("IX_AuditLogs_EntityName_EntityId");

        builder.HasIndex(l => new { l.UserId, l.OccurredAt })
            .HasDatabaseName("IX_AuditLogs_UserId_OccurredAt");
    }
}

public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles");

        builder.Property(f => f.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(f => f.OriginalFileName).HasMaxLength(300).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(f => f.OwnerEntityId).HasColumnType("uniqueidentifier");
        builder.Property(f => f.OwnerEntityName).HasMaxLength(100);
        builder.Property(f => f.Description).HasMaxLength(500);

        builder.Ignore(nameof(StoredFile.ToEntityPath));

        builder.HasIndex(f => new { f.TenantId, f.Category })
            .HasDatabaseName("IX_StoredFiles_TenantId_Category");

        builder.HasIndex(f => f.StorageKey).IsUnique().HasDatabaseName("IX_StoredFiles_StorageKey");
    }
}
