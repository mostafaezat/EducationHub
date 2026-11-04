namespace EducationHub.Shared.Common.Localization;

/// <summary>
/// Resource keys used by both the API and the Angular app. The API localizes its own
/// validation and error messages; the frontend localizes its UI. Keeping the keys in one
/// shared assembly is what guarantees the two never drift.
/// </summary>
/// <remarks>
/// Keys are grouped by feature and use dot notation, matching the JSON layout in
/// <c>EducationHub.API/Localization/Resources/*.json</c>.
/// </remarks>
public static class LocalizationKeys
{
    // ---- Generic errors -----------------------------------------------------
    public const string ErrorUnexpected = "Errors.Unexpected";
    public const string ErrorValidationFailed = "Errors.ValidationFailed";
    public const string ErrorNotFound = "Errors.NotFound";
    public const string ErrorConflict = "Errors.Conflict";
    public const string ErrorForbidden = "Errors.Forbidden";
    public const string ErrorUnauthorized = "Errors.Unauthorized";
    public const string ErrorTenantMismatch = "Errors.TenantMismatch";
    public const string ErrorNoTenantContext = "Errors.NoTenantContext";
    public const string ErrorInactiveAccount = "Errors.InactiveAccount";
    public const string ErrorTenantInactive = "Errors.TenantInactive";
    public const string ErrorExpiredSubscription = "Errors.ExpiredSubscription";

    // ---- Field-level validation --------------------------------------------
    public const string ValidationRequired = "Validation.Required";
    public const string ValidationEmail = "Validation.Email";
    public const string ValidationMaxLength = "Validation.MaxLength";
    public const string ValidationMinLength = "Validation.MinLength";
    public const string ValidationRange = "Validation.Range";
    public const string ValidationPasswordStrength = "Validation.PasswordStrength";

    // ---- Auth ---------------------------------------------------------------
    public const string AuthLoginFailed = "Auth.LoginFailed";
    public const string AuthInvalidCredentials = "Auth.InvalidCredentials";
    public const string AuthAccountLocked = "Auth.AccountLocked";
    public const string AuthEmailNotConfirmed = "Auth.EmailNotConfirmed";
    public const string AuthInvalidRefreshToken = "Auth.InvalidRefreshToken";
    public const string AuthReuseDetected = "Auth.ReuseDetected";
    public const string AuthPasswordChangeFailed = "Auth.PasswordChangeFailed";
    public const string AuthLogoutSuccess = "Auth.LogoutSuccess";

    // ---- Common UI ----------------------------------------------------------
    public const string CommonSave = "Common.Save";
    public const string CommonCancel = "Common.Cancel";
    public const string CommonDelete = "Common.Delete";
    public const string CommonEdit = "Common.Edit";
    public const string CommonSearch = "Common.Search";
    public const string CommonLoading = "Common.Loading";
    public const string CommonNoData = "Common.NoData";
    public const string CommonActions = "Common.Actions";
    public const string CommonActive = "Common.Active";
    public const string CommonInactive = "Common.Inactive";

    // ---- Navigation ---------------------------------------------------------
    public const string NavDashboard = "Nav.Dashboard";
    public const string NavUsers = "Nav.Users";
    public const string NavRoles = "Nav.Roles";
    public const string NavPermissions = "Nav.Permissions";
    public const string NavTenants = "Nav.Tenants";
    public const string NavBranches = "Nav.Branches";
    public const string NavSettings = "Nav.Settings";
    public const string NavAcademicYears = "Nav.AcademicYears";
    public const string NavGrades = "Nav.Grades";
    public const string NavSubjects = "Nav.Subjects";
    public const string NavAuditLogs = "Nav.AuditLogs";

    // ---- Login page ---------------------------------------------------------
    public const string LoginTitle = "Login.Title";
    public const string LoginEmail = "Login.Email";
    public const string LoginPassword = "Login.Password";
    public const string LoginRememberMe = "Login.RememberMe";
    public const string LoginButton = "Login.Button";

