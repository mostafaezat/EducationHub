using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Learning;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationHub.Infrastructure.Persistence.Configurations;

/// <summary>Schema for homework, exams and in-app notifications (Mission Phase 6).</summary>
public sealed class HomeworkConfiguration : IEntityTypeConfiguration<Homework>
{
    public void Configure(EntityTypeBuilder<Homework> builder)
    {
        builder.ToTable("Homeworks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.MaxGrade).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.TenantId, x.DueDate }).HasDatabaseName("IX_Homeworks_TenantId_DueDate");
        builder.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.StudyGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class HomeworkSubmissionConfiguration : IEntityTypeConfiguration<HomeworkSubmission>
{
    public void Configure(EntityTypeBuilder<HomeworkSubmission> builder)
    {
        builder.ToTable("HomeworkSubmissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Grade).HasPrecision(18, 2);
        builder.Property(x => x.Feedback).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.HomeworkId, x.StudentProfileId }).IsUnique()
            .HasDatabaseName("IX_HomeworkSubmissions_TenantId_HomeworkId_StudentProfileId");
        builder.HasOne<Homework>().WithMany().HasForeignKey(x => x.HomeworkId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> builder)
    {
        builder.ToTable("Exams");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.MaxScore).HasPrecision(18, 2);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.ExamDate }).HasDatabaseName("IX_Exams_TenantId_ExamDate");
        builder.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.StudyGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ExamResultConfiguration : IEntityTypeConfiguration<ExamResult>
{
    public void Configure(EntityTypeBuilder<ExamResult> builder)
    {
        builder.ToTable("ExamResults");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Score).HasPrecision(18, 2);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.ExamId, x.StudentProfileId }).IsUnique()
            .HasDatabaseName("IX_ExamResults_TenantId_ExamId_StudentProfileId");
        builder.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Body).HasMaxLength(1000);
        builder.Property(x => x.Link).HasMaxLength(300);
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.IsRead }).HasDatabaseName("IX_Notifications_TenantId_UserId_IsRead");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
