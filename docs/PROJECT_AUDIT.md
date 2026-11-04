# Project Audit

Re-verified 2026-10-20 against the current tree (`D:\test API\NEWApp\New folder`).
Consolidates and refreshes the earlier audit in `docs/audit/` (00–08). Every status below was
re-checked against live code and tests this session; **Verified** means "read in code or
observed in a test run today", **Assumption** means "consistent with evidence but not
directly executed", **Unverified** means "recorded from the earlier audit, not re-checked".

## Scope and evidence base

| Source | Used for |
|---|---|
| `docs/audit/02-requirements-traceability.md` | Requirement taxonomy A–O |
| `docs/audit/03-phase-gap-analysis.md` | Original phase numbering 0–8 |
| `docs/audit/05-production-blockers.md`, `07-backlog.md` | Defect list (BL/GAP ids) |
| `src/`, `tests/`, `admin/` | Live re-verification |

Limitations: the original 59-section product spec is **not** in the repo; the A–O taxonomy in
`docs/audit/02` is the closest recovered requirement set. This directory is **not a git
repository**, so historical diffs cannot be inspected and there is no commit-level safety net.

## Defect status (re-verified 2026-10-20)

### Fixed — evidence in code and tests

| ID | Defect | Evidence (Verified) |
|---|---|---|
| P0-1 / BL-001 | FluentValidation not wired | `Application/Common/Behaviors/ValidationBehavior.cs` registered in `Application/DependencyInjection.cs:32`; command-level validators exist (`Features/Auth/Validators/AuthValidators.cs:78`); regression test `AuthEndpointTests.Login_with_empty_credentials_is_rejected_with_400` asserts 400 + `Errors.Validation`. |
| P0-2 / BL-002 | Swagger duplicate schema `$ResetPasswordRequest` | DTO renamed (`AdminResetPasswordRequest`); `SwaggerDocumentTests` covers `/swagger/v1/swagger.json`. |
| P0-3 / BL-003 | ValidationException → 500 | `API/Middleware/ExceptionHandlingMiddleware.cs:52` catches `FluentValidation.ValidationException` and writes 400 `Errors.Validation` with field names stripped of the `Request.` prefix. |
| P1-2 / BL-004 | Localization claim without implementation | Request-localization pipeline + resource files + `LocalizedValidator<T>` base exist; `LocalizationEndpointTests` covers content negotiation. |
| BL-006 | Missing regression tests for validation + Swagger | `AuthEndpointTests` (400 on empty login) and `SwaggerDocumentTests` (200 on swagger.json) exist. |
| BL-008 | Creates declared 200 instead of 201 | `UsersController`, `RolesController`, `TenancyController`, `AcademicController` create endpoints declare `ProducesResponseType(..., 201Created)` (Verified in source; runtime 201 not re-executed). |

### Open

| ID | Defect | Evidence | Planned |
|---|---|---|---|
| P1-3 / BL-005 | File upload/download endpoints missing | `IFileStorageService` is implemented but no controller references it (grep of `src/EducationHub.API/Controllers` returns nothing). | Mission Phase 6 |
| BL-007 | `Platform.*` / unused permissions in catalog without enforcement | `PermissionConstants` defines `Platform.View/ManageTenants/...` and `Dashboard.View`; controllers authorize by role (`[Authorize(Roles=...)]`) or by other permission codes; no endpoint requires `Platform.*`. | Documented; reconcile in Mission Phase 8 |
| BL-009 | `UserProfileDto.TenantName` always null | `LoginCommand.MapProfile` passes `null` for the `TenantName` argument (`LoginCommand.cs:216`). | Mission Phase 8 (cheap fix) |
| BL-010 | `TenantId` nullability inconsistency (`Guid.Empty` sentinel vs nullable in DTOs) | `RoleDtos` vs `AuthDtos` treat it differently. Not re-diffed line-by-line this session (**Unverified** detail). | Phase 8 review |
| BL-011 | Cross-tenant duplicate email surfaces as identity conflict | Not re-verified. (**Unverified**) | Phase 8 review |
| BL-012 | Audit noise from RefreshToken/LoginHistory writes | Not re-verified. (**Unverified**) | Phase 8 review |
| BL-013 | `ApiResultFilter` brittle HTTP mapping | Not re-verified. (**Unverified**) | Phase 8 review |
| BL-014–BL-018 | P3 hardening (launchSettings, README/CI, filtered unique index on GradeLevel Order, pagination edge cases, `SortOrder` naming) | Not re-verified except: GradeLevel `(TenantId, Order)` unique index confirmed **not** filtered on `IsDeleted` (`TenancyConfigurations.cs:151`, migration `InitialSchema`). | Phase 8 / backlog |

## Functional gaps (product scope, not defects)

Original numbering from `docs/audit/03-phase-gap-analysis.md` and `07-backlog.md`:

| ID | Original phase | Gap | Status 2026-10-20 |
|---|---|---|---|
| GAP-01 | Phase 3 | Students / teachers / parents / enrolment | **Partial (Mission Phase 2, 2026-10-21)** — teachers module delivered (`TeacherProfile`/`TeacherSubject`/`CompensationRule`, `TeachersController`, `CompensationRulesController`, Angular `features/teachers`, migration `AddTeachersModule`); students/parents profile entities and enrolment still missing (user accounts seeded in Phase 1c). |
| GAP-02 | Phase 4 | Classes / timetable / attendance | **Missing** |
| GAP-03 | Phase 5 | Lessons / homework / exams / results | **Missing** |
| GAP-04 | Phase 6 | Fees / invoices / payments | **Missing** (settings fields only) |
| GAP-05 | Phase 8 | Parent/student portal | **Missing** — deferred by original plan; current mission scope = Admin SPA only. |
| GAP-06 | Phase 7 | Production email provider + SMS | **Partial** — `LogEmailSender` dev sink only; no SMS. |
| — | Phase 7 | File storage service without endpoints | Service exists; HTTP surface missing (same as P1-3). |

## Angular Admin inventory (Verified via file listing)

13 features today: auth, dashboard, tenants, users, roles, permissions, branches, settings,
academic, teachers, audit, profile. All call the real API (no mock layer). Conventions:
feature modules under `admin/src/app/features/`, services under `core/services/`,
permission-guarded nav in `core/nav.ts`, i18n dictionaries in
`core/i18n/parts/{en,ar}/`. No schedule/student/fee/finance screens exist.

## Seed pipeline (Verified)

`src/EducationHub.API/Seed/`: `RoleSeeder` → `AdminUserSeeder` → `AcademicSeeder`
(catalog seed for the `admin-ui-test` tenant, Phase 1a) → `DemoTeacherSeeder` (that
tenant's demo teacher accounts, Phase 1b) → `DemoStudentParentSeeder` (that tenant's demo
student + parent accounts, Phase 1c; two independently password-gated groups) →
`DevelopmentDataSeeder` (no-op placeholder).
Runs only from `DatabaseInitializer` under
`ASPNETCORE_ENVIRONMENT=Development` + `Database:AutoMigrateOnStartup=true`.
Integration-tested by `SeedingTests` (5 tests), `AcademicSeedingTests` (5 tests),
`DemoTeacherSeedingTests` (7 tests) and `DemoStudentParentSeedingTests` (8 tests).
Development DB observations
(`EducationHub_Dev`, Verified via sqlcmd 2026-10-21 after Phase 1c):
22 users (18 before Phase 1c + exactly 4 new); `admin-ui-test` holds 7 users
(`tenant.admin` + 2 teachers + 2 students + 2 parents; each demo user has a membership
row on that tenant's own `Teacher`/`Student`/`Parent` role), 1 branch, 8 grades (legacy
G1 Order 1 + KG1…G6 Orders 2–8),
5 subjects (MATH + ARB/ENG/SCI/SOC),
academic year 2025/2026 demoted to Upcoming/IsCurrent=0 (dates unchanged) and 2026/2027
Current (2026-09-01 → 2027-06-30). Every other tenant's user count is unchanged from the
pre-Phase-1c baseline; all other tenants still hold 0 grades, 0 subjects, 0 years.
A second startup run created nothing (live idempotency observed).

## Test state (Verified by execution 2026-10-21, after Mission Phase 2)

- `dotnet build`: 0 warnings, 0 errors.
- Unit suite: 63/63 passed.
- Integration suite: 76/76 passed (65 before Phase 2 + 11 new `TeachersEndpointTests`).
- Angular: `ng build` green; EN/AR i18n key parity 492/492.
- Coverage known to include: auth flows, tenant isolation/provisioning/security, users/roles
  endpoints, localization, audit summary, swagger, seeding, academic catalog seeding,
  demo teacher seeding, demo student/parent seeding, teachers CRUD + atomic creation
  rollback + link-once conflict + subject replacement + permission boundary + tenant
  isolation + compensation precedence/guards. Coverage **not** present for future
  modules (they do not exist yet).

## Assumptions (explicit)

- The A–O requirement taxonomy is a faithful reconstruction of the original spec.
- `admin-ui-test` is the intended demo tenant (user decision 2026-10-20).
- Arabic strings shown as `????` in sqlcmd output are a console codepage artifact, not data
  corruption (not re-checked in hex; low risk).