    // ---- Validation messages ------------------------------------------------
    // One key per user-facing validation message. The English value in
    // Localization/Resources/en.json is byte-identical to the literal it replaced, so no
    // wording drift is introduced by keying these.
    public const string RequestBodyRequired = "Validation.RequestBodyRequired";
    public const string EmailRequired = "Validation.EmailRequired";
    public const string EmailMaxLength = "Validation.EmailMaxLength";
    public const string EmailInvalid = "Validation.EmailInvalid";
    public const string EmailValidRequired = "Validation.EmailValidRequired";
    public const string PasswordRequired = "Validation.PasswordRequired";
    public const string PasswordMaxLength = "Validation.PasswordMaxLength";
    public const string PasswordMinLength8 = "Validation.PasswordMinLength8";
    public const string PasswordLetter = "Validation.PasswordLetter";
    public const string PasswordDigit = "Validation.PasswordDigit";
    public const string RefreshTokenRequired = "Validation.RefreshTokenRequired";
    public const string CurrentPasswordRequired = "Validation.CurrentPasswordRequired";
    public const string NewPasswordRequired = "Validation.NewPasswordRequired";
    public const string NewPasswordMinLength = "Validation.NewPasswordMinLength";
    public const string NewPasswordLetter = "Validation.NewPasswordLetter";
    public const string NewPasswordDigit = "Validation.NewPasswordDigit";
    public const string PasswordMismatch = "Validation.PasswordMismatch";
    public const string ResetTokenRequired = "Validation.ResetTokenRequired";
    public const string NewPasswordRequiredSimple = "Validation.NewPasswordRequiredSimple";
    public const string PasswordMinLengthSimple = "Validation.PasswordMinLengthSimple";
    public const string PasswordDigitSimple = "Validation.PasswordDigitSimple";
    public const string PasswordLowerSimple = "Validation.PasswordLowerSimple";
    public const string PasswordMismatchSimple = "Validation.PasswordMismatchSimple";
    public const string ConfirmationTokenRequired = "Validation.ConfirmationTokenRequired";
    public const string FullNameRequired = "Validation.FullNameRequired";
    public const string FullNameMinLength3 = "Validation.FullNameMinLength3";
    public const string FullNameMaxLength200 = "Validation.FullNameMaxLength200";
    public const string FullNameBlank = "Validation.FullNameBlank";
    public const string PreferredLanguage = "Validation.PreferredLanguage";
    public const string RoleRequired = "Validation.RoleRequired";
    public const string RoleDuplicate = "Validation.RoleDuplicate";
    public const string RoleListRequired = "Validation.RoleListRequired";
    public const string RoleAssigned = "Validation.RoleAssigned";
    public const string RoleNameBlank = "Validation.RoleNameBlank";
    public const string RoleNameRequired = "Validation.RoleNameRequired";
    public const string RoleNameMaxLength256 = "Validation.RoleNameMaxLength256";
    public const string RoleNameSpaces = "Validation.RoleNameSpaces";
    public const string RoleNameReserved = "Validation.RoleNameReserved";
    public const string DescriptionMaxLength500 = "Validation.DescriptionMaxLength500";
    public const string DescriptionArMaxLength500 = "Validation.DescriptionArMaxLength500";
    public const string PermissionDuplicate = "Validation.PermissionDuplicate";
    public const string PermissionListRequired = "Validation.PermissionListRequired";
    public const string PermissionBlank = "Validation.PermissionBlank";
    public const string TenantNameRequired = "Validation.TenantNameRequired";
    public const string TenantNameMaxLength200 = "Validation.TenantNameMaxLength200";
    public const string TenantNameChars = "Validation.TenantNameChars";
    public const string TenantNameBlank = "Validation.TenantNameBlank";
    public const string SlugRequired = "Validation.SlugRequired";
    public const string SlugMaxLength100 = "Validation.SlugMaxLength100";
    public const string SlugChars = "Validation.SlugChars";
    public const string Currency = "Validation.Currency";
    public const string TimeZoneRequired = "Validation.TimeZoneRequired";
    public const string DefaultLanguage = "Validation.DefaultLanguage";
    public const string AdminFullNameRequired = "Validation.AdminFullNameRequired";
    public const string AdminFullNameChars = "Validation.AdminFullNameChars";
    public const string AdminEmailRequired = "Validation.AdminEmailRequired";
    public const string AdminEmailMaxLength256 = "Validation.AdminEmailMaxLength256";
    public const string AdminEmailInvalid = "Validation.AdminEmailInvalid";
    public const string AdminPasswordRequired = "Validation.AdminPasswordRequired";
    public const string AdminPasswordMinLength8 = "Validation.AdminPasswordMinLength8";
    public const string AdminPasswordMaxLength128 = "Validation.AdminPasswordMaxLength128";
    public const string AdminPasswordLetter = "Validation.AdminPasswordLetter";
    public const string AdminPasswordDigit = "Validation.AdminPasswordDigit";
    public const string SubscriptionRange = "Validation.SubscriptionRange";
    public const string BranchNameRequired = "Validation.BranchNameRequired";
    public const string BranchNameMaxLength200 = "Validation.BranchNameMaxLength200";
    public const string BranchNameChars = "Validation.BranchNameChars";
    public const string BranchNameBlank = "Validation.BranchNameBlank";
    public const string LateThreshold = "Validation.LateThreshold";
    public const string FeeDueDay = "Validation.FeeDueDay";
    public const string GracePeriod = "Validation.GracePeriod";
    public const string SiblingDiscount = "Validation.SiblingDiscount";
    public const string PassingPercentage = "Validation.PassingPercentage";
    public const string NameRequired = "Validation.NameRequired";
    public const string NameMaxLength100 = "Validation.NameMaxLength100";
    public const string NameArMaxLength100 = "Validation.NameArMaxLength100";
    public const string DateRange = "Validation.DateRange";
    public const string CodeRequired = "Validation.CodeRequired";
    public const string CodeMaxLength20 = "Validation.CodeMaxLength20";
    public const string OrderNonNegative = "Validation.OrderNonNegative";
    public const string TeacherLinkConflicts = "Validation.TeacherLinkConflicts";
    public const string SubjectListRequired = "Validation.SubjectListRequired";
    public const string SubjectDuplicate = "Validation.SubjectDuplicate";
    public const string RateNonNegative = "Validation.RateNonNegative";
    public const string ProfileLinkConflicts = "Validation.ProfileLinkConflicts";
    public const string AccountEmailRequired = "Validation.AccountEmailRequired";
    public const string GuardianListRequired = "Validation.GuardianListRequired";
    public const string GuardianDuplicate = "Validation.GuardianDuplicate";
    public const string GuardianPrimaryMultiple = "Validation.GuardianPrimaryMultiple";

    // ---- API boundary messages ---------------------------------------------
    // Used by middleware, the result filter and the rate limiter. Values are the codes that
    // already appear in those responses, so nothing a client branches on changes.
    public const string ErrorValidation = "Errors.Validation";
    public const string ErrorInternal = "Errors.Internal";
    public const string ErrorFailed = "Errors.Failed";
    public const string AuthForbidden = "Auth.Forbidden";
    public const string AuthMustChangePassword = "Auth.MustChangePassword";
    public const string BranchMismatch = "Errors.BranchMismatch";
    public const string RateLimited = "Errors.TooManyRequests";
    public const string RateLimitedTitle = "Errors.TooManyAttemptsTitle";
}
