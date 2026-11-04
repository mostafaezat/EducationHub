using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationHub.Infrastructure.Persistence.Configurations;

/// <summary>
/// Schema for scheduling (Mission Phase 3). Navigation-free FKs. Cascades run only from the
/// group down to its own slots, sessions, enrollments and from a session to its attendance;
/// every reference into another module is restrict-on-delete.
/// </summary>
public sealed class StudyGroupConfiguration : IEntityTypeConfiguration<StudyGroup>
{
    public void Configure(EntityTypeBuilder<StudyGroup> builder)
    {
        builder.ToTable("StudyGroups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(200);
        builder.Property(g => g.Code).HasMaxLength(50);
        builder.Property(g => g.Notes).HasMaxLength(1000);

        builder.HasIndex(g => new { g.TenantId, g.Name }).IsUnique()
            .HasDatabaseName("IX_StudyGroups_TenantId_Name")
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(g => new { g.TenantId, g.IsDeleted })
            .HasDatabaseName("IX_StudyGroups_TenantId_IsDeleted");

        builder.HasOne<Subject>().WithMany().HasForeignKey(g => g.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GradeLevel>().WithMany().HasForeignKey(g => g.GradeLevelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TeacherProfile>().WithMany().HasForeignKey(g => g.TeacherProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcademicYear>().WithMany().HasForeignKey(g => g.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(g => g.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("Enrollments");
        builder.HasKey(e => e.Id);

        // One live enrollment row per (group, student); ending it keeps the row.
        builder.HasIndex(e => new { e.TenantId, e.StudyGroupId, e.StudentProfileId }).IsUnique()
            .HasDatabaseName("IX_Enrollments_TenantId_StudyGroupId_StudentProfileId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne<StudyGroup>().WithMany().HasForeignKey(e => e.StudyGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(e => e.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ScheduleSlotConfiguration : IEntityTypeConfiguration<ScheduleSlot>
{
    public void Configure(EntityTypeBuilder<ScheduleSlot> builder)
    {
        builder.ToTable("ScheduleSlots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Room).HasMaxLength(100);

        builder.HasIndex(s => new { s.TenantId, s.StudyGroupId, s.DayOfWeek })
            .HasDatabaseName("IX_ScheduleSlots_TenantId_StudyGroupId_DayOfWeek");

        builder.HasOne<StudyGroup>().WithMany().HasForeignKey(s => s.StudyGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
{
    public void Configure(EntityTypeBuilder<ClassSession> builder)
    {
        builder.ToTable("ClassSessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Topic).HasMaxLength(300);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        // Materializing a slot twice for the same date must never duplicate the session.
        builder.HasIndex(s => new { s.TenantId, s.ScheduleSlotId, s.Date }).IsUnique()
            .HasDatabaseName("IX_ClassSessions_TenantId_ScheduleSlotId_Date")
            .HasFilter("[ScheduleSlotId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(s => new { s.TenantId, s.Date })
            .HasDatabaseName("IX_ClassSessions_TenantId_Date");

        builder.HasOne<StudyGroup>().WithMany().HasForeignKey(s => s.StudyGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ScheduleSlot>().WithMany().HasForeignKey(s => s.ScheduleSlotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TeacherProfile>().WithMany().HasForeignKey(s => s.TeacherProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionAttendanceConfiguration : IEntityTypeConfiguration<SessionAttendance>
{
    public void Configure(EntityTypeBuilder<SessionAttendance> builder)
    {
        builder.ToTable("SessionAttendances");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Notes).HasMaxLength(500);

        builder.HasIndex(a => new { a.TenantId, a.ClassSessionId, a.StudentProfileId }).IsUnique()
            .HasDatabaseName("IX_SessionAttendances_TenantId_ClassSessionId_StudentProfileId");

        builder.HasOne<ClassSession>().WithMany().HasForeignKey(a => a.ClassSessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(a => a.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}
