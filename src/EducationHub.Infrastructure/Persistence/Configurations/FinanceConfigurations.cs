using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Finance;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationHub.Infrastructure.Persistence.Configurations;

/// <summary>
/// Schema for fees, payments, cash, expenses and teacher settlements (Mission Phases 4-5).
/// Money columns are decimal(18,2). Only a payment's allocations cascade from it; every
/// other reference is restrict-on-delete because finance rows are history.
/// </summary>
public sealed class FeeRuleConfiguration : IEntityTypeConfiguration<FeeRule>
{
    public void Configure(EntityTypeBuilder<FeeRule> builder)
    {
        builder.ToTable("FeeRules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.IsDeleted }).HasDatabaseName("IX_FeeRules_TenantId_IsDeleted");
        builder.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.StudyGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GradeLevel>().WithMany().HasForeignKey(x => x.GradeLevelId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    public void Configure(EntityTypeBuilder<Charge> builder)
    {
        builder.ToTable("Charges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Description).HasMaxLength(300);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.AmountPaid).HasPrecision(18, 2);
        builder.Property(x => x.PeriodKey).HasMaxLength(20);
        builder.Property(x => x.RowVersion).IsRowVersion();

        // One generated charge per student, group and billing period.
        builder.HasIndex(x => new { x.TenantId, x.StudentProfileId, x.StudyGroupId, x.PeriodKey }).IsUnique()
            .HasDatabaseName("IX_Charges_TenantId_StudentProfileId_StudyGroupId_PeriodKey")
            .HasFilter("[StudyGroupId] IS NOT NULL AND [PeriodKey] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("IX_Charges_TenantId_Status");

        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FeeRule>().WithMany().HasForeignKey(x => x.FeeRuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.StudyGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.VoidReason).HasMaxLength(300);
        builder.HasIndex(x => new { x.TenantId, x.PaidOn }).HasDatabaseName("IX_Payments_TenantId_PaidOn");
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashAccount>().WithMany().HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("PaymentAllocations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.TenantId, x.PaymentId, x.ChargeId }).IsUnique()
            .HasDatabaseName("IX_PaymentAllocations_TenantId_PaymentId_ChargeId");
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Charge>().WithMany().HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CashAccountConfiguration : IEntityTypeConfiguration<CashAccount>
{
    public void Configure(EntityTypeBuilder<CashAccount> builder)
    {
        builder.ToTable("CashAccounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique()
            .HasDatabaseName("IX_CashAccounts_TenantId_Name")
            .HasFilter("[IsDeleted] = 0");
    }
}

public sealed class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> builder)
    {
        builder.ToTable("CashTransactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Description).HasMaxLength(300);

        // Double-post guard: one live posting per (source type, source id).
        builder.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId }).IsUnique()
            .HasDatabaseName("IX_CashTransactions_TenantId_SourceType_SourceId")
            .HasFilter("[SourceId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(x => new { x.TenantId, x.OccurredOn }).HasDatabaseName("IX_CashTransactions_TenantId_OccurredOn");
        builder.HasOne<CashAccount>().WithMany().HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("ExpenseCategories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique()
            .HasDatabaseName("IX_ExpenseCategories_TenantId_Name")
            .HasFilter("[IsDeleted] = 0");
    }
}

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Description).HasMaxLength(300);
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.HasIndex(x => new { x.TenantId, x.SpentOn }).HasDatabaseName("IX_Expenses_TenantId_SpentOn");
        builder.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashAccount>().WithMany().HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeacherPaymentConfiguration : IEntityTypeConfiguration<TeacherPayment>
{
    public void Configure(EntityTypeBuilder<TeacherPayment> builder)
    {
        builder.ToTable("TeacherPayments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.PaidOn }).HasDatabaseName("IX_TeacherPayments_TenantId_PaidOn");
        builder.HasOne<TeacherProfile>().WithMany().HasForeignKey(x => x.TeacherProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashAccount>().WithMany().HasForeignKey(x => x.CashAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionCompensationConfiguration : IEntityTypeConfiguration<SessionCompensation>
{
    public void Configure(EntityTypeBuilder<SessionCompensation> builder)
    {
        builder.ToTable("SessionCompensations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.RowVersion).IsRowVersion();

        // A session is priced once.
        builder.HasIndex(x => new { x.TenantId, x.ClassSessionId }).IsUnique()
            .HasDatabaseName("IX_SessionCompensations_TenantId_ClassSessionId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne<ClassSession>().WithMany().HasForeignKey(x => x.ClassSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TeacherProfile>().WithMany().HasForeignKey(x => x.TeacherProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompensationRule>().WithMany().HasForeignKey(x => x.CompensationRuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TeacherPayment>().WithMany().HasForeignKey(x => x.TeacherPaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
