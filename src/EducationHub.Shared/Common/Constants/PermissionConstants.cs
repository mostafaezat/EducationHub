namespace EducationHub.Shared.Common.Constants;

/// <summary>
/// The complete permission catalog. This is the SINGLE source of truth: the seed data
/// writes these codes into Identity role claims, the authorization handler checks them
/// on every request, and the frontend renders nav items from the same list.
///
/// Adding a permission here makes it available everywhere at once. There is
/// deliberately no parallel hand-maintained flat list - <see cref="GetAll"/> is derived
/// from <see cref="Areas"/> - because two parallel lists inevitably drift apart.
/// </summary>
public static class PermissionConstants
{
    // ---- Authentication -----------------------------------------------------
    public const string Login = "Auth.Login";
    public const string ChangePassword = "Auth.ChangePassword";

    // ---- Platform administration -------------------------------------------
    public const string PlatformView = "Platform.View";
    public const string PlatformManageTenants = "Platform.ManageTenants";
    public const string PlatformManagePlans = "Platform.ManagePlans";
    public const string PlatformManageSettings = "Platform.ManageSettings";
    public const string PlatformViewActivity = "Platform.ViewActivity";

    // ---- Users / roles / permissions ---------------------------------------
    public const string UsersView = "Users.View";
    public const string UsersCreate = "Users.Create";
    public const string UsersUpdate = "Users.Update";
    public const string UsersDelete = "Users.Delete";
    public const string UsersAssignRoles = "Users.AssignRoles";
    public const string UsersResetPassword = "Users.ResetPassword";
    public const string UsersToggleActive = "Users.ToggleActive";

    public const string RolesView = "Roles.View";
    public const string RolesCreate = "Roles.Create";
    public const string RolesUpdate = "Roles.Update";
    public const string RolesDelete = "Roles.Delete";
    public const string RolesAssignPermissions = "Roles.AssignPermissions";

    public const string PermissionsView = "Permissions.View";

    // ---- Tenancy ------------------------------------------------------------
    public const string TenantsView = "Tenants.View";
    public const string TenantsUpdate = "Tenants.Update";

    public const string BranchesView = "Branches.View";
    public const string BranchesCreate = "Branches.Create";
    public const string BranchesUpdate = "Branches.Update";
    public const string BranchesDelete = "Branches.Delete";

    public const string SettingsView = "Settings.View";
    public const string SettingsUpdate = "Settings.Update";

    // ---- Academic structure -------------------------------------------------
    public const string AcademicYearsView = "AcademicYears.View";
    public const string AcademicYearsCreate = "AcademicYears.Create";
    public const string AcademicYearsUpdate = "AcademicYears.Update";
    public const string AcademicYearsDelete = "AcademicYears.Delete";

    public const string GradesView = "Grades.View";
    public const string GradesCreate = "Grades.Create";
    public const string GradesUpdate = "Grades.Update";
    public const string GradesDelete = "Grades.Delete";

    public const string SubjectsView = "Subjects.View";
    public const string SubjectsCreate = "Subjects.Create";
    public const string SubjectsUpdate = "Subjects.Update";
    public const string SubjectsDelete = "Subjects.Delete";

    // ---- Teachers & compensation -------------------------------------------
    public const string TeachersView = "Teachers.View";
    public const string TeachersCreate = "Teachers.Create";
    public const string TeachersUpdate = "Teachers.Update";
    public const string TeachersDelete = "Teachers.Delete";

    // ---- Students & parents (Mission Phase 2.5) ----------------------------
    public const string StudentsView = "Students.View";
    public const string StudentsCreate = "Students.Create";
    public const string StudentsUpdate = "Students.Update";
    public const string StudentsDelete = "Students.Delete";

    public const string ParentsView = "Parents.View";
    public const string ParentsCreate = "Parents.Create";
    public const string ParentsUpdate = "Parents.Update";
    public const string ParentsDelete = "Parents.Delete";

    /// <summary>A parent reading their own children (scoped to the caller's account).</summary>
    public const string ParentsViewOwnChildren = "Parents.ViewOwnChildren";

    // ---- Mission Phases 2.5b-6: files, scheduling, finance, learning ------
    public const string FilesView = "Files.View";
    public const string FilesUpload = "Files.Upload";
    public const string FilesDelete = "Files.Delete";

    public const string GroupsView = "Groups.View";
    public const string GroupsCreate = "Groups.Create";
    public const string GroupsUpdate = "Groups.Update";
    public const string GroupsDelete = "Groups.Delete";

    public const string SessionsView = "Sessions.View";
    public const string SessionsManage = "Sessions.Manage";

    public const string AttendanceView = "Attendance.View";
    public const string AttendanceMark = "Attendance.Mark";

    public const string FeesView = "Fees.View";
    public const string FeesManage = "Fees.Manage";

