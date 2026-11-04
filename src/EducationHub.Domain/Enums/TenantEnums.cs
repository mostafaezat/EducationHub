namespace EducationHub.Domain.Enums;

public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    Suspended = 2,
    Expired = 3,
    Cancelled = 4
}

/// <summary>
/// How a tenant relates to branches (spec sections 5 and 17). An individual teacher must
/// not be forced to create a branch, so the branch concept is optional per tenant and the
/// whole application branches off this flag rather than off "does a branch row exist".
/// </summary>
public enum TenantType
{
    /// <summary>One teacher. No branches.</summary>
    IndividualTeacher = 0,

    /// <summary>A tutoring business or education center. One or more branches.</summary>
    EducationCenter = 1
}

public enum BranchStatus
{
    Active = 0,
    Inactive = 1
}

public enum AcademicYearStatus
{
    Upcoming = 0,
    Current = 1,
    Archived = 2
}

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    LoggedIn = 3,
    LoggedOut = 4,
    PasswordChanged = 5,
    PasswordReset = 6,
    PermissionChanged = 7,
    RoleAssigned = 8,
    RoleRemoved = 9,
    AccountActivated = 10,
    AccountDeactivated = 11
}

public enum StoredFileCategory
{
    TenantLogo = 0,
    StudentPhoto = 1,
    TeacherPhoto = 2,
    StudentDocument = 3,
    TeacherDocument = 4,
    AssignmentAttachment = 5,
    MaterialAttachment = 6,
    PaymentReceipt = 7,
    ExamAttachment = 8,
    AnnouncementAttachment = 9
}
