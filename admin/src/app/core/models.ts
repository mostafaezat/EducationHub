export interface ApiError {
  code: string;
  message: string;
}

export interface Result<T> {
  succeeded: boolean;
  isFailure: boolean;
  message: string;
  errors: ApiError[];
  data: T | null;
}

export interface PaginatedList<T> {
  items: T[];
  totalItems: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: ApiError[];
  traceId?: string;
}

export type SortOrder = 'Asc' | 'Desc';

export interface PaginationQuery {
  pageNumber?: number;
  pageSize?: number;
  searchTerm?: string;
  sortBy?: string;
  sortOrder?: SortOrder;
}

export interface PermissionAreaDto {
  area: string;
  permissions: string[];
}

export type TenantType = 'IndividualTeacher' | 'EducationCenter';
export type TenantStatus = 'Trial' | 'Active' | 'Suspended' | 'Expired' | 'Cancelled';
export type BranchStatus = 'Active' | 'Inactive';
export type AcademicYearStatus = 'Upcoming' | 'Current' | 'Archived';
export type AuditAction =
  | 'Created'
  | 'Updated'
  | 'Deleted'
  | 'LoggedIn'
  | 'LoggedOut'
  | 'PasswordChanged'
  | 'PasswordReset'
  | 'PermissionChanged'
  | 'RoleAssigned'
  | 'RoleRemoved'
  | 'AccountActivated'
  | 'AccountDeactivated';

export const TENANT_TYPES: TenantType[] = ['IndividualTeacher', 'EducationCenter'];
export const TENANT_STATUSES: TenantStatus[] = ['Trial', 'Active', 'Suspended', 'Expired', 'Cancelled'];
export const BRANCH_STATUSES: BranchStatus[] = ['Active', 'Inactive'];
export const ACADEMIC_YEAR_STATUSES: AcademicYearStatus[] = ['Upcoming', 'Current', 'Archived'];
export const AUDIT_ACTIONS: AuditAction[] = [
  'Created',
  'Updated',
  'Deleted',
  'LoggedIn',
  'LoggedOut',
  'PasswordChanged',
  'PasswordReset',
  'PermissionChanged',
  'RoleAssigned',
  'RoleRemoved',
  'AccountActivated',
  'AccountDeactivated'
];

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe?: boolean;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ForgotPasswordByPhoneRequest {
  phone: string;
}

export interface ResetPasswordByPhoneRequest {
  phone: string;
  code: string;
  newPassword: string;
  confirmPassword: string;
}

export interface ResetPasswordRequest {
  email: string;
  resetToken: string;
  newPassword: string;
  confirmPassword: string;
}

export interface ConfirmEmailRequest {
  email: string;
  token: string;
}

export interface ResendConfirmationRequest {
  email: string;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  tokenType: string;
  user: UserProfileDto;
}

export interface UserProfileDto {
  id: string;
  fullName: string;
  email: string;
  emailConfirmed: boolean;
  phoneNumber: string | null;
  tenantId: string;
  branchId: string | null;
  tenantName: string | null;
  preferredLanguage: string;
  isActive: boolean;
  mustChangePassword: boolean;
  roles: string[];
  permissions: string[];
}

export interface UserDto {
  id: string;
  fullName: string;
  email: string;
  emailConfirmed: boolean;
  phoneNumber: string | null;
  tenantId: string;
  branchId: string | null;
  preferredLanguage: string;
  isActive: boolean;
  mustChangePassword: boolean;
  isLockedOut: boolean;
  lockedOutUntil: string | null;
  lastLoginAt: string | null;
  roles: string[];
  permissions: string[];
  createdAt: string;
}

export interface CreateUserRequest {
  fullName: string;
  email: string;
  password: string;
  phoneNumber: string | null;
  branchId: string | null;
  preferredLanguage: string | null;
  mustChangePassword: boolean;
  roles: string[];
  sendEmailConfirmation: boolean;
}

export interface UpdateUserRequest {
  fullName: string | null;
  phoneNumber: string | null;
  branchId: string | null;
  preferredLanguage: string | null;
  isActive: boolean | null;
  notes: string | null;
}

export interface AdminResetPasswordRequest {
  newPassword: string;
  mustChangePassword: boolean;
}

export interface AssignRolesRequest {
  roles: string[];
}

