# Row-level access scope (who sees which rows)

Endpoint permissions (`[Authorize(Policy = "Groups.View")]`) decide whether a caller may open a
screen at all. `DataScopeService` (`src/EducationHub.Application/Common/Access/DataScope.cs`)
decides **which rows** they get. It is resolved once per request from the caller's roles and
their linked profiles.

| Caller's roles | Scope | Sees | May change |
|---|---|---|---|
| PlatformAdmin, TenantOwner, TenantAdmin, Accountant | Unrestricted | whatever their permissions allow | whatever their permissions allow |
| Teacher | Teacher | groups whose `TeacherProfileId` is theirs, students ever enrolled in those groups, sessions of those groups plus sessions they taught (substitute), homework/exams of those groups, their own compensation and the centre-wide default rules | sessions, attendance, homework, exams of their own groups only |
| Student / Parent | Family | the student themself / the parent's linked children, the groups those students are (or were) enrolled in, and only their own attendance marks, homework submissions and exam results | nothing (no write permission in the role) |
| Assistant, custom roles | Unrestricted | whatever their permissions allow | whatever their permissions allow |

Rules that follow from it:

- A teacher is scoped by **group assignment**, not by subject: two maths teachers never see each
  other's groups. Assign the teacher on the group (`PUT /api/groups/{id}` with `teacherProfileId`).
- Rows outside the scope answer **404**, exactly like another tenant's ids, so ids cannot be probed.
- A teacher's student list is the students of their groups; an unassigned teacher sees none.
- Assistants have no link to a teacher yet, so they keep tenant-wide read access (their role has
  no write permission on teaching data except attendance).

## Role permission changes (2026-11-18)

- `Teacher`, `Assistant`: removed `Users.View` and `Settings.View` (user directory and centre settings
  are management data).
- `Student`, `Parent`: added `Groups.View`, `Sessions.View`, `Attendance.View`, `Homework.View`,
  `Exams.View` - read-only and narrowed to their own family by the scope above.

`RoleSeeder` reconciles the built-in roles on every startup, so these apply to existing tenants after
the API restarts. Users must sign out and back in to receive a token with the new permissions.

## Family endpoint

`GET /api/my/family` - for the signed-in student or parent, one entry per student (themself / each
linked child) with active groups, attendance summary, the last 15 and next 10 sessions with the
student's own mark, the latest 30 homework items with the student's own submission and grade, the
latest 30 exams with the student's own result, and the fee balance. It takes no id and needs no
extra permission: the data is resolved from the caller's own account, and staff get an empty list.

## Family page and dashboard (2026-11-18)

- The admin app has a **My family** page (`/my-family`, nav item for the Parent and Student roles)
  built on `GET /api/my/family`: a child picker for parents with more than one child, summary
  cards (attendance rate, next session, homework not submitted, fees due) and tabs for groups,
  sessions with the child's attendance, homework with the child's own status/grade/feedback, and
  exams with the child's own result.
- For accounts whose only roles are Parent/Student the staff teaching screens (groups, sessions,
  homework, exams) are hidden from the menu, and the dashboard shows one summary card per child
  instead of the centre's figures.
- `GET /api/dashboard/kpis` is scoped the same way as the lists (a teacher counts their own groups,
  a family its own children) and answers 400 `Dashboard.TenantContextRequired` for the platform
  administrator, who has no tenant to count.
- Homework and exam-result notifications now link to `/my-family`.

## Registering the guardian with the student

`POST /api/students` accepts an optional `guardian` block
(`fullName`, `phoneNumber`, `email`, `relationship`, `createAccount`). In the same transaction:

1. a parent of the tenant with the same e-mail is reused, otherwise a parent profile is created;
2. with `createAccount` (e-mail required) and no existing account, a Parent account is created with
   a generated 10-character temporary password and `MustChangePassword = true`;
3. the parent is linked to the student as primary guardian.

The response carries `newGuardian` (`parentProfileId`, `linkedExistingParent`, `accountCreated`,
`temporaryPassword`); the password is returned only in that response and is shown once in the
admin app so the staff member can hand it over. The caller needs `Parents.Create` in addition to
`Students.Create`. Any failure rolls the whole registration back.

## Fixes from the role audit (2026-11-18)