    public const string PaymentsView = "Payments.View";
    public const string PaymentsCreate = "Payments.Create";
    public const string PaymentsVoid = "Payments.Void";

    public const string FinanceView = "Finance.View";
    public const string FinanceManage = "Finance.Manage";

    public const string ReportsView = "Reports.View";

    public const string HomeworkView = "Homework.View";
    public const string HomeworkManage = "Homework.Manage";

    public const string ExamsView = "Exams.View";
    public const string ExamsManage = "Exams.Manage";

    // ---- Dashboard ----------------------------------------------------------
    public const string DashboardView = "Dashboard.View";

    // ---- Audit --------------------------------------------------------------
    public const string AuditLogsView = "AuditLogs.View";

    /// <summary>
    /// Permission areas mapped to the display label used by the role editor.
    /// The dictionary is authoritative; <see cref="GetAll"/> enumerates its values,
    /// so a permission can never exist in one list but not the other.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Areas { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["Auth"] =
            [
                Login,
                ChangePassword
            ],
            ["Platform"] =
            [
                PlatformView,
                PlatformManageTenants,
                PlatformManagePlans,
                PlatformManageSettings,
                PlatformViewActivity
            ],
            ["Users"] =
            [
                UsersView,
                UsersCreate,
                UsersUpdate,
                UsersDelete,
                UsersAssignRoles,
                UsersResetPassword,
                UsersToggleActive
            ],
            ["Roles"] =
            [
                RolesView,
                RolesCreate,
                RolesUpdate,
                RolesDelete,
                RolesAssignPermissions
            ],
            ["Permissions"] =
            [
                PermissionsView
            ],
            ["Tenants"] =
            [
                TenantsView,
                TenantsUpdate
            ],
            ["Branches"] =
            [
                BranchesView,
                BranchesCreate,
                BranchesUpdate,
                BranchesDelete
            ],
            ["Settings"] =
            [
                SettingsView,
                SettingsUpdate
            ],
            ["AcademicYears"] =
            [
                AcademicYearsView,
                AcademicYearsCreate,
                AcademicYearsUpdate,
                AcademicYearsDelete
            ],
            ["Grades"] =
            [
                GradesView,
                GradesCreate,
                GradesUpdate,
                GradesDelete
            ],
            ["Subjects"] =
            [
                SubjectsView,
                SubjectsCreate,
                SubjectsUpdate,
                SubjectsDelete
            ],
            ["Teachers"] =
            [
                TeachersView,
                TeachersCreate,
                TeachersUpdate,
                TeachersDelete
            ],
            ["Students"] =
            [
                StudentsView,
                StudentsCreate,
                StudentsUpdate,
                StudentsDelete
            ],
            ["Parents"] =
            [
                ParentsView,
                ParentsCreate,
                ParentsUpdate,
                ParentsDelete,
                ParentsViewOwnChildren
            ],
            ["Files"] =
            [
                FilesView,
                FilesUpload,
                FilesDelete
            ],
            ["Groups"] =
            [
                GroupsView,
                GroupsCreate,
                GroupsUpdate,
                GroupsDelete
            ],
            ["Sessions"] =
            [
                SessionsView,
                SessionsManage
            ],
            ["Attendance"] =
            [
                AttendanceView,
                AttendanceMark
            ],
            ["Fees"] =
            [
                FeesView,
                FeesManage
            ],
            ["Payments"] =
            [
                PaymentsView,
                PaymentsCreate,
                PaymentsVoid
            ],
            ["Finance"] =
            [
                FinanceView,
                FinanceManage
            ],
            ["Reports"] =
            [
                ReportsView
            ],
            ["Homework"] =
            [
                HomeworkView,
                HomeworkManage
            ],
            ["Exams"] =
            [
                ExamsView,
                ExamsManage
            ],
            ["Dashboard"] =
            [
                DashboardView
            ],
            ["AuditLogs"] =
            [
                AuditLogsView
            ]
        };

    /// <summary>Flattened catalog derived from <see cref="Areas"/>.</summary>
    public static IReadOnlyList<string> GetAll()
        => [.. Areas.Values.SelectMany(static v => v)];

    /// <summary>Label shown for a permission code, e.g. <c>Students.Create</c> -> <c>Create</c>.</summary>
    public static string DescribeAction(string permission)
    {
        var separator = permission.LastIndexOf('.');
        return separator >= 0 && separator < permission.Length - 1
            ? permission[(separator + 1)..]
            : permission;
    }

    /// <summary>Area shown for a permission code, e.g. <c>Students.Create</c> -> <c>Students</c>.</summary>
    public static string DescribeArea(string permission)
    {
        var separator = permission.IndexOf('.');
        return separator > 0 ? permission[..separator] : permission;
    }
}