export interface ChangeUserStatusRequest {
  isActive: boolean;
  reason: string | null;
}

export interface RoleDto {
  id: string;
  name: string;
  description: string | null;
  descriptionAr: string | null;
  isSystemRole: boolean;
  isActive: boolean;
  tenantId: string | null;
  permissions: string[];
  userCount: number;
  createdAt: string;
}

export interface CreateRoleRequest {
  name: string;
  description: string | null;
  descriptionAr: string | null;
  permissions: string[] | null;
}

export interface UpdateRoleRequest {
  description: string | null;
  descriptionAr: string | null;
  isActive: boolean | null;
}

export interface AssignPermissionsRequest {
  permissions: string[];
}

export interface TenantDto {
  id: string;
  slug: string;
  name: string;
  nameAr: string | null;
  description: string | null;
  descriptionAr: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  addressAr: string | null;
  city: string | null;
  country: string | null;
  type: TenantType;
  status: TenantStatus;
  currency: string;
  timeZone: string;
  defaultLanguage: string;
  isActive: boolean;
  subscriptionStartDate: string | null;
  subscriptionExpiryDate: string | null;
  isSubscriptionExpired: boolean;
  createdAt: string;
  adminEmail: string | null;
}

export interface CreateTenantRequest {
  slug: string;
  name: string;
  nameAr: string | null;
  type: TenantType;
  email: string | null;
  phone: string | null;
  address: string | null;
  addressAr: string | null;
  city: string | null;
  country: string | null;
  currency: string;
  timeZone: string;
  defaultLanguage: string;
  description: string | null;
  descriptionAr: string | null;
  adminFullName: string;
  adminEmail: string;
  adminPassword: string;
}

export interface UpdateTenantRequest {
  name: string | null;
  nameAr: string | null;
  description: string | null;
  descriptionAr: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  addressAr: string | null;
  city: string | null;
  country: string | null;
  currency: string | null;
  timeZone: string | null;
  defaultLanguage: string | null;
  status: TenantStatus | null;
  isActive: boolean | null;
  subscriptionStartDate: string | null;
  subscriptionExpiryDate: string | null;
}

export interface BranchDto {
  id: string;
  tenantId: string;
  name: string;
  nameAr: string | null;
  code: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  addressAr: string | null;
  city: string | null;
  status: BranchStatus;
  isMainBranch: boolean;
  createdAt: string;
}

export interface CreateBranchRequest {
  name: string;
  nameAr: string | null;
  code: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  addressAr: string | null;
  city: string | null;
  isMainBranch: boolean;
}

export interface UpdateBranchRequest {
  name: string | null;
  nameAr: string | null;
  code: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  addressAr: string | null;
  city: string | null;
  status: BranchStatus | null;
  isMainBranch: boolean | null;
}

export interface TenantSettingsDto {
  tenantId: string;
  tenantName: string;
  notifyParentOnAbsence: boolean;
  notifyParentOnSessionCancellation: boolean;
  lateThresholdMinutes: number;
  requireExcuseReason: boolean;
  autoGenerateMonthlyFees: boolean;
  feeDueDayOfMonth: number;
  gracePeriodDays: number;
  siblingDiscountPercentage: number;
  defaultPassingPercentage: number;
  useCustomGradingScale: boolean;
  notifyParentOnFeeDue: boolean;
  notifyParentOnPaymentReceived: boolean;
  notifyParentOnHomeworkAssigned: boolean;
  notifyParentOnExamResultPublished: boolean;
  notifyParentOnMakeupClassScheduled: boolean;
  dateFormat: string;
  currencySymbol: string;
}

export interface UpdateTenantSettingsRequest {
  notifyParentOnAbsence: boolean | null;
  notifyParentOnSessionCancellation: boolean | null;
  lateThresholdMinutes: number | null;
  requireExcuseReason: boolean | null;
  autoGenerateMonthlyFees: boolean | null;
  feeDueDayOfMonth: number | null;
  gracePeriodDays: number | null;
  siblingDiscountPercentage: number | null;
  defaultPassingPercentage: number | null;
  useCustomGradingScale: boolean | null;
  notifyParentOnFeeDue: boolean | null;
  notifyParentOnPaymentReceived: boolean | null;
  notifyParentOnHomeworkAssigned: boolean | null;
  notifyParentOnExamResultPublished: boolean | null;
  notifyParentOnMakeupClassScheduled: boolean | null;
  dateFormat: string | null;
  currencySymbol: string | null;
}

