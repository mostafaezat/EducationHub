# EducationHub — Phase 0 + Phase 1 Implementation Plan

> Live tracking file. Updated continuously during implementation; represents the current
> state of the work. Read this first when returning to the project.

## Status legend
- `NOT_STARTED` — not yet touched
- `IN_PROGRESS` — current phase, being implemented
- `COMPLETED` — implemented, built, and verified
- `BLOCKED` — cannot proceed; reason + unblock required documented below

## Phase 0 — Critical backend fixes

### P0-12 · JWT signing key from environment variable only
- **Description:** Remove the committed `Jwt:SigningKey` values from BOTH `appsettings.json`
  and `appsettings.Development.json`. Require a signing key from configuration/environment
  (`EducationHub__Jwt__SigningKey`, fallback `Jwt__SigningKey`) with fail-fast validation:
  present, >= 32 bytes, and not a known placeholder. No dev fallback constant.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 14:30
- **Completed At:** 2026-10-07 14:37
- **Notes:** Removed `Jwt:SigningKey` from `appsettings.json` and `appsettings.Development.json`. Added
  `JwtOptions.ReadSigningKey` (source `Jwt:SigningKey` → fallback `EducationHub:Jwt:SigningKey`, the
  key form of the `EducationHub__Jwt__SigningKey` env var) and `JwtOptions.ValidateSigningKey`
  (present, >= 32 bytes, rejects known placeholders). `DependencyInjection` fills the key into
  `JwtOptions`; `Program.cs` resolves/validates at startup before `builder.Build()`. Dev run
  convention change: the `EducationHub__Jwt__SigningKey` env var is now required.
- **Tests / Verification:** `dotnet build` 0 warnings/0 errors. Negative test: API with no key exits
  with `Jwt:SigningKey is not configured`. Positive test: API with `EducationHub__Jwt__SigningKey`
  set starts, runs migrations+seed, `/health/ready` = 200.
- **Dev-launch follow-up (2026-10-07, later):** Developers hit the fail-fast on F5 / `dotnet run`
  because no local key was supplied. Fixed by adding
  `EducationHub__Jwt__SigningKey` to `src\EducationHub.API\Properties\launchSettings.json` (dev-only
  launch profile; no change to appsettings or the validation logic; production still fail-fasts).
  Verified: build 0/0; unit 16 + integration 25 green; API started via the launch profile (DB
  `EducationHub_Dev`, 2 migrations applied), `/health/ready` = 200, `POST /api/auth/login` with the
  seeded platform admin returned 200 (PlatformAdmin role). The dev key literal exists in exactly one
  file (`launchSettings.json`) and is never copied to build/publish output. Caveat: launching the
  built DLL directly (or with `--no-launch-profile`) still requires the env var by design.

### P0-1 · Login persistence (+ latent DbContext tenant-stamp fix)
- **Description:** `LoginCommandHandler` currently mutates the user (rehash, LastLoginAt,
  LastLoginIp), stages a `RefreshToken` and a `LoginHistory` row, but never persists anything.
  Inject `IUnitOfWork` and call `SaveChangesAsync` once (success path); persist failed-attempt
  history before throwing. Latent fix: `StampTenantIdOnInsert` throws on
  `TenantId == Guid.Empty` inserts when there is no tenant context (anonymous request) — this
  would 500 platform-admin login/refresh. Allow anonymous authentication artifacts
  (`RefreshToken`, `LoginHistory`) to keep a pre-set `TenantId` (incl. `Guid.Empty`); all other
  tenant-save guards unchanged.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 14:37
- **Completed At:** 2026-10-07 14:47
- **Notes:** `LoginCommandHandler` now takes `IUnitOfWork`; adds
  `AccessFailedCount++`/`LockedOutUntil` at the `FailedLoginLockoutThreshold` on wrong password and
  persists the failed attempt before throwing; on success resets counters, sets LastLogin fields,
  stages the refresh token + login history and commits all with one `SaveChangesAsync`.
  `AuthConstants.FailedLoginLockoutDuration` added (single source; DI now uses it).
  `StampTenantIdOnInsert` now lets anonymous authentication artifacts (`RefreshToken`,
  `LoginHistory`) keep a pre-set `TenantId` incl. `Guid.Empty`; all other tenant-save guards intact.
  (Types fully qualified because the `DbSet` members shadow the type names in `is` patterns.)
