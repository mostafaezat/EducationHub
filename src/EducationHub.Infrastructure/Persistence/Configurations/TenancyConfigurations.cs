using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Audit;
using EducationHub.Domain.Entities.Files;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationHub.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.NameAr).HasMaxLength(200);
        builder.Property(t => t.Slug).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.DescriptionAr).HasMaxLength(1000);
        builder.Property(t => t.Email).HasMaxLength(256);
        builder.Property(t => t.Phone).HasMaxLength(30);
        builder.Property(t => t.Address).HasMaxLength(500);
        builder.Property(t => t.AddressAr).HasMaxLength(500);
        builder.Property(t => t.City).HasMaxLength(100);
        builder.Property(t => t.Country).HasMaxLength(100);
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(t => t.DefaultLanguage).HasMaxLength(5).IsRequired();

        // The slug is what routes and subdomains resolve, so it must be unique across the
        // platform - not merely within a tenant, since it is the tenant's own identifier.
        builder.HasIndex(t => t.Slug)
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL")
            .HasDatabaseName("IX_Tenants_Slug");

        builder.HasIndex(t => t.Status).HasDatabaseName("IX_Tenants_Status");
        builder.HasIndex(t => t.IsDeleted).HasDatabaseName("IX_Tenants_IsDeleted");

        builder.HasMany(t => t.Branches)
            .WithOne(b => b.Tenant)
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Settings)
            .WithOne(s => s.Tenant)
            .HasForeignKey<TenantSettings>(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.NameAr).HasMaxLength(200);
        builder.Property(b => b.Code).HasMaxLength(50);
        builder.Property(b => b.Email).HasMaxLength(256);
        builder.Property(b => b.Phone).HasMaxLength(30);
        builder.Property(b => b.Address).HasMaxLength(500);
        builder.Property(b => b.AddressAr).HasMaxLength(500);
        builder.Property(b => b.City).HasMaxLength(100);

        // Uniqueness is per tenant, not global: two different centers may both call a
        // branch "Main".
        builder.HasIndex(b => new { b.TenantId, b.Code })
            .IsUnique()
            .HasFilter("[Code] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("IX_Branches_TenantId_Code");

        builder.HasIndex(b => new { b.TenantId, b.IsDeleted })
            .HasDatabaseName("IX_Branches_TenantId_IsDeleted");
    }
}

public sealed class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("TenantSettings");

        // TenantId is both the primary key and the relationship to the tenant. That makes
        // 1:1 structural: a tenant cannot have two settings rows, and a settings row
        // cannot exist without its tenant.
        builder.HasKey(s => s.TenantId);

        builder.Property(s => s.DateFormat).HasMaxLength(20).IsRequired();
        builder.Property(s => s.CurrencySymbol).HasMaxLength(10).IsRequired();

        builder.Ignore(s => s.Tenant);

        // The composite index on TenantId from the base class would be redundant once
        // TenantId is the primary key (which already has its own clustered index).
        builder.HasIndex(s => new { s.TenantId, s.IsDeleted })
            .HasDatabaseName("IX_TenantSettings_TenantId_IsDeleted");
    }
}

public sealed class AcademicYearConfiguration : IEntityTypeConfiguration<AcademicYear>
{
    public void Configure(EntityTypeBuilder<AcademicYear> builder)
    {
        builder.ToTable("AcademicYears");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(50).IsRequired();
        builder.Property(a => a.NameAr).HasMaxLength(50);

        builder.HasIndex(a => new { a.TenantId, a.Name }).IsUnique()
            .HasDatabaseName("IX_AcademicYears_TenantId_Name");

        // Only one current year per tenant. The filtered unique index makes that a
        // database guarantee rather than an application convention.
        builder.HasIndex(a => a.TenantId)
            .IsUnique()
            .HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("IX_AcademicYears_TenantId_IsCurrent");

        builder.HasIndex(a => new { a.TenantId, a.IsDeleted })
            .HasDatabaseName("IX_AcademicYears_TenantId_IsDeleted");
    }
}

public sealed class GradeLevelConfiguration : IEntityTypeConfiguration<GradeLevel>
{
    public void Configure(EntityTypeBuilder<GradeLevel> builder)
    {
        builder.ToTable("GradeLevels");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Code).HasMaxLength(50).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(200).IsRequired();
        builder.Property(g => g.NameAr).HasMaxLength(200);
        builder.Property(g => g.Description).HasMaxLength(1000);
        builder.Property(g => g.DescriptionAr).HasMaxLength(1000);

        builder.HasIndex(g => new { g.TenantId, g.Code }).IsUnique()
            .HasDatabaseName("IX_GradeLevels_TenantId_Code");

        // Filtered on IsDeleted (BL-016): a soft-deleted grade must not keep its Order slot.
        builder.HasIndex(g => new { g.TenantId, g.Order }).IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_GradeLevels_TenantId_Order");
    }
}

public sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("Subjects");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.Property(s => s.DescriptionAr).HasMaxLength(1000);

        builder.HasIndex(s => new { s.TenantId, s.Code }).IsUnique()
            .HasDatabaseName("IX_Subjects_TenantId_Code");

        builder.HasIndex(s => new { s.TenantId, s.IsDeleted })
            .HasDatabaseName("IX_Subjects_TenantId_IsDeleted");
    }
}