export interface AcademicYearDto {
  id: string;
  name: string;
  nameAr: string | null;
  startDate: string;
  endDate: string;
  status: AcademicYearStatus;
  isCurrent: boolean;
  isArchived: boolean;
  createdAt: string;
}

export interface CreateAcademicYearRequest {
  name: string;
  nameAr: string | null;
  startDate: string;
  endDate: string;
  isCurrent: boolean;
}

export interface UpdateAcademicYearRequest {
  name: string | null;
  nameAr: string | null;
  startDate: string | null;
  endDate: string | null;
  status: AcademicYearStatus | null;
}

export interface GradeLevelDto {
  id: string;
  code: string;
  name: string;
  nameAr: string | null;
  order: number;
  isActive: boolean;
  description: string | null;
  descriptionAr: string | null;
  createdAt: string;
}

export interface CreateGradeLevelRequest {
  code: string;
  name: string;
  nameAr: string | null;
  order: number;
  description: string | null;
  descriptionAr: string | null;
}

export interface UpdateGradeLevelRequest {
  name: string | null;
  nameAr: string | null;
  order: number | null;
  isActive: boolean | null;
  description: string | null;
  descriptionAr: string | null;
}

export interface SubjectDto {
  id: string;
  code: string;
  name: string;
  nameAr: string;
  description: string | null;
  descriptionAr: string | null;
  isActive: boolean;
  sortOrder: number;
  createdAt: string;
}

export interface CreateSubjectRequest {
  code: string;
  name: string;
  nameAr: string | null;
  description: string | null;
  descriptionAr: string | null;
  sortOrder: number;
}

export interface UpdateSubjectRequest {
  name: string | null;
  nameAr: string | null;
  description: string | null;
  descriptionAr: string | null;
  isActive: boolean | null;
  sortOrder: number | null;
}

export interface AuditLogDto {
  id: string;
  userId: string | null;
  userName: string | null;
  action: AuditAction;
  entityName: string;
  entityId: string | null;
  correlationId: string | null;
  ipAddress: string | null;
  oldValue: string | null;
  newValue: string | null;
  description: string | null;
  occurredAt: string;
}

export interface AuditSummaryDto {
  action: AuditAction;
  userId: string | null;
  userName: string | null;
  count: number;
  lastOccurredAt: string;
}

export interface LoginHistoryDto {
  id: string;
  userId: string;
  userName: string | null;
  succeeded: boolean;
  failureReason: string | null;
  ipAddress: string | null;
  userAgent: string | null;
  attemptedAt: string;
}

export type CompensationRateBasis = 'PerSession' | 'PerHour';

export interface TeacherDto {
  id: string;
  userId: string;
  employeeNumber: string | null;
  fullName: string;
  email: string;
  phoneNumber: string | null;
  hireDate: string | null;
  isActive: boolean;
  notes: string | null;
  subjects: SubjectDto[];
  createdAt: string;
}

export interface CreateTeacherRequest {
  employeeNumber: string | null;
  email: string | null;
  password: string | null;
  fullName: string | null;
  phoneNumber: string | null;
  userId: string | null;
  hireDate: string | null;
  subjectIds: string[];
}

export interface UpdateTeacherRequest {
  employeeNumber: string | null;
  phoneNumber: string | null;
  hireDate: string | null;
  isActive: boolean | null;
  notes: string | null;
}

export interface CompensationRuleDto {
  id: string;
  teacherProfileId: string | null;
  subjectId: string | null;
  studyGroupId: string | null;
  rate: number;
  rateBasis: CompensationRateBasis;
  isActive: boolean;
  createdAt: string;
}

export interface CreateCompensationRuleRequest {
  teacherProfileId: string | null;
  subjectId: string | null;
  studyGroupId: string | null;
  rate: number;
  rateBasis: CompensationRateBasis;
}

export interface UpdateCompensationRuleRequest {
  rate: number | null;
  rateBasis: CompensationRateBasis | null;
  isActive: boolean | null;
}

// ---- Students & parents (Mission Phase 2.5) --------------------------------

export type StudentStatus = 'Active' | 'Inactive' | 'Graduated' | 'Withdrawn';
export type GuardianRelationship = 'Father' | 'Mother' | 'Guardian' | 'Other';

export const STUDENT_STATUSES: StudentStatus[] = ['Active', 'Inactive', 'Graduated', 'Withdrawn'];
export const GUARDIAN_RELATIONSHIPS: GuardianRelationship[] = ['Father', 'Mother', 'Guardian', 'Other'];

export interface GuardianLinkDto {
  parentProfileId: string;
  parentFullName: string;
  phoneNumber?: string | null;
  relationship: GuardianRelationship;
  isPrimary: boolean;
}

export interface StudentDto {
  id: string;
  userId?: string | null;
  accountEmail?: string | null;
  fullName: string;
  studentNumber?: string | null;
  dateOfBirth?: string | null;
  gradeLevelId?: string | null;
  gradeLevelName?: string | null;
  branchId?: string | null;
  branchName?: string | null;
  status: StudentStatus;
  enrollmentDate?: string | null;
  notes?: string | null;
  guardians: GuardianLinkDto[];
  createdAt: string;
  /** Only on the create response, when a guardian was registered with the student. */
  newGuardian?: NewGuardianAccountDto | null;
}

/** The guardian registered with a new student; the temporary password is shown once. */
export interface NewGuardianAccountDto {
  parentProfileId: string;
  fullName: string;
  email?: string | null;
  phoneNumber?: string | null;
  /** What the parent types to sign in: the e-mail, or the mobile number when there is none. */
  loginName?: string | null;
  linkedExistingParent: boolean;
  accountCreated: boolean;
  temporaryPassword?: string | null;
  whatsAppSent?: boolean;
}

export interface CreateStudentGuardianRequest {
  fullName: string;
  phoneNumber: string | null;
  email: string | null;
  relationship: GuardianRelationship;
  createAccount: boolean;
}

export interface ChildLinkDto {
  studentProfileId: string;
  studentFullName: string;
  studentNumber?: string | null;
  gradeLevelName?: string | null;
  status: StudentStatus;
  relationship: GuardianRelationship;
  isPrimary: boolean;
}

export interface ParentDto {
  id: string;
  userId?: string | null;
  accountEmail?: string | null;
  fullName: string;
  phoneNumber?: string | null;
  email?: string | null;
  notes?: string | null;
  children: ChildLinkDto[];
  createdAt: string;
}

export interface CreateStudentRequest {
  fullName: string;
  studentNumber: string | null;
  dateOfBirth: string | null;
  gradeLevelId: string | null;
  branchId: string | null;
  enrollmentDate: string | null;
  notes: string | null;
  userId: string | null;
  accountEmail: string | null;
  accountPassword: string | null;
  guardian?: CreateStudentGuardianRequest | null;
}

export interface UpdateStudentRequest {
  fullName: string | null;
  studentNumber: string | null;
  dateOfBirth: string | null;
  gradeLevelId: string | null;
  branchId: string | null;
  status: StudentStatus | null;
  enrollmentDate: string | null;
  notes: string | null;
}

export interface GuardianLinkRequest {
  parentProfileId: string;
  relationship: GuardianRelationship;
  isPrimary: boolean;
}

export interface CreateParentRequest {
  fullName: string;
  phoneNumber: string | null;
  email: string | null;
  notes: string | null;
  userId: string | null;
  accountPassword: string | null;
}

export interface UpdateParentRequest {
  fullName: string | null;
  phoneNumber: string | null;
  email: string | null;
  notes: string | null;
}

// ---- Files (Mission Phase 2.5b) ------------------------------------------------

export interface StoredFileDto {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeInBytes: number;
  category: string;
  ownerEntityId?: string | null;
  ownerEntityName?: string | null;
  description?: string | null;
  isImage: boolean;
  createdAt: string;
}

// ---- Scheduling (Mission Phase 3) ---------------------------------------------

export type DayOfWeekName =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday';
export const DAYS_OF_WEEK: DayOfWeekName[] = [
  'Saturday',
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday'
];
export type EnrollmentStatus = 'Active' | 'Completed' | 'Withdrawn';
export type SessionStatus = 'Scheduled' | 'Completed' | 'Cancelled';
export type AttendanceStatus = 'Present' | 'Absent' | 'Late' | 'Excused';
export const ATTENDANCE_STATUSES: AttendanceStatus[] = ['Present', 'Absent', 'Late', 'Excused'];