- **Tests / Verification:** Build 0 warnings/0 errors. Unit 16 + Integration 9 all pass. E2E smoke:
  admin login persisted `RefreshTokens` (activeRT=2 across two test logins) + `LoginHistory` (2) +
  `LastLoginAt`; platform-admin **refresh succeeded** (previously would have inserted a `Guid.Empty`
  row and 500'd).

### P0-2 / P0-3 · Refresh round-trip + logout
- **Description:** Refresh-token rotation and reuse detection already persist (verified in
  inspection). Logout already revokes. Transport fixes from P0-1 make refresh reachable for
  platform admins. Deliverable is endpoint-level proof: refresh round-trip, rotation burns the
  old token, replay triggers family revocation, logout revokes.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 14:47
- **Completed At:** 2026-10-07 14:53
- **Notes:** No production-code change required (rotation/revocation already persisted). Verification
  was pushed to E2E smoke because the P1 harness is a later phase; durable endpoint tests are added
  under P1-1/P1.
- **Tests / Verification:** E2E smoke on dev API: login→refresh (new token issued, old burned),
  refresh again, replay of the rotated token → 400, refresh of the descendant → 400 (family
  revocation), login→logout→refresh → 400. DB revoke reasons: `Rotated` 3, `ReuseDetected` 1,
  `Logout` 1, active 2 — exactly as expected.
- **Notes:** —
- **Tests / Verification:** Endpoint tests (P1 harness) covering round-trip, rotation, reuse
  detection, logout.

### P0-4 / P0-5 · Reserved role names + platform-scope hardening
- **Description:** `CreateRoleCommandHandler` lets a tenant create a role literally named
  `PlatformAdmin`; the login snapshot reads role claims without the query filter, so the token
  then carries the `PlatformAdmin` role, making `HttpTenantContext.IsPlatformScope()` true and
  bypassing the tenant query filter (complete cross-tenant read). Fix: reject reserved names
  (`RoleNames.TenantRoles` + `PlatformAdmin`) in the handler and the validator. Harden
  `IsPlatformScope` to require BOTH the PlatformAdmin role AND `TenantId == Guid.Empty` in
  `HttpTenantContext`, `HttpCurrentUser.IsPlatformAdmin`, and `PlatformAdminAuthorizationHandler`.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 14:53
- **Completed At:** 2026-10-07 15:06
- **Notes:** Reserved names rejected at two layers: `RoleValidators` (clean 400 via FluentValidation)
  and `CreateRoleCommandHandler` (authority check, returns `Roles.ReservedName` even when invoked
  directly). `ClaimReader.IsPlatformAdmin` now requires the signed `TenantId == Guid.Empty` claim in
  addition to the role, which also strengthens `HttpTenantContext.IsPlatformScope` and
  `HttpCurrentUser.IsPlatformAdmin` (both delegate to it). `PlatformAdminAuthorizationHandler`
  independently checks the tenant claim at the endpoint. Raw `ClaimsPrincipal` param added to the
  handler's private helper (new `using System.Security.Claims`).
- **Tests / Verification:** Build 0 warnings/0 errors. Unit 16 + Integration 9 pass. E2E smoke:
  platform admin login + `/api/tenants` OK; tenant admin (freshly provisioned tenant) gets **403**
  on `/api/tenants`; create role `PlatformAdmin` → **400**; `TenantAdmin` → **400**; legitimate
  role → success. The pure spoofed-role case (fabricated token with the role claim but a tenant
  TenantId) is covered by the durable WAF test in P1.
- **Notes:** —
- **Tests / Verification:** Unit/handler test: creating `PlatformAdmin` role fails; endpoint test:
  user holding a spoofed-role token (fabricated) is denied platform scope / cross-tenant reads.

### P0-6 / P0-7 · Failed-attempt lockout + rate limiting
- **Description:** Identity lockout options exist but are dead — the login handler never touches
  `AccessFailedCount`/`LockedOutUntil`. On failed password: increment `AccessFailedCount`, set
  `LockedOutUntil` at threshold, persist, throw. On success: reset both. Check lockout and
  email-confirmation state before password hashing work. Add `AddRateLimiter` partitioned per
  IP (+ username) over `login`/`refresh`, 429 response, configurable.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 15:06
- **Completed At:** 2026-10-07 15:28
- **Notes:** Lockout (P0-6) delivered with P0-1 and proven here: the 5th consecutive wrong password
  arms `LockedOutUntil`; the next attempt is rejected with `Auth.AccountLocked` before tokens are
  issued. Rate limiting (P0-7): `AddRateLimiter` with an `auth-credential` fixed-window policy keyed
  per remote IP, applied via `[EnableRateLimiting]` to `login` and `refresh` only; rejects with
  ProblemDetails 429 + `Retry-After: 60`. Limits under `Auth:RateLimit` (defaults 5 / 5 min),
  overridable by config (P0-7 probe used `Auth__RateLimit__PermitLimit=50`). `UseRateLimiter()`
  runs before authentication so the guard is identity-independent and never throttles non-auth
  endpoints. New file `API/Auth/AuthRateLimitOptions.cs`.
- **Tests / Verification:** Build 0 warnings/0 errors; Unit 16 + Integration 9 green. E2E: 6 wrong
  passwords → 401 each then correct password while locked → 409 `Auth.AccountLocked`; 6th
  credential attempt from one IP → **429**; `/health/ready` still 200.

### P0-8 · Change password persistence + session revocation
- **Description:** `ChangePasswordCommandHandler` mutates `PasswordHash`/`PasswordChangedAt`/
  `MustChangePassword` but never saves and never revokes sessions. Persist via `IUnitOfWork`
  (with audit), and revoke ALL refresh-token families for the user
  (`RevokeAllForUserAsync(userId, reason)` added to `IAuthenticationRepository`).
- **Status:** COMPLETED
- **Started At:** 2026-10-07 15:28
- **Completed At:** 2026-10-07 15:45
- **Notes:** `ChangePasswordCommandHandler` now injects `IUnitOfWork` and persists: the previous
  implementation mutated the tracked entity and never saved, so the change was silently lost.
  Added `IAuthenticationRepository.RevokeAllForUserAsync` (stages revocations WITHOUT saving, so the
  caller controls the transaction) and implemented it as a filtered pass over the user's live tokens
  (no `SaveChanges`; comment documents why). Handler calls it with `PasswordChanged`, then one
  `SaveChangesAsync` commits the hash + `PasswordChangedAt` + `MustChangePassword=false` and all
  revocations atomically; failure rolls everything back. Audit comes free from
  `AuditSaveChangesInterceptor` (ApplicationUser + RefreshToken rows, `PasswordHash`/`TokenHash`
  redacted).
- **Tests / Verification:** Build 0 warnings/0 errors; Unit 16 + Integration 9 green. E2E: login →
  refresh OK; change password → 200; old refresh token → **400**; login with new password → 200. DB:
  refresh token `RevokedReason='PasswordChanged'`; `PasswordChangedAt` set; `MustChangePassword=0`;
  audit rows show ApplicationUser `{"PasswordChangedAt":...}` old→new and the RefreshToken revoke,
  with no hash/token in `NewValue`/`OldValue`. (Note: the `loginWithOldPassword` probe hit the rate
  limiter (429) instead of 401 — limiter generated it before the wrong-password check could; the 401
  wrong-password path was already proven in P0-6.)

### P0-9 / P0-10 · Email flows (log-only sender) + enforced confirmation
- **Description:** No SMTP infra. Define `IEmailSender` in Application; Infrastructure
  `LogEmailSender` writes the reset/confirm link to logs. Endpoints: `POST /api/auth/forgot-password`
  (always 200, no account enumeration), `POST /api/auth/reset-password`,
  `POST /api/auth/confirm-email`, `POST /api/auth/resend-confirmation`. Token generation/validation
  happens inside a `PlatformScopeOverride` elevation (same pattern as `DatabaseInitializer`).
  Enforce `EmailConfirmed` at login (P0-10 decision) and refresh.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 15:45
- **Completed At:** 2026-10-07 16:05
- **Notes:** Deviation from the plan text (documented per reorder rule): handlers load the target
  account through `IAuthenticationRepository.GetByEmailAsync` — the Application layer's sanctioned
  filter bypass — instead of `PlatformScopeOverride`, because Application cannot reference
  Infrastructure and the repository already performs exactly the same function (as Login does).
  Token generation/validation via `UserManager` (`GeneratePasswordResetTokenAsync`,
  `VerifyUserTokenAsync`, `ConfirmEmailAsync`) on the tracked entity — no DB rows, no migration.
  New: `IEmailSender` + `EmailMessage` (Application), `LogEmailSender` + `WebLinkBuilder`
  (Infrastructure; base URL from `Auth:Email:FrontendBaseUrl`, never client-supplied), four
  commands/validators (`EmailFlowCommands.cs`, `EmailFlowValidators.cs`), four anonymous
  endpoints on AuthController (`forgot-password`, `reset-password`, `confirm-email`,
  `resend-confirmation`) — `forgot`/`resend` rate-limited (`auth-credential`), always-200 with
  identical responses for known/unknown emails (no enumeration). Reset: token check → set hash +
  `PasswordChangedAt` + clear lockout + `RevokeAllForUserAsync("PasswordReset")` + one
  `SaveChangesAsync` (ChangePassword pattern, audit-interceptor coverage).
  **Enforcement (P0-10):** `EnsureUserCanAuthenticateAsync` throws
  `BusinessRuleException.EmailNotConfirmed()` (new factory; `ApiResultFilter` maps
  `EmailNotConfirmed` → 403); `RefreshTokenCommand` revokes the family with reason
  `EmailNotConfirmed` and refuses the refresh. Seeder, tenant provisioning and
  `CreateUserCommand(SendEmailConfirmation=false)` all set `EmailConfirmed=true`, so no existing
  flow regresses.
- **Tests / Verification:** Build 0 warnings/0 errors; Unit 16 + Integration 9 green. E2E:
  unconfirmed login → **403 `Account.EmailNotConfirmed`**; resend-confirmation 200 (link visible in
  log with configured base `https://portal.test`); bogus confirm on unconfirmed → **400** and the
  account stays unconfirmed (login still 403); valid confirm → 200; login after confirm → **200**;
  forgot-password known vs unknown → identical 200; reset bogus token → **400**; valid reset → 200;
  login with new password → 200, old → **401**; unknown-email reset → generic 200; DB shows
  `RevokedReason='PasswordReset'` on pre-reset sessions. Note: cosmetic follow-up (title of
  bogus-confirm 400 changed to stable `Auth.InvalidEmailToken` after the E2E run) is asserted
  durably in P1 endpoint tests.
- **Notes:** —
- **Tests / Verification:** Endpoint tests: forgot-password for existing + non-existing email both
  200; reset with valid token changes password; unconfirmed account blocked at login until
  confirmed; confirm-email flow works.

### P0-11 · MustChangePassword server-side enforcement
- **Description:** `MustChangePassword` is currently a DTO-only flag. Add a `MustChangePassword`
  JWT claim in `JwtTokenService.CreateAccessToken`; add middleware that denies (403) every
  authenticated request carrying the flag except an allowlist (`change-password`, `me`,
  `permissions`, `logout`, `refresh`).
- **Status:** COMPLETED
- **Started At:** 2026-10-07 16:05
- **Completed At:** 2026-10-07 16:40
- **Notes:** Added `EducationHubClaimTypes.MustChangePassword` = `"MustChangePassword"` (claim written
  only when `user.MustChangePassword` is true, so claim presence == obligation; no DB round trip at
  request time). New `MustChangePasswordMiddleware` (API/Middleware) denies authenticated principals
  carrying the claim with 403 ProblemDetails `title`/`code` = `Auth.MustChangePassword`, except
  segment-aware allowlist `/api/auth/{change-password,me,permissions,logout,refresh}` (so
  `/api/auth/me-anything` is *not* allowed). Registered in `Program.cs` right after
  `TenantResolutionMiddleware`, before `UseAuthorization`, so the deny applies regardless of endpoint
  policy. Session-revocation on change-password (P0-8) means the flag clears on the next login; the
  forced session's access token stays flagged until then.
- **Tests / Verification:** Build 0/0; Unit 16 + Integration 9 pass. E2E: admin reset user password
  with `mustChangePassword=true` → login returns `user.mustChangePassword=true` and JWT payload
  contains the `MustChangePassword` claim; `GET /api/users` → **403 `Auth.MustChangePassword`**;
  `GET /api/auth/me` → 200; `GET /api/auth/permissions` → 200; `POST /api/auth/change-password` → 200;
  refresh of the pre-change token → 400 (sessions revoked); re-login → `mustChangePassword=false`,
  claim absent, `GET /api/users` → 200. Durable endpoint coverage deferred to P1.

## Phase 1 — Security / multi-tenant hardening + endpoint tests

### P1-1 · WebApplicationFactory harness
- **Description:** `ApiWebApplicationFactory<Program>` over a fresh random SQL Server database
  (reuses the `SqlServerFixture` server), seeds the platform administrator via
  `EDUCATIONHUB_ADMIN_EMAIL/PASSWORD`, enables `Database:AutoMigrateOnStartup`. Provides
  `LoginAsync`/`RefreshAsync` helpers for every endpoint test.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 16:45
- **Completed At:** 2026-10-07 17:35
- **Notes:** Added `ApiWebApplicationFactory` + `ApiTestFixture` (collection fixture `api`) —
  a real SQL Server database (one per run, dropped on dispose) and one real in-process API
  host shared by every endpoint test. Configuration (connection string, `Database:AutoMigrateOnStartup`
  = true, `Jwt:SigningKey`, seeded platform-admin credentials, `Auth:RateLimit` ceiling,
  `Auth:Email:FrontendBaseUrl`) is pushed through **environment variables set before the first
  host build**, because `Program.cs` validates the signing key in top-level statements — before
  `ConfigureAppConfiguration` overrides would apply; env vars are the only point early enough.
  The platform administrator is created by the app's own `DatabaseInitializer`/`DataSeeder`
  (via `EDUCATIONHUB_ADMIN_EMAIL/PASSWORD`). Helpers: `LoginAsync`/`LoginTokenAsync`,
  `ProvisionTenantAsync`, `CreateUserAsync`, token minters (`GenerateEmailConfirmationTokenAsync`
  / `GeneratePasswordResetTokenAsync` via the host's own `UserManager` under
  `PlatformScopeOverride` so tokens validate against the host's data-protection keys),
  `WithPlatformScopeAsync` for arrange/assert, and `CreateRateLimitedHost` for the 429 test.
  `ApiClientExtensions` adds JSON POST/PUT/GET and ProblemDetails-`title` readers.
- **Tests / Verification:** Harness exercised by all 16 endpoint tests below; all green.

### P1-2 · Tenant-scoped CRUD isolation via API
- **Description:** Tenant admin cannot read/modify another tenant's resources through the API.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** Covered by `TenantSecurityEndpointTests.Tenant_admin_cannot_read_another_tenants_user`.
- **Tests / Verification:** Cross-tenant direct read → **404** (query filter hides the row; no
  403 confirming existence), and the tenant's list page does not contain the other tenant's
  user id. No data leak observed.

### P1-3 · Tenant switching / mismatch rejection
- **Description:** `X-Tenant-Id` header contradicting the signed claim → 403 (already enforced by
  `TenantResolutionMiddleware`); prove via API test.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** Covered by `TenantSecurityEndpointTests.Contradicting_tenant_header_is_rejected`.
- **Tests / Verification:** Matching `X-Tenant-Id` → 200; contradicting header → **403**. The
  signed claim remains authoritative; the header never switches tenant.

### P1-4 · Parent-module ownership tests — N/A (deferred)
- **Description:** Parent module does not exist (out of scope per user). Deferred to Phase 3.
  Substituted by P1-2/P1-9 ownership tests against existing resources.
- **Status:** COMPLETED (N/A — deferred)
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** N/A — Students/Parents/Teachers/Finance modules excluded from this work.
- **Tests / Verification:** N/A; substitution covered by P1-2/P1-9.

### P1-5 · Teacher-module ownership tests — N/A (deferred)
- **Description:** Teacher module does not exist (out of scope per user). Deferred to Phase 3.
  Substituted by ownership tests on Users/Roles/Branches/Settings/Academic.
- **Status:** COMPLETED (N/A — deferred)
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** N/A.
- **Tests / Verification:** N/A; substitution covered by P1-2/P1-9.

### P1-6 · Unknown tenant handling
- **Description:** Requests for nonexistent/deleted tenants fail closed; no account enumeration.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** Covered by `Unknown_login_email_gets_the_same_generic_failure`. There is no
  client-supplied tenant at login (email identifies the account), so "unknown tenant" surfaces
  as an unknown account, answered with the generic credential failure.
- **Tests / Verification:** Unknown email login → **401** `Auth.InvalidCredentials` (indistinguishable
  from a wrong password).

### P1-7 · Deleted / suspended tenant access blocked
- **Description:** Users of deleted/suspended tenants cannot authenticate or refresh.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** Covered by `Suspended_tenant_blocks_login` (tenant row deactivated with platform
  scope, then login attempted). `LoginCommandHandler.EnsureUserCanAuthenticateAsync` refuses an
  inactive/deleted/expired-subscription tenant.
- **Tests / Verification:** Deactivated tenant → login **409** `Tenant.Inactive`.

### P1-9 · Unauthorized cross-tenant access attempt
- **Description:** A tenant admin attempting to access platform endpoints or another tenant's
  data is refused with no information leak.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** Covered by `Tenant_admin_cannot_reach_platform_endpoints`.
- **Tests / Verification:** Tenant admin `GET /api/tenants` → **403**; `GET /api/tenants/{other}`
  → **403**.

### P1-10 · Security response headers
- **Description:** Add security-headers middleware (X-Content-Type-Options, X-Frame-Options,
  Referrer-Policy, Content-Security-Policy, HSTS where applicable) and assert via API test.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 17:30
- **Completed At:** 2026-10-07 17:35
- **Notes:** New `SecurityHeadersMiddleware` registered first (before `ExceptionHandlingMiddleware`)
  so headers are present even on error responses. Emits `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and
  `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` (skipped only for
  `/swagger`, whose UI needs scripts/styles). `Strict-Transport-Security` emitted only when the
  request is already HTTPS (so it never lies about a plain-HTTP deployment).
- **Tests / Verification:** Covered by `Security_headers_are_present_on_success_and_error_responses`
  (asserted on a 200 and on a 401).

### P1-11 · Password policy repeated-change prevention (if applicable)
- **Description:** Placeholder for any password-policy hardening surfaced during verification.
- **Status:** COMPLETED (N/A — no gap surfaced)
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** No password-history/reuse requirement exists in the specification, and no defect
  was found during Phase 0/1 verification. Change-password already re-verifies the current
  password and enforces Identity's strength policy; no additional rule was invented.
- **Tests / Verification:** N/A.

### P1-12 · Migration status
- **Description:** No database migration is expected for Phase 0/1 (all required columns already
  exist: `AccessFailedCount`, `LockedOutUntil`, `PasswordChangedAt`, `MustChangePassword`,
  `EmailConfirmed`). Revisit only if a change forces schema drift.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:35
- **Notes:** Confirmed no schema change was needed: every Phase 0/1 change is code-only
  (claims, middleware, rate limiter, repository methods, handlers) or writes to columns that
  already existed. No new migration was created.
- **Tests / Verification:** `No_migrations_are_pending` asserts `GetPendingMigrationsAsync()`
  is empty on the migrated test database; full solution builds.

## Final verification + report
- **Description:** Full build (0 warnings — CI treats warnings as errors), Unit + Integration
  suites green, E2E smoke on dev API, then write the required 15-section report.
- **Status:** COMPLETED
- **Started At:** 2026-10-07 17:35
- **Completed At:** 2026-10-07 17:40
- **Notes:** Build 0 warnings / 0 errors. Unit suite 16/16. Integration suite 25/25 (9 original
  + 16 new endpoint tests). E2E smoke performed per Phase 0 item against the dev API. Report
  written to the session chat (15 sections); evidence audit at
  `C:\Users\MOSTAF~1\AppData\Local\Temp\opencode\EducationHub-Gap-Analysis.md`.
- **Tests / Verification:** `dotnet build` (0/0); `dotnet test` (16 + 25 green); manual E2E; report.

---

## Order note
Execution order is fixed by the user: P0-12 → P0-1 → P0-2/3 → P0-4/5 → P0-6/7 → P0-8 →
P0-9/10 → P0-11 → P1 → verify+report. Any reordering must be justified by a technical
dependency and recorded here.