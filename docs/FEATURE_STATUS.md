# Feature Status

Snapshot of every product area against the original plan, updated 2026-11-03.
Backend for every row below was built and tested on 2026-11-03 against a real SQL Server
(dev machine): build 0/0, unit 84/84, integration 108/109 then the one failure fixed (see
`LOCAL_RUN_AND_TEST_STATUS.md`). "Done (verified)" = covered by tests that actually ran and passed.
Legend: **Done** = implemented, endpoints live, covered by passing tests; **Partial** =
present with known gaps; **Missing** = nothing in the tree.
Full defect evidence: `docs/PROJECT_AUDIT.md`. Roadmap: `docs/IMPLEMENTATION_PLAN.md`.

## Delivered (original Phases 0–2 + parts of 7)

| Feature | Status | Notes / evidence |
|---|---|---|
| Login / refresh / logout | Done | `AuthController`; lockout, throttling, anti-enumeration; `AuthEndpointTests` |
| Password change / forgot / reset / email confirm | Done | log-only email sink in dev (`LogEmailSender`) |
| Profile (`/auth/me`) | Done (verified) | `TenantName` populated — BL-009 fixed in Mission Phase A (2026-11-03) |
| Localization (ar/en, content negotiation) | Done | `LocalizationEndpointTests` |
| Platform: tenants CRUD, suspend, settings | Done | `TenancyController`; platform-scope tests |
| Branches CRUD | Done | `TenancyController` (branches section) |
| Tenant settings | Done | `TenantSettingsEndpointTests` |
| Users CRUD, roles assign, reset password, toggle active | Done | `UsersController`; cross-tenant duplicate email shows raw conflict (BL-011) |
| Roles CRUD, permission assignment | Done | `RolesController` |
| Permission catalog (46 codes) | Partial | `Platform.*` unused by endpoints (BL-007) |
| Academic years (incl. set-current) | Done | one Current per tenant enforced by filtered unique index |
| Grade levels, subjects | Done | unique `(TenantId, Code)`; `(TenantId, Order)` unique lacks IsDeleted filter (BL-016) |
| Teachers + compensation rules | Done | Mission Phase 2 (2026-10-21): `TeacherProfile`/`TeacherSubject`/`CompensationRule`, precedence resolver, atomic teacher-user creation, `TeachersController` + `CompensationRulesController`, Angular `features/teachers`; `TeachersEndpointTests` (11) + `CompensationResolverTests` + `TeacherValidatorTests`; group scope deferred to Mission Phase 3 |
| Students + parents + guardian links | Done (verified) | Mission Phase 2.5a (2026-11-03): `StudentsController`, `ParentsController` incl. `/api/parents/me/children` (P1-4 ownership), Angular students/parents/my-children; `StudentsEndpointTests` (9), `StudentValidatorTests` |
| Dashboard | Done (verified) | `/api/dashboard/kpis` (students, groups, teachers, sessions today, 30-day attendance; money KPIs with `Reports.View`) + KPI cards |
| Audit log + summary + login history | Done | `AuditController`; `AuditSummaryEndpointTests`; noisy for token rows (BL-012) |
| Tenant isolation (query filter + platform scope) | Done | `TenantIsolationTests`, `TenantSecurityEndpointTests` |
| API validation (FluentValidation pipeline) | Done | P0-1/P0-3 fixed; 400 + `Errors.Validation` |
| Swagger generation | Done | P0-2 fixed; `SwaggerDocumentTests` |
| Seed pipeline (roles, admin, academic catalog, demo teachers/students/parents) | Done | `Seed/` classes incl. `AcademicSeeder` (Phase 1a) + `DemoTeacherSeeder` (Phase 1b) + `DemoStudentParentSeeder` (Phase 1c, 2026-10-21); `SeedingTests`, `AcademicSeedingTests`, `DemoTeacherSeedingTests`, `DemoStudentParentSeedingTests` |
| Files (upload/download) | Done (verified) | `FilesController` (Phase 2.5b), documents dialog on students; `FilesEndpointTests` |
| Study groups, enrolment, weekly slots | Done (verified) | Phase 3: `/api/groups`, Angular groups |
| Sessions + attendance | Done (verified) | Phase 3: `/api/sessions`, generate + attendance sheet |
| Fee rules, charges, payments, balances | Done (verified) | Phase 4: FIFO allocation, void, monthly generation |
| Cashbox, expenses, teacher settlements, reports | Done (verified) | Phase 5: `/api/finance/*`, Angular finance + reports |
| Homework + submissions, exams + results | Done (verified) | Phase 6 |
| In-app notifications | Done (verified) | Phase 6: `/api/notifications`, top-bar bell |
| Angular Admin (21 features) | Done | all real-API driven, permission-guarded, en/ar i18n; `npm run test:ci` 41/41 |

## Still missing / deferred

| Feature | Original phase | Status |
|---|---|---|
| Production email/SMS provider | 7 | Deferred (dev log sink only) |
| Parent/student/teacher portals | 8 | Deferred (user decision 2026-10-20); parents have `/my-children` only |
| Lesson plans / materials library | 5 | Not in scope of this mission |
| Linking an existing user account to a student/parent from the UI | 3 | API supports it; no UI |

## Test inventory (passing suites, last full run recorded in `LOCAL_RUN_AND_TEST_STATUS.md`)

- Unit (`EducationHub.Tests.Unit`): auth, domain rules, localization keys, permission
  catalog, compensation-resolver precedence (`CompensationResolverTests`) and teacher /
  compensation command validators (`TeacherValidatorTests`).
- Integration (`EducationHub.Tests.Integration`, live SQL Server + real host): auth endpoint
  flows, tenant isolation/provisioning/security/settings, users/roles, platform user
  creation, localization, audit summary, swagger, seeding (5 tests), academic catalog
  seeding (5 tests), demo teacher seeding (7 tests), demo student/parent seeding
  (8 tests), teachers endpoints (`TeachersEndpointTests`, 11 tests — CRUD, atomic
  creation rollback, link-once conflict, subject replacement, permission boundary, tenant
  isolation, compensation precedence + guards). Latest full run 2026-10-21:
  76/76 integration, 63/63 unit, build 0/0, `ng build` green. New since then (not yet run
  locally): `StudentsEndpointTests` (9), `FilesEndpointTests` (2), `SchedulingEndpointTests` (3),
  `FinanceEndpointTests` (4), `LearningEndpointTests` (2), `PhaseABacklogFixTests` (4),
  unit `StudentValidatorTests`, `FinanceEngineTests`.