export interface ScheduleSlotDto {
  id: string;
  studyGroupId: string;
  dayOfWeek: DayOfWeekName;
  startTime: string;
  endTime: string;
  room?: string | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
}

export interface StudyGroupDto {
  id: string;
  name: string;
  code?: string | null;
  subjectId: string;
  subjectName?: string | null;
  gradeLevelId?: string | null;
  gradeLevelName?: string | null;
  teacherProfileId?: string | null;
  teacherName?: string | null;
  academicYearId?: string | null;
  branchId?: string | null;
  capacity?: number | null;
  isActive: boolean;
  notes?: string | null;
  activeEnrollments: number;
  slots: ScheduleSlotDto[];
  createdAt: string;
}

export interface StudyGroupRequest {
  name: string;
  code?: string | null;
  subjectId: string;
  gradeLevelId?: string | null;
  teacherProfileId?: string | null;
  capacity?: number | null;
  isActive?: boolean;
  notes?: string | null;
}

export interface EnrollmentDto {
  id: string;
  studyGroupId: string;
  groupName?: string | null;
  studentProfileId: string;
  studentName?: string | null;
  studentNumber?: string | null;
  enrolledOn: string;
  endedOn?: string | null;
  status: EnrollmentStatus;
}

export interface ScheduleSlotRequest {
  dayOfWeek: DayOfWeekName;
  startTime: string;
  endTime: string;
  room?: string | null;
}

export interface ClassSessionDto {
  id: string;
  studyGroupId: string;
  groupName?: string | null;
  scheduleSlotId?: string | null;
  date: string;
  startTime: string;
  endTime: string;
  teacherProfileId?: string | null;
  teacherName?: string | null;
  status: SessionStatus;
  topic?: string | null;
  notes?: string | null;
  markedCount: number;
  presentCount: number;
}

export interface AttendanceRowDto {
  studentProfileId: string;
  studentName: string;
  studentNumber?: string | null;
  status?: AttendanceStatus | null;
  notes?: string | null;
}

export interface GenerateSessionsResultDto {
  created: number;
  skipped: number;
}

// ---- Fees & payments (Mission Phase 4) ----------------------------------------

export type FeeFrequency = 'OneTime' | 'Monthly';
export type ChargeStatus = 'Open' | 'PartiallyPaid' | 'Paid' | 'Cancelled';
export type PaymentMethod = 'Cash' | 'Card' | 'BankTransfer' | 'Other';
export const PAYMENT_METHODS: PaymentMethod[] = ['Cash', 'Card', 'BankTransfer', 'Other'];

export interface FeeRuleDto {
  id: string;
  name: string;
  amount: number;
  frequency: FeeFrequency;
  studyGroupId?: string | null;
  studyGroupName?: string | null;
  gradeLevelId?: string | null;
  gradeLevelName?: string | null;
  isActive: boolean;
  notes?: string | null;
}

export interface ChargeDto {
  id: string;
  studentProfileId: string;
  studentName?: string | null;
  feeRuleId?: string | null;
  studyGroupId?: string | null;
  studyGroupName?: string | null;
  description: string;
  amount: number;
  amountPaid: number;
  outstanding: number;
  dueDate: string;
  periodKey?: string | null;
  status: ChargeStatus;
  createdAt: string;
}

export interface GenerateChargesResultDto {
  created: number;
  skipped: number;
  withoutRule: number;
}

export interface AllocationDto {
  chargeId: string;
  chargeDescription?: string | null;
  amount: number;
}

export interface PaymentDto {
  id: string;
  studentProfileId: string;
  studentName?: string | null;
  cashAccountId?: string | null;
  amount: number;
  allocated: number;
  unallocated: number;
  paidOn: string;
  method: PaymentMethod;
  reference?: string | null;
  notes?: string | null;
  isVoided: boolean;
  voidReason?: string | null;
  allocations: AllocationDto[];
  createdAt: string;
}

