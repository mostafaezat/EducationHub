using EducationHub.Domain.Entities.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationHub.Infrastructure.Persistence.Configurations;

/// <summary>
/// Schema for the students module (Mission Phase 2.5): student and parent profiles and the
/// guardian links between them.
/// </summary>
/// <remarks>
/// Every foreign key into another module (account, grade, branch) is restrict-on-delete:
/// those rows are soft-deleted in practice, and a hard delete must fail loudly rather than
/// cascade away student history. Only the guardian junction cascades from its two ends.
/// </remarks>
public sealed class StudentProfileConfiguration : IEntityTypeConfiguration<StudentProfile>
{
    public void Configure(EntityTypeBuilder<StudentProfile> builder)
    {
        builder.ToTable("StudentProfiles");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.FullName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.StudentNumber).HasMaxLength(50);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        // Same convention as teacher employee numbers: optional, unique per tenant when set,
        // soft-deleted rows keep their number.
        builder.HasIndex(s => new { s.TenantId, s.StudentNumber }).IsUnique()
            .HasDatabaseName("IX_StudentProfiles_TenantId_StudentNumber")
            .HasFilter("[StudentNumber] IS NOT NULL");

        // One profile per account when an account is linked.
        builder.HasIndex(s => new { s.TenantId, s.UserId }).IsUnique()
            .HasDatabaseName("IX_StudentProfiles_TenantId_UserId")
            .HasFilter("[UserId] IS NOT NULL");

        builder.HasIndex(s => new { s.TenantId, s.IsDeleted })
            .HasDatabaseName("IX_StudentProfiles_TenantId_IsDeleted");

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.GradeLevel)
            .WithMany()
            .HasForeignKey(s => s.GradeLevelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Branch)
            .WithMany()
            .HasForeignKey(s => s.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ParentProfileConfiguration : IEntityTypeConfiguration<ParentProfile>
{
    public void Configure(EntityTypeBuilder<ParentProfile> builder)
    {
        builder.ToTable("ParentProfiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.FullName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.PhoneNumber).HasMaxLength(30);
        builder.Property(p => p.Email).HasMaxLength(256);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.HasIndex(p => new { p.TenantId, p.UserId }).IsUnique()
            .HasDatabaseName("IX_ParentProfiles_TenantId_UserId")
            .HasFilter("[UserId] IS NOT NULL");

        builder.HasIndex(p => new { p.TenantId, p.IsDeleted })
            .HasDatabaseName("IX_ParentProfiles_TenantId_IsDeleted");

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StudentGuardianConfiguration : IEntityTypeConfiguration<StudentGuardian>
{
    public void Configure(EntityTypeBuilder<StudentGuardian> builder)
    {
        builder.ToTable("StudentGuardians");
        builder.HasKey(g => g.Id);

        builder.HasIndex(g => new { g.TenantId, g.StudentProfileId, g.ParentProfileId }).IsUnique()
            .HasDatabaseName("IX_StudentGuardians_TenantId_StudentProfileId_ParentProfileId");

        builder.HasOne(g => g.Student)
            .WithMany(s => s.Guardians)
            .HasForeignKey(g => g.StudentProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(g => g.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(g => g.ParentProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Pre-registration requests from the public form.</summary>
public sealed class RegistrationRequestConfiguration : IEntityTypeConfiguration<RegistrationRequest>
{
    public void Configure(EntityTypeBuilder<RegistrationRequest> builder)
    {
        builder.ToTable("RegistrationRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.GuardianName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.GuardianPhone).HasMaxLength(30).IsRequired();
        builder.Property(r => r.GuardianEmail).HasMaxLength(256);
        builder.Property(r => r.StudentName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.School).HasMaxLength(200);
        builder.Property(r => r.PreferredSubjects).HasMaxLength(500);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.Property(r => r.SourceIp).HasMaxLength(64);
        builder.Property(r => r.RowVersion).IsRowVersion();

        // The staff list filters by status; the public form checks pending duplicates by phone.
        builder.HasIndex(r => new { r.TenantId, r.Status, r.CreatedAt })
            .HasDatabaseName("IX_RegistrationRequests_TenantId_Status_CreatedAt");

        builder.HasIndex(r => new { r.TenantId, r.GuardianPhone })
            .HasDatabaseName("IX_RegistrationRequests_TenantId_GuardianPhone");

        // One pending request per student and mobile number. The handler checks first; the index
        // turns a race between two identical submissions into a 409 instead of a duplicate.
        builder.HasIndex(r => new { r.TenantId, r.GuardianPhone, r.StudentName }).IsUnique()
            .HasDatabaseName("IX_RegistrationRequests_Pending_Unique")
            .HasFilter("[Status] = 0 AND [IsDeleted] = 0");

        builder.HasOne<EducationHub.Domain.Entities.Academic.GradeLevel>()
            .WithMany()
            .HasForeignKey(r => r.GradeLevelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StudentProfile>()
            .WithMany()
            .HasForeignKey(r => r.CreatedStudentProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