- **Platform administrator screens.** `GET /api/roles` under the platform scope lists the
  platform roles (`TenantId = Guid.Empty`), the only roles a platform user can hold, and the
  admin app shows them read-only. `GET /api/permissions` no longer needs a tenant: the platform
  administrator gets the platform areas, a tenant gets every area except the platform-only ones.
- **System roles keep tenant edits.** `RoleSeeder` now calls
  `TenantRoleProvisioner.EnsureRoleKeepingCustomizationsAsync`: a role already reconciled to
  `TenantRoleProvisioner.DefaultsVersion` (stored as a `SystemPermissionsVersion` role claim, never
  copied into tokens) is left as the tenant configured it. An older stamp is upgraded
  **additively** - see [Built-in role defaults are additive](#built-in-role-defaults-are-additive).
- **Audit trail per tenant.** Rows written before a request is authenticated (sign-in history,
  refresh tokens, last-login stamps) and rows changed by the platform administrator are recorded
  under the tenant that owns the changed row, with the row's own user as actor when there is no
  caller. A centre's audit page therefore shows its own users' sign-ins.
- **No platform permissions in tenant roles.** `PermissionConstants.PlatformOnlyAreaNames`
  (`Platform`, `Tenants`) are excluded from `TenantOwner` (`GetTenantPermissions()`); `TenantAdmin`
  lost the unused `Tenants.View`.
- **Files follow the teacher scope.** A teacher lists, downloads and deletes only files of the
  students in their own groups, of their own teacher profile, and files they uploaded; uploads can
  be attached only to those students or to their own profile (others answer 404).

## Homework answers uploaded by students (2026-11-18)

`POST /api/my/homework/{homeworkId}/submission` (multipart: `File`, optional `Note`, and
`StudentProfileId` when a parent with several children submits) stores the file as a `StoredFile`
owned by the submission row (`OwnerEntityName = HomeworkSubmission`) and marks the submission
`Submitted`. Only the student themself or their parent can call it, only while the student is
actively enrolled in the homework's group, and never once the work is graded. No schema change.

- Family: `GET /api/my/files/{id}/content` downloads, `DELETE /api/my/homework/{id}/files/{fileId}`
  removes an answer before grading (the last removal puts the submission back to `Pending`).
- Staff: `GET /api/homework/{id}/submissions` now lists each student's `files`; a teacher can open
  answers of their own groups through `GET /api/files/{id}/content`.

## WhatsApp (2026-11-18)

`IWhatsAppSender` is chosen by `WhatsApp:Provider`: `None` (default), `Log` (development, writes the
message to the log) or `Meta` (WhatsApp Cloud API - set `WhatsApp:Meta:PhoneNumberId` and
`WhatsApp:Meta:AccessToken`, the token through user secrets or an environment variable). Business-
initiated WhatsApp messages need templates approved in the Meta console: set
`CredentialsTemplate` (body {{1}} name, {{2}} sign-in name, {{3}} temporary password, {{4}} link) and
`NotificationTemplate` ({{1}} title, {{2}} details); without them the text is sent as a free message,
which Meta accepts only inside a 24-hour conversation or for test numbers.

Sent (best-effort, after the change is committed): a new parent account's sign-in details when the
guardian has a mobile number (`SendGuardianCredentials`), and new homework / exam results to every
parent with a mobile number (`NotifyParents`).

## Password reset by mobile number (2026-11-18)

For phone-only parents (no real e-mail). Both endpoints are anonymous.

- `POST /api/auth/forgot-password/phone` `{ phone }` - the number is canonicalised and looked up
  exactly like sign-in (`GetByPhoneAsync`: the account's own number or its parent profile's, and
  only when exactly one active account matches). A 6-digit code is generated with Identity's phone
  token provider (`TokenOptions.DefaultPhoneProvider`, purpose `ResetPasswordByPhone`; registered by
  `AddDefaultTokenProviders()`, no table) and sent by WhatsApp (`WhatsAppMessageKind.PasswordReset`).
  The answer is always the same `200` (`Auth.PhoneResetCodeSent`), registered or not.
- `POST /api/auth/reset-password/phone` `{ phone, code, newPassword, confirmPassword }` - verifies the
  code (valid for a few minutes, bound to the security stamp), sets the new password, clears
  `MustChangePassword` and the lockout, revokes every refresh token (same unit of work as the e-mail
  reset) and rotates the security stamp, so the code works once. An unknown number and a wrong code
  both answer `400 Auth.InvalidResetCode`.
- Rate limits (`Auth:RateLimit:PhoneReset`): per remote IP `PermitLimit` (default 10) per `Window`
  (15 min) through the `auth-phone-reset` limiter policy, and per mobile number
  `PerPhonePermitLimit` (default 3) per window and per endpoint (`PhoneResetRateLimiter`, an action
  filter, because the number is in the body). Over the limit: `429` with `Retry-After`.
- Admin app: the forgot-password page has an Email / Mobile toggle; the mobile path asks for the code
  and the new password on the same page.

## Pre-registration requests (2026-11-18)

A public page per centre where a parent asks to register a child; staff approve or reject.

**Public endpoints** (anonymous; the centre is the tenant's existing unique `Slug`, never a token):

| Endpoint | Does |
|---|---|
| `GET /api/public/registration/{tenantSlug}` | Centre name (`centreName`, `centreNameAr`), active grade levels and subjects. `404 PublicRegistration.NotFound` when the slug is unknown, deleted, inactive or expired. |
| `POST /api/public/registration/{tenantSlug}` | Creates a `Pending` `RegistrationRequest` (guardian name, mobile - stored canonical -, optional e-mail, relationship, student name, birth date, grade, school, subject ids, notes). Rate-limited per IP (`public-registration` policy, `Auth:RateLimit:PublicRegistration`, default 5 per hour). The hidden `website` field is a honeypot: when filled, the answer is the normal `200` and nothing is stored. A second pending request for the same mobile + student name is `409 RegistrationRequests.AlreadyExists` (also enforced by the filtered unique index `IX_RegistrationRequests_Pending_Unique`). Every active account of the centre holding `RegistrationRequests.View` gets an in-app notification linking to `/registration-requests`. |

The anonymous reads bypass the query filters and apply the tenant from the slug explicitly; they
all live in `PublicRegistrationReader`. The admin app's HTTP interceptor never sends a token to
`/api/public/`, so a staff session open in the same browser does not ride along.

**Permissions**: `RegistrationRequests.View`, `RegistrationRequests.Manage` (new area
`RegistrationRequests`). Default holders: `TenantOwner` (all tenant permissions), `TenantAdmin`,
`Assistant`. `TenantRoleProvisioner.DefaultsVersion` is now `2026-11-18.2`; its
`AddedInVersion` entry grants the two permissions to those roles in existing tenants on the next
start-up, without touching anything else (see the next section).

## Built-in role defaults are additive

Each tenant's seven system roles carry a `SystemPermissionsVersion` role claim (never copied into
tokens). On every start-up `RoleSeeder` calls
`TenantRoleProvisioner.EnsureRoleKeepingCustomizationsAsync` for each of them:

| Role state | What happens |
|---|---|
| Missing (new tenant, or a role that was never created) | Created with the **full** defaults from `BuildDefaultRolePermissions`, stamped `DefaultsVersion`. Tenant creation (`CreateTenantCommandHandler`) does the same. |
| Stamped `DefaultsVersion` | Nothing. |
| Stamped an older known version | Only the permissions listed in the `AddedInVersion` entries **newer than the stamp** are granted (those the role already holds are skipped), then the stamp is set to `DefaultsVersion`. |
| No stamp (created before versioning, when every start-up reset roles) | The defaults it is missing are added; nothing is removed; stamped. |
| Stamp this build does not know (written by a newer build) | Left alone, with a warning in the log. |

The upgrade **never removes** a permission, so a tenant's extra grants survive, and it never
re-grants a permission introduced by an older version: if a centre removed `Attendance.Mark` from
its Assistant, a later bump does not give it back.

**Adding the next version's permissions** (e.g. a new `Foo.View` for `TenantAdmin`):

1. Add the constant to `PermissionConstants` (and its area/catalogue, as for any permission).
2. Add it to the role(s) in `BuildDefaultRolePermissions` - new tenants get it from there.
   `TenantOwner` gets every tenant permission automatically.
3. Append an entry to `TenantRoleProvisioner.AddedInVersion`, e.g.
   `("2026-12-01", { [RoleNames.TenantOwner] = [Foo.View], [RoleNames.TenantAdmin] = [Foo.View] })`.
   List `TenantOwner` explicitly too. Never edit or reorder an existing entry.
4. Set `DefaultsVersion` to that new version string. Versions are matched by exact string and
   ordered by their position in the list, not by comparing the strings.
5. Users pick the new permission up at their next sign-in or token refresh.

Removing a permission from a built-in role in existing tenants is deliberately not automatic; do
it with a one-off data fix or let each centre change its roles. The test
`SeedingTests.Every_version_entry_only_adds_permissions_the_role_has_by_default` checks that
every entry only grants what the role has by default and that `DefaultsVersion` is the newest
entry.

**Staff endpoints**:

| Endpoint | Permission | Does |
|---|---|---|
| `GET /api/registration-requests?status=&search=&pageNumber=&pageSize=` | View | Newest first; search matches student / guardian name or the mobile in any shape. |
| `GET /api/registration-requests/link` | View | The centre's slug; the admin app shows `{origin}/register/{slug}` with a copy button. |
| `POST /api/registration-requests/{id}/approve` | Manage | Body (all optional): `studyGroupId`, overrides `studentName`, `birthDate`, `gradeLevelId`, `guardianName`, `guardianPhone`, `guardianEmail`, `relationship`, `createAccount` (default true), `notes`. One transaction: the student is registered by `StudentRegistrationService` - the same code `POST /api/students` runs - with the guardian (a parent with the same e-mail, or the same mobile when there is no e-mail, is linked instead of duplicated; otherwise a parent and, with `createAccount`, a phone-only Parent account with a temporary password are created), then enrolled in the group (same rules as `POST /api/groups/{id}/enrollments`), then the request becomes `Approved` with `createdStudentProfileId`. After the commit the parent's sign-in details go out by WhatsApp. The response carries `student.newGuardian` exactly like the students page. |
| `POST /api/registration-requests/{id}/reject` | Manage | `{ reason }` (required) - `Rejected`. |

Approving or rejecting a request that is not `Pending` is `409 RegistrationRequests.ReviewConflict`;
two simultaneous approvals are stopped by the row version (the loser gets `409` and its student is
rolled back). Requests are centre-management data: a restricted data scope (teacher, family) sees
none of them, and another centre's id answers `404`.

## WhatsApp message kinds and Meta templates

Each kind needs its own approved template in the Meta console before production use (`WhatsApp:Meta:*`).
Without a template the text is sent as a free-form message, which Meta accepts only inside a
24-hour conversation window or for test numbers.

| Kind | Setting | Category | Body parameters | Sent when |
|---|---|---|---|---|
| `Credentials` | `CredentialsTemplate` | Utility | {{1}} name, {{2}} sign-in name, {{3}} temporary password, {{4}} sign-in link | a parent account is created (`SendGuardianCredentials`) |
| `Notification` | `NotificationTemplate` | Utility | {{1}} title, {{2}} details | homework / exam results (`NotifyParents`) |
| `PasswordReset` | `PasswordResetTemplate` | **Authentication** (copy-code button) | {{1}} the 6-digit code; the copy-code URL button is sent the same code | `POST /api/auth/forgot-password/phone` |
| `RegistrationReceived` | `RegistrationReceivedTemplate` | Utility | {{1}} guardian name, {{2}} student name, {{3}} centre name | a pre-registration request is stored, only with `WhatsApp:SendRegistrationConfirmation = true` (default false; true in Development) |

The password-reset message is business-initiated and outside any conversation window, so in
production it is only delivered once `PasswordResetTemplate` is approved.

## Audit trail records deletes (2026-11-19)

`AuditSaveChangesInterceptor` writes an `AuditLog` row for every added, modified **and deleted**
auditable entity. Both kinds of delete are recorded with `Action = Deleted` (never `Updated`):

- **Hard delete** (the row is removed, e.g. a custom role): `OldValue` holds the row's last known
  state (sensitive columns redacted), `NewValue` is empty.
- **Soft delete** (`IsDeleted` set to true, e.g. a student or a file): `OldValue`/`NewValue` hold the
  changed columns (`IsDeleted`, `DeletedAt`, `DeletedById`).

Every `Deleted` row also carries the entity's key values (`Id`, or each column of a composite
key) and `EntityId`, so `GET /api/audit/logs?action=Deleted&entityName=…&entityId=…` finds it.
Covered by `AuditDeleteTests`.