export interface CreatePaymentRequest {
  studentProfileId: string;
  amount: number;
  paidOn?: string | null;
  method: PaymentMethod;
  cashAccountId?: string | null;
  reference?: string | null;
  notes?: string | null;
  allocations?: { chargeId: string; amount: number }[] | null;
}

export interface StudentBalanceDto {
  studentProfileId: string;
  totalCharged: number;
  totalPaid: number;
  outstanding: number;
  credit: number;
  openCharges: number;
}

// ---- Cash, expenses, settlements, reports (Mission Phase 5) -------------------

export type CashAccountType = 'Cash' | 'Bank';
export type CashDirection = 'In' | 'Out';
export type CashSourceType = 'Manual' | 'Payment' | 'PaymentVoid' | 'Expense' | 'TeacherPayment';

export interface CashAccountDto {
  id: string;
  name: string;
  type: CashAccountType;
  openingBalance: number;
  balance: number;
  isActive: boolean;
  isDefault: boolean;
}

export interface CashTransactionDto {
  id: string;
  cashAccountId: string;
  cashAccountName?: string | null;
  direction: CashDirection;
  amount: number;
  occurredOn: string;
  sourceType: CashSourceType;
  sourceId?: string | null;
  description?: string | null;
}

export interface ExpenseCategoryDto {
  id: string;
  name: string;
  isActive: boolean;
}

export interface ExpenseDto {
  id: string;
  expenseCategoryId: string;
  categoryName?: string | null;
  cashAccountId?: string | null;
  amount: number;
  spentOn: string;
  description: string;
  reference?: string | null;
}

export interface ComputeCompensationsResultDto {
  created: number;
  alreadyPriced: number;
  withoutRule: number;
}

export interface SessionCompensationDto {
  id: string;
  classSessionId: string;
  sessionDate: string;
  groupName?: string | null;
  teacherProfileId: string;
  amount: number;
  compensationRuleId?: string | null;
  teacherPaymentId?: string | null;
}

export interface TeacherSettlementDto {
  teacherProfileId: string;
  teacherName?: string | null;
  unsettled: number;
  unsettledSessions: number;
  paidInRange: number;
  items: SessionCompensationDto[];
}

export interface TeacherPaymentDto {
  id: string;
  teacherProfileId: string;
  teacherName?: string | null;
  amount: number;
  paidOn: string;
  periodFrom?: string | null;
  periodTo?: string | null;
  cashAccountId?: string | null;
  notes?: string | null;
  settledSessions: number;
}

export interface CategoryTotalDto {
  categoryId: string;
  name: string;
  total: number;
}

export interface FinanceSummaryDto {
  from: string;
  to: string;
  income: number;
  expenses: number;
  teacherPayments: number;
  net: number;
  outstandingFees: number;
  chargesIssued: number;
  expensesByCategory: CategoryTotalDto[];
  cashAccounts: CashAccountDto[];
}

export interface DashboardKpisDto {
  activeStudents: number;
  activeGroups: number;
  activeTeachers: number;
  sessionsToday: number;
  attendanceRate30Days?: number | null;
  outstandingFees?: number | null;
  incomeThisMonth?: number | null;
  expensesThisMonth?: number | null;
}

// ---- Homework, exams, notifications (Mission Phase 6) -------------------------

export type SubmissionStatus = 'Pending' | 'Submitted' | 'Graded' | 'Missing';
export const SUBMISSION_STATUSES: SubmissionStatus[] = ['Pending', 'Submitted', 'Graded', 'Missing'];

export interface HomeworkDto {
  id: string;
  studyGroupId: string;
  groupName?: string | null;
  title: string;
  description?: string | null;
  assignedOn: string;
  dueDate: string;
  maxGrade?: number | null;
  submitted: number;
  graded: number;
  rosterSize: number;
}

export interface SubmissionRowDto {
  studentProfileId: string;
  studentName: string;
  status: SubmissionStatus;
  submittedAt?: string | null;
  grade?: number | null;
  feedback?: string | null;
  /** Answers the student (or parent) uploaded. */
  files?: SubmissionFileDto[];
}

export interface SubmissionFileDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeInBytes: number;
  note?: string | null;
  uploadedAt: string;
}

export interface ExamDto {
  id: string;
  studyGroupId: string;
  groupName?: string | null;
  title: string;
  examDate: string;
  maxScore: number;
  notes?: string | null;
  resultsRecorded: number;
  averageScore?: number | null;
}

