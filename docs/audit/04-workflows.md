# 04 · Workflow Traces

Each workflow was traced through source. Where an integration test exercises it, the test name is
given (all listed tests currently pass — see `06-verification-results.md`). Findings that affect a
workflow are cross-referenced.

---

## WF-001 · Tenant provisioning
`POST /api/tenants` → `CreateTenantCommand` → handler stages `Tenant`, `TenantSettings`, seven system
roles + permission claims, first admin user in one transaction.
- Entry: `TenancyController.cs:49-58` (platform-admin only).
- Handler: `Features/Tenancy/Commands/TenantCommands.cs`; roles via `TenantRoleProvisioner`.
- Tests: `TenantProvisioningTests` — creates-all-together, cross-tenant email conflict
  (`Tenants.AdminEmailTaken`), rollback on weak password (all pass).
- Note: body validation does not run (P0-1); handler-side + Identity checks still enforce key rules.

## WF-002 · Login
`POST /api/auth/login` → `LoginCommand` → `AuthenticationRepository.GetByEmailAsync(normalized)` →
password re-hash verify → active/locked/email-confirmed/tenant checks → issue access + refresh tokens →
record `LoginHistory` → single `SaveChanges`.
- Entry: `AuthController.cs:35-38`; handler `LoginCommand.cs`.
- Tests: happy path, lockout, unknown-email generic failure, suspended tenant (all pass).
- **Defect:** an empty/invalid email reaches the handler and 500s (P0-1) because `LoginRequestValidator`
  never runs.

## WF-003 · Refresh + reuse detection
`POST /api/auth/refresh` → `RefreshTokenCommand` → look up by hash → rotate (burn old, insert successor
in same `FamilyId`) → on replay of a used token, revoke the whole family.
- Entry: `AuthController.cs:55-58`.
- Test: `Refresh_rotates_the_token_and_reuse_revokes_the_family` (pass).

## WF-004 · Logout
`POST /api/auth/logout` → `LogoutCommand` → revoke presented refresh token (no-op if absent).
- Entry: `AuthController.cs:64-69`; test `Logout_revokes_the_presented_refresh_token` (pass).

## WF-005 · Change password / must-change gate
`POST /api/auth/change-password` (authenticated) → verify current → set new → revoke sessions.
`MustChangePasswordMiddleware` restricts a flagged user to `/api/auth/change-password`, `/me`,
`/permissions`, `/logout`, `/refresh`.
- Entry: `AuthController.cs:77-82`; middleware `MustChangePasswordMiddleware.cs`.
- Tests: `Change_password_revokes_sessions_and_replaces_the_credential`,
  `Must_change_password_blocks_everything_but_the_allowlist` (pass).

## WF-006 · Forgot / reset password
`POST /api/auth/forgot-password` → always 200 with identical body (anti-enumeration) → email link.
`POST /api/auth/reset-password` → validate token → replace credential.
- Entry: `AuthController.cs:90-107`; handler `EmailFlowCommands.cs`.
- Test: `Password_reset_flow_replaces_the_credential_and_hides_unknown_addresses` (pass).

## WF-007 · Email confirmation
`POST /api/auth/confirm-email` (+ `resend-confirmation`); login is refused (`Account.EmailNotConfirmed`)
until confirmed.
- Test: `Unconfirmed_account_is_refused_then_admitted_after_confirmation`,
  `Bogus_confirmation_token_is_rejected_with_a_stable_code` (pass).

## WF-008 · Create user
`POST /api/users` → `CreateUserCommand` → resolve roles within caller's tenant → create via
`UserManager` → assign roles. Tenant is taken from context, never the body.
- Entry: `UsersController.cs:49-58`; handler `UserCommands.cs`.
- Tests: `ApiScenarios.CreateUserAsync` used across auth/security tests (pass).
- **Defect:** `CreateUserRequestValidator` never runs (P0-1), so field rules are enforced only by
  Identity (surfacing as generic errors). Response is 200 despite declared 201.

## WF-009 · Assign roles (privilege boundaries)
`PUT /api/users/{id}/roles` → `AssignRolesCommand` → replace role set; caller cannot grant a role with
permissions/scope beyond their own.
- Entry: `UsersController.cs:122-132`; handler `AssignRolesCommand.cs`.
- Not re-exercised by a dedicated HTTP test this pass; boundary logic relies on prior source read.

## WF-010 · Role & permission administration
`/api/roles` CRUD + `PUT /api/roles/{id}/permissions`; `/api/permissions` catalog. System roles and
roles with members are refused on delete; role names immutable; new roles start permissionless.
- Entry: `RolesController.cs`, `PermissionsController.cs`; handlers `RoleCommands.cs`.
- Note: catalog includes `Platform.*`/`Dashboard.View` with no endpoints (P2).

## WF-011 · Branch & tenant settings
`/api/branches` CRUD (tenant-scoped); `/api/settings` GET/PUT (partial update, null = unchanged).
- Entry: `TenancyController.cs` `BranchesController`, `TenantSettingsController`.

## WF-012 · Academic year current-year invariant
Creating/marking a year current demotes the previous; `AcademicCommandSupport.ClearCurrentAsync`
enforces exactly one current year per tenant. `/api/academic-years/current` returns 404 when unset.
- Entry: `AcademicController.cs` `AcademicYearsController`; handler `AcademicYearCommands.cs`.

## WF-013 · Audit read
`/api/audit/logs`, `/logs/{id}`, `/summary`, `/logins` — read-only; rows written by
`AuditSaveChangesInterceptor`.
- Entry: `AuditController.cs`.
- Note: interceptor audits all `IAuditableEntity` including `RefreshToken`/`LoginHistory`, producing
  high-volume audit rows (noise); secrets are redacted.

## Cross-workflow observations
- **Validation is bypassed for every workflow** (P0-1) — the single most pervasive issue.
- **Error shape is correct for `BusinessRuleException`** (400 + code + errors) and for the special
  exception types in `ExceptionHandlingMiddleware`; it is wrong for `ValidationException` (500).
- All create endpoints return 200 while documenting 201 (P2).
