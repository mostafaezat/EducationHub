using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Teachers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationHub.Infrastructure.Persistence.Configurations;

/// <summary>
/// Schema for the teachers module: profile, subject assignments and compensation rules.
/// </summary>
public sealed class TeacherProfileConfiguration : IEntityTypeConfiguration<TeacherProfile>
{
    public void Configure(EntityTypeBuilder<TeacherProfile> builder)
    {
        builder.ToTable("TeacherProfiles");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.EmployeeNumber).HasMaxLength(50);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        // One profile per account: a second profile for the same user would silently split
        // that teacher's subjects and compensation across two rows.
        builder.HasIndex(t => new { t.TenantId, t.UserId }).IsUnique()
            .HasDatabaseName("IX_TeacherProfiles_TenantId_UserId");

        // Staff numbers are optional; the filtered unique lets many nulls coexist while a
        // set number is unique per tenant. Soft-deleted rows keep occupying their number,
        // matching how grades and subjects treat their codes.
        builder.HasIndex(t => new { t.TenantId, t.EmployeeNumber }).IsUnique()
            .HasDatabaseName("IX_TeacherProfiles_TenantId_EmployeeNumber")
            .HasFilter("[EmployeeNumber] IS NOT NULL");

        builder.HasIndex(t => new { t.TenantId, t.IsDeleted })
            .HasDatabaseName("IX_TeacherProfiles_TenantId_IsDeleted");

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.User).IsRequired();
    }
}

public sealed class TeacherSubjectConfiguration : IEntityTypeConfiguration<TeacherSubject>
{
    public void Configure(EntityTypeBuilder<TeacherSubject> builder)
    {
        builder.ToTable("TeacherSubjects");
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => new { t.TenantId, t.TeacherProfileId, t.SubjectId }).IsUnique()
            .HasDatabaseName("IX_TeacherSubjects_TenantId_TeacherProfileId_SubjectId");

        builder.HasOne(t => t.TeacherProfile)
            .WithMany(p => p.Subjects)
            .HasForeignKey(t => t.TeacherProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Subject)
            .WithMany()
            .HasForeignKey(t => t.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CompensationRuleConfiguration : IEntityTypeConfiguration<CompensationRule>
{
    public void Configure(EntityTypeBuilder<CompensationRule> builder)
    {
        builder.ToTable("CompensationRules");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Rate).HasPrecision(18, 3);

        // At most one active-shape rule per scope bucket, mirroring the resolution
        // precedence: the ladder must never find two rules tied on the same level.
        // Each filter names only non-null columns, because SQL Server treats NULLs in a
        // unique index as distinct and would let duplicates through.
        builder.HasIndex(c => new { c.TenantId, c.TeacherProfileId, c.StudyGroupId })
            .IsUnique()
            .HasDatabaseName("IX_CompensationRules_TenantId_TeacherProfileId_StudyGroupId")
            .HasFilter("[TeacherProfileId] IS NOT NULL AND [StudyGroupId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(c => new { c.TenantId, c.TeacherProfileId, c.SubjectId })
            .IsUnique()
            .HasDatabaseName("IX_CompensationRules_TenantId_TeacherProfileId_SubjectId")
            .HasFilter("[TeacherProfileId] IS NOT NULL AND [SubjectId] IS NOT NULL AND [StudyGroupId] IS NULL AND [IsDeleted] = 0");

        builder.HasIndex(c => new { c.TenantId, c.TeacherProfileId })
            .IsUnique()
            .HasDatabaseName("IX_CompensationRules_TenantId_TeacherProfileId")
            .HasFilter("[TeacherProfileId] IS NOT NULL AND [SubjectId] IS NULL AND [StudyGroupId] IS NULL AND [IsDeleted] = 0");

        builder.HasIndex(c => new { c.TenantId, c.StudyGroupId, c.SubjectId })
            .IsUnique()
            .HasDatabaseName("IX_CompensationRules_TenantId_StudyGroupId_SubjectId")
            .HasFilter("[TeacherProfileId] IS NULL AND [StudyGroupId] IS NOT NULL AND [SubjectId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(c => new { c.TenantId, c.StudyGroupId })
            .IsUnique()
            .HasDatabaseName("IX_CompensationRules_TenantId_StudyGroupId")
            .HasFilter("[TeacherProfileId] IS NULL AND [StudyGroupId] IS NOT NULL AND [SubjectId] IS NULL AND [IsDeleted] = 0");

        builder.HasIndex(c => new { c.TenantId, c.SubjectId })
            .IsUnique()
            .HasDatabaseName("IX_CompensationRules_TenantId_SubjectId")
            .HasFilter("[TeacherProfileId] IS NULL AND [StudyGroupId] IS NULL AND [SubjectId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(c => c.TenantId)
            .IsUnique()
            .HasDatabaseName("IX_CompensationRules_TenantId")
            .HasFilter("[TeacherProfileId] IS NULL AND [SubjectId] IS NULL AND [StudyGroupId] IS NULL AND [IsDeleted] = 0");

        builder.HasOne(c => c.TeacherProfile)
            .WithMany(p => p.CompensationRules)
            .HasForeignKey(c => c.TeacherProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Subject)
            .WithMany()
            .HasForeignKey(c => c.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mission Phase 3: group-scoped rules now reference a real study group.
        builder.HasOne<EducationHub.Domain.Entities.Scheduling.StudyGroup>()
            .WithMany()
            .HasForeignKey(c => c.StudyGroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