export interface ExamResultRowDto {
  studentProfileId: string;
  studentName: string;
  score?: number | null;
  isAbsent: boolean;
  notes?: string | null;
  recorded: boolean;
}

export interface AppNotificationDto {
  id: string;
  title: string;
  body?: string | null;
  link?: string | null;
  isRead: boolean;
  createdAt: string;
}

export interface NotificationFeedDto {
  unreadCount: number;
  items: AppNotificationDto[];
}

// ---- Family view: a student or parent following their own (children's) learning ----------

export interface FamilyAttendanceSummaryDto {
  present: number;
  absent: number;
  late: number;
  excused: number;
  attendanceRate?: number | null;
}

export interface FamilySessionDto {
  session: ClassSessionDto;
  attendance?: AttendanceStatus | null;
}

export interface FamilyHomeworkDto {
  id: string;
  studyGroupId: string;
  groupName?: string | null;
  title: string;
  description?: string | null;
  assignedOn: string;
  dueDate: string;
  maxGrade?: number | null;
  status: SubmissionStatus;
  submittedAt?: string | null;
  grade?: number | null;
  feedback?: string | null;
  files: SubmissionFileDto[];
  /** True while the answer can still be uploaded (enrolled and not graded yet). */
  canSubmit: boolean;
}

export interface FamilyExamDto {
  id: string;
  studyGroupId: string;
  groupName?: string | null;
  title: string;
  examDate: string;
  maxScore: number;
  recorded: boolean;
  score?: number | null;
  isAbsent: boolean;
  notes?: string | null;
}

export interface FamilyStudentOverviewDto {
  studentProfileId: string;
  fullName: string;
  studentNumber?: string | null;
  groups: StudyGroupDto[];
  attendance: FamilyAttendanceSummaryDto;
  recentSessions: FamilySessionDto[];
  upcomingSessions: FamilySessionDto[];
  homework: FamilyHomeworkDto[];
  exams: FamilyExamDto[];
  balance?: StudentBalanceDto | null;
  /** The centre's currency (ISO code, e.g. EGP). */
  currency?: string | null;
}

// ---- Pre-registration requests (public form + staff review) ----------------

export type RegistrationRequestStatus = 'Pending' | 'Approved' | 'Rejected';
export const REGISTRATION_STATUSES: RegistrationRequestStatus[] = ['Pending', 'Approved', 'Rejected'];

export interface PublicOptionDto {
  id: string;
  name: string;
  nameAr?: string | null;
}

export interface PublicRegistrationFormDto {
  tenantSlug: string;
  centreName: string;
  centreNameAr?: string | null;
  gradeLevels: PublicOptionDto[];
  subjects: PublicOptionDto[];
}

export interface SubmitRegistrationRequest {
  guardianName: string;
  guardianPhone: string;
  guardianEmail?: string | null;
  relationship: GuardianRelationship;
  studentName: string;
  birthDate?: string | null;
  gradeLevelId?: string | null;
  school?: string | null;
  subjectIds?: string[];
  notes?: string | null;
  /** Honeypot - always empty for a person. */
  website?: string | null;
}

export interface RegistrationRequestDto {
  id: string;
  guardianName: string;
  guardianPhone: string;
  guardianEmail?: string | null;
  relationship: GuardianRelationship;
  studentName: string;
  birthDate?: string | null;
  gradeLevelId?: string | null;
  gradeLevelName?: string | null;
  school?: string | null;
  preferredSubjects?: string | null;
  notes?: string | null;
  status: RegistrationRequestStatus;
  createdAt: string;
  reviewedByUserId?: string | null;
  reviewedAt?: string | null;
  rejectionReason?: string | null;
  createdStudentProfileId?: string | null;
}

export interface ApproveRegistrationRequest {
  studyGroupId?: string | null;
  studentName?: string | null;
  birthDate?: string | null;
  gradeLevelId?: string | null;
  guardianName?: string | null;
  guardianPhone?: string | null;
  guardianEmail?: string | null;
  relationship?: GuardianRelationship | null;
  createAccount: boolean;
  notes?: string | null;
}

export interface ApproveRegistrationResultDto {
  request: RegistrationRequestDto;
  student: StudentDto;
  enrolled: boolean;
}
