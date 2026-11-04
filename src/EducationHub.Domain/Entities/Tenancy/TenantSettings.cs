using EducationHub.Domain.Common;

namespace EducationHub.Domain.Entities.Tenancy;

/// <summary>
/// One row per tenant holding the configurable business rules the platform must never
/// hard-code (spec section 11). Created alongside the tenant and kept 1:1.
/// </summary>
/// <remarks>
/// <para>
/// A dedicated entity rather than columns on <see cref="Tenant"/> because
/// <see cref="TenantOwnedEntity.TenantId"/> is configured as the PRIMARY KEY here
/// (<c>HasKey(e =&gt; e.TenantId)</c> in <c>TenantSettingsConfiguration</c>), which makes
/// the relationship 1:1 at the database level: a settings row cannot be orphaned, and a
/// tenant cannot silently accumulate duplicate settings rows.
/// </para>
/// <para>
/// <see cref="TenantId"/> is inherited rather than redeclared. Redeclaring it would hide
/// the base member and, more importantly, would risk the tenant query filter no longer
/// recognising the entity as tenant-owned.
/// </para>
/// </remarks>
public class TenantSettings : TenantOwnedEntity
{
    // ---- Attendance rules ---------------------------------------------------
    /// <summary>Send a parent notification automatically when a student is marked absent.</summary>
    public bool NotifyParentOnAbsence { get; set; } = true;

    /// <summary>Send a parent notification when a session is cancelled.</summary>
    public bool NotifyParentOnSessionCancellation { get; set; } = true;

    /// <summary>Minutes after session start before a student counts as late rather than absent.</summary>
    public int LateThresholdMinutes { get; set; } = 15;

    /// <summary>Whether a teacher must justify an "Excused" absence.</summary>
    public bool RequireExcuseReason { get; set; } = true;

    // ---- Fee rules ----------------------------------------------------------
    /// <summary>Charge the group's monthly fee for every enrolled student.</summary>
    public bool AutoGenerateMonthlyFees { get; set; } = true;

    /// <summary>Day of month monthly fees become due (1-28).</summary>
    public int FeeDueDayOfMonth { get; set; } = 5;

    /// <summary>Days after the due date before an unpaid fee is reported as overdue.</summary>
    public int GracePeriodDays { get; set; } = 7;

    /// <summary>Default automatic sibling discount percentage, e.g. 10 for 10%.</summary>
    public decimal SiblingDiscountPercentage { get; set; } = 0m;

    // ---- Grading rules ------------------------------------------------------
    /// <summary>Default percentage at or above which a result counts as a pass.</summary>
    public decimal DefaultPassingPercentage { get; set; } = 50m;

    /// <summary>Use the tenant's own grading scale instead of the system default.</summary>
    public bool UseCustomGradingScale { get; set; }

    // ---- Notification rules -------------------------------------------------
    public bool NotifyParentOnFeeDue { get; set; } = true;
    public bool NotifyParentOnPaymentReceived { get; set; } = true;
    public bool NotifyParentOnHomeworkAssigned { get; set; } = true;
    public bool NotifyParentOnExamResultPublished { get; set; } = true;
    public bool NotifyParentOnMakeupClassScheduled { get; set; } = true;

    // ---- Display ------------------------------------------------------------
    public string DateFormat { get; set; } = "dd/MM/yyyy";

    public string CurrencySymbol { get; set; } = "EGP";

    public Tenant Tenant { get; set; } = null!;
}
