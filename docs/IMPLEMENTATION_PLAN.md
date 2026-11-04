# Implementation Plan

**Source of truth** for remaining work. Created 2026-10-20 during mission Phase 0.

This document **preserves the original plan's numbering** (Phases 0–8, recovered from the
root `IMPLEMENTATION_PLAN.md`, `docs/audit/03-phase-gap-analysis.md` and
`docs/audit/07-backlog.md`) and maps the current mission's action items onto it. The root
`IMPLEMENTATION_PLAN.md` (Phases 0–1 remediation, all COMPLETED) is a historical record and
must not be overwritten. Original status details: see the root plan and
`docs/PROJECT_AUDIT.md`.

## Original phase map

| Original | Theme | Status (2026-10-20) | Evidence |
|---|---|---|---|
| 0 | Auth hardening + provisioning foundations | Implemented | `docs/PROJECT_AUDIT.md` (P0-1/2/3 fixed); root plan Phase 0 all P0-1…P0-12 |
| 1 | Core tenancy (tenants/branches/settings/users/roles) | Implemented | Tenancy/Users/Roles endpoints + isolation tests; root plan Phase 1 |
| 2 | Academic structure (years/grades/subjects) | Implemented | `AcademicController` + feature slice + tests |
| 3 | People & enrolment (students/teachers/parents) | Implemented (pending local verification) | Mission Phases 2, 2.5, 3 |
| 4 | Scheduling (classes/timetable/attendance) | Implemented (pending local verification) | Mission Phase 3 |
| 5 | Learning (lessons/homework/exams/results) | Implemented — homework/exams (pending local verification) | Mission Phase 6; lesson plans not in scope |
| 6 | Finance (fees/invoices/payments) | Implemented (pending local verification) | Mission Phases 4–5 |
| 7 | Notifications & files | Implemented in-app + files; email/SMS still a dev sink (GAP-06) | Mission Phases 2.5b, 6 |
| 8 | Frontend / portal | **Partial** | Angular Admin exists for Phases 0–2 only; portals deferred (GAP-05, user decision) |

## Mission phase overlay

The current mission's Phases 0–9 map onto the original numbering as follows. Mission
checklists are updated as each phase completes.

### Mission Phase 0 — Recover plan, audit, status docs ✅ (2026-10-20)
- [x] Recover original phase numbering (0–8) and requirements (A–O).
- [x] Re-verify defect statuses against current code/tests.
- [x] `docs/PROJECT_AUDIT.md` created.
- [x] This document created.
- [x] `docs/FEATURE_STATUS.md` created.
- Limitations recorded: no git repository; 59-section spec not in repo.

### Mission Phase 1 — Seed data
Split into 1a (catalog, now) and 1b–1d (module demo data, with each module).

**1a — Academic catalog seed ✅ (user-approved 2026-10-21)**
- [x] `AcademicSeeder` class in `src/EducationHub.API/Seed/`, wired into `DatabaseSeeder`.
- [x] Academic year `2026/2027` (2026-09-01 → 2027-06-30) marked Current for
      `admin-ui-test`; stale `2025/2026` demoted (`IsCurrent=false`, `Status → Upcoming`,
      same semantics as `AcademicCommandSupport.ClearCurrentAsync`; never archived,
      never deleted).
- [x] Grades KG1, KG2, G1–G6 (add-only; existing rows untouched).
- [x] Subjects Arabic, English, Mathematics, Science, Social Studies (add-only).
- [x] Idempotent, keyed by `(TenantId, Code)`; add-only, never overwrites.
- [x] Tests: idempotency, tenant scoping, academic-year behavior.
- [x] `docs/SEED_DATA_AND_LOCAL_LOGIN.md` updated.

**1b — Demo teacher accounts ✅ (user-approved 2026-10-21)**
Scope: add-only seed of demo **user accounts** carrying the tenant `Teacher` role into the
`admin-ui-test` tenant. Teacher *profiles* / the teachers module belong to Mission Phase 2
and are explicitly out of scope here.
- [x] `DemoTeacherSeeder` in `src/EducationHub.API/Seed/`, wired into `DatabaseSeeder`
      after `AcademicSeeder`; registered in `Program.cs`.
- [x] Accounts `teacher.one@educationhub.local`, `teacher.two@educationhub.local`
      (`FullName` "Demo Teacher 1"/"Demo Teacher 2"), `TenantId` = demo tenant, active,
      `EmailConfirmed`; initial password from `EDUCATIONHUB_DEMO_TEACHER_PASSWORD`
      (warn + skip when unset; never an invented default password).
- [x] Assigned the demo tenant's own `Teacher` role row via `UserManager` (the user store
      resolves the role inside the user's tenant); role row missing → logged skip, never a
      pipeline crash.
- [x] Idempotent, keyed by email (globally unique); existing accounts never modified,
      passwords never reset; e-mail already owned by another tenant → left untouched.
- [x] Missing tenant → logged no-op (same contract as `AcademicSeeder`).
- [x] Tests: creation + role membership + working login, idempotency / no-modify of
      pre-existing accounts, tenant scoping, foreign-e-mail skip, missing tenant,
      missing password, missing role row. (`DemoTeacherSeedingTests`, 7 tests — all pass.)
- [x] `docs/SEED_DATA_AND_LOCAL_LOGIN.md` updated (pipeline table, §4.2–§4.3, credentials).

**1c — Demo student & parent accounts ✅ (user-approved 2026-10-21)**
Scope: add-only seed of demo **user accounts** carrying the tenant `Student` and `Parent`
roles into the `admin-ui-test` tenant. Student/parent *profile* entities belong to their
module phases (later mission phases) and are explicitly out of scope here.
- [x] `DemoStudentParentSeeder` in `src/EducationHub.API/Seed/`, wired into `DatabaseSeeder`
      after `DemoTeacherSeeder`; registered in `Program.cs`.
- [x] Accounts `student.one@…`/`student.two@…` ("Demo Student 1/2") and
      `parent.one@…`/`parent.two@…` ("Demo Parent 1/2") in the demo tenant, active,
      `EmailConfirmed`; passwords from `EDUCATIONHUB_DEMO_STUDENT_PASSWORD` /
      `EDUCATIONHUB_DEMO_PARENT_PASSWORD` — each group gated independently (warn + skip
      for the group whose variable is unset; never an invented default password).
- [x] Each group assigned its own tenant's `Student` / `Parent` role row via `UserManager`;
      role row missing → logged skip of that group, never a pipeline crash.
- [x] Idempotent, keyed by email (globally unique); existing accounts never modified,
      adopted or reset; e-mails owned by other tenants left untouched.
- [x] Missing tenant → logged no-op; all seeded records belong to the demo tenant and use
      that tenant's own role rows.
- [x] Tests: creation + both roles + logins, idempotency / no-modify of pre-existing
      accounts, tenant scoping, foreign-e-mail skip, missing tenant, missing passwords,
      single-password group gating, missing role row. (`DemoStudentParentSeedingTests`,
      8 tests — all pass; integration suite 65/65, unit 30/30, build 0/0.)
- [x] `docs/SEED_DATA_AND_LOCAL_LOGIN.md` updated (pipeline table, §4.4–§4.5, credentials).

**1d — Remaining module demo data** (deferred to their modules; each listed where it
lands): groups/schedules/sessions/attendance, fee
rules/charges/payments, expenses/cash/settlements.

### Mission Phase 2 — Teachers + compensation (→ original Phase 3, part 1)
✅ implemented 2026-10-21 (awaiting user review)
- [x] `TeacherProfile`, `TeacherSubject`, `CompensationRule` entities; resolver with
      explicit precedence (Group+Teacher > Teacher+Subject > Teacher > Group > Subject >
      TenantDefault). (`src/EducationHub.Domain/Entities/Teachers/`, `Enums/CompensationEnums.cs`,
      `Features/Teachers/Services/CompensationResolver.cs` — pure `ResolveIn` core ranks
      (T,G,*)=1, (T,!G,S)=2, (T,!G,!S)=3, (!T,G,*)=4, (!T,!G,S)=5, default=6, tie-break by
      lowest rule id; migration `AddTeachersModule` applied to dev DB with 7 filtered
      unique scope indexes.)
- [x] Teachers feature slice (CRUD, subject assignment, atomic teacher-user creation).
      (`Features/Teachers/` DTOs/Commands/Queries/Validators + `TeachersController` +
      `CompensationRulesController`.) `CompensationRule.StudyGroupId` has no FK yet and
      group-scoped rules are rejected with `Compensation.GroupsNotAvailable` until Mission
      Phase 3 ships study groups.
- [x] Permissions `Teachers.*` (View/Create/Update/Delete) in `PermissionConstants` +
      `TenantRoleProvisioner` sets (TenantAdmin full, Teacher/Assistant view).
- [x] Angular `features/teachers`; i18n en/ar; nav + guard. (`teachers.component.ts` list +
      create (new-account/link-existing modes) + edit + delete + compensation-rules
      dialog, `TeachersService`, `parts/{en,ar}/teachers.ts`, nav item +
      `permissionGuard('Teachers.View')`; `ng build` green, EN/AR key parity 492/492.)
- [x] Unit + integration tests (precedence, CRUD, isolation, permission denial).
      (`CompensationResolverTests` + `TeacherValidatorTests` + validator-registration
      asserts; `TeachersEndpointTests` 11 cases incl. atomic rollback, link-once 409,
      teacher-role view/create boundary, tenant isolation, precedence ladder over HTTP.
      Full suites: 63/63 unit, 76/76 integration, build 0/0.)

### Mission Phase A — Engineering foundations (inserted 2026-11-03, user-approved)
Inserted before Phase 2.5 after the 2026-11-03 full-app test + gap analysis. Backend changes
were authored in a cloud workspace without .NET/NuGet access, so they are **not yet compiled
or test-run**; Phase A closes only after the verification gate below passes locally.
- [x] Angular unit tests: `core/guards/guards.spec.ts`, `core/interceptors/auth.interceptor.spec.ts`,
      `core/interceptors/error.interceptor.spec.ts`, `core/i18n/translate.service.spec.ts`
      (+ `core/testing/auth-stub.ts`); `karma.conf.js` with `ChromeHeadlessCI`;
      `npm run test:ci` → 29/29 green.
- [x] CI workflow `.github/workflows/ci.yml` (backend build + unit + integration on SQL Server
      2022 service container; admin build + unit).
- [x] `README.md` (BL-015).
- [x] BL-009: `AuthenticationSnapshot.TenantName` loaded in `AuthenticationRepository`;
      `LoginCommandHandler.MapProfile` returns it (login, refresh, `/auth/me`).
- [x] BL-016: `IX_GradeLevels_TenantId_Order` filtered on `[IsDeleted] = 0` — config +
      additive migration `20261103120000_FilterGradeLevelOrderIndex` (+ Designer, snapshot).
- [x] BL-007 (security part): `TenantsController` now uses the hardened
      `[Authorize(Policy = RoleNames.PlatformAdmin)]` (role **and** `TenantId == Guid.Empty`)
      instead of `[Authorize(Roles = ...)]`, which checked the role alone. The `Platform.*` /
      `Dashboard.View` codes stay in the catalog as UI-gating codes (used by `nav.ts`,
      `app.routes.ts`, dashboard); `Auth.Login` / `Auth.ChangePassword` / `Tenants.View|Update`
      remain unused and are recorded, not removed (removing codes touches seeded role claims).
- [x] Integration tests `PhaseABacklogFixTests` (4): tenant name in profile, none for the
      platform admin, forged PlatformAdmin-role token with a tenant id → 403 on `/api/tenants`,
      soft-deleted grade releases its Order.
- [ ] **Verification gate (local):** `dotnet build` 0/0; unit + integration green
      (expected 63 unit, 80 integration); `dotnet ef migrations list` shows the new migration;
      `No_migrations_are_pending` green.
- [ ] `git init` + first commit (approved by the user 2026-11-03; run locally — the cloud
      workspace cannot reach the machine's shell); push to a GitHub repo to activate CI.

### Mission Phase 2.5 — Students & parents (→ original Phase 3, part 2) (inserted 2026-11-03, user-approved)
Prerequisite of Mission Phase 3: enrolment, attendance and charges all need a student record.
Authored 2026-11-03 in the cloud workspace (no .NET/NuGet there): **backend not yet compiled
or test-run** — the phase closes only after the local verification gate below.

**2.5a — Profiles, guardians, API, UI (implemented 2026-11-03, pending local verification)**
- [x] Entities `StudentProfile` (name on the profile; optional `UserId`; student number,
      birth date, grade, branch, `StudentStatus`, enrolment date, notes), `ParentProfile`
      (name, phone, contact e-mail, optional `UserId`), `StudentGuardian` (relationship,
      primary flag); enums `StudentStatus`, `GuardianRelationship`
      (`Domain/Entities/Students/`, `Domain/Enums/StudentEnums.cs`).
- [x] EF config `StudentsConfigurations.cs` (filtered unique student number / user link,
      restrict FKs to Users/GradeLevels/Branches, cascading junction) + DbSets + additive
      migration `20261103130000_AddStudentsModule` (+ Designer, snapshot).
- [x] Feature slice `Application/Features/Students/` — commands (create/update/delete
      student, set guardians, create/update/delete parent), queries (paged lists with
      search/grade/status filters, by id, `GetMyChildrenQuery`), validators (+ command
      validators), `StudentReadModel` (shared DTO builder), `ProfileAccountSupport`
      (three account modes: none / link existing / create with Student or Parent role,
      atomic with the profile).
- [x] `StudentsController` (`/api/students`, `/{id}/guardians`) and `ParentsController`
      (`/api/parents`, `/api/parents/me/children`).
- [x] Permissions `Students.View|Create|Update|Delete`, `Parents.View|Create|Update|Delete`,
      `Parents.ViewOwnChildren` via `PermissionConstants.Areas`; `TenantRoleProvisioner`:
      TenantAdmin full, Teacher/Assistant `Students.View`, Accountant `Students.View` +
      `Parents.View`, Parent `Parents.ViewOwnChildren` (existing tenants reconciled by
      `RoleSeeder` at startup).
- [x] Localization keys (5 new, EN/AR) for the new validation messages.
- [x] `DemoStudentProfileSeeder` (add-only): profiles for the demo student/parent accounts,
      parent N ↔ student N.
- [x] Deferred P1-4 ownership: a parent reads only `/api/parents/me/children`, scoped by the
      caller's account; the admin lists stay closed to the Parent role.
- [x] Tests: `StudentsEndpointTests` (9 — CRUD, conflict, validation, account sign-in,
      guardian set/replace, two-primary/unknown parent, parent ownership, tenant isolation,
      parent delete unlinks); unit `StudentValidatorTests` (10).
- [x] Angular: `features/students` (list with search + grade/status filters, create/edit,
      optional account, guardians dialog), `features/parents` (+ optional account),
      `features/parents/my-children`, `StudentsService`, routes + nav ("People"), EN/AR
      parts; `students.service.spec.ts`; `npm run test:ci` 34/34, `ng build` green.
- [x] Fix: Angular `TeachersService.list` now sends `search` (the teachers endpoint ignores
      the generic `searchTerm`, so teacher search never filtered).
- [ ] **Verification gate (local):** `dotnet build` 0/0; unit + integration green; app starts
      and `Migrate()` reports no pending model changes; smoke the new screens.

**2.5b — Files ✅ (2026-11-03, pending local verification)**
- [x] `FilesController` over `IFileStorageService` (BL-005 / P1-3): `GET /api/files?ownerEntityName&ownerEntityId`,
      multipart `POST /api/files`, `GET /api/files/{id}/content`, `DELETE /api/files/{id}`; owner
      (Student/Parent/Teacher) verified in the caller's tenant; type/size allow-list.
- [x] Permissions `Files.View/Upload/Delete`; Angular documents dialog on the students page.
- [x] `FilesEndpointTests` (2).

### Mission Phase 3 — Scheduling (→ original Phase 4) ✅ (2026-11-03, pending local verification)
- [x] `StudyGroup`, `Enrollment`, `ScheduleSlot`, `ClassSession`, `SessionAttendance`
      (migration `20261103140000_AddSchedulingModule`; also wires `CompensationRule.StudyGroupId` FK).
- [x] `SessionMaterializer` (idempotent, max 120 days per run), attendance marking for the
      active roster only.
- [x] `/api/groups` (+ `/enrollments`, `/slots`), `/api/students/{id}/enrollments`, `/api/sessions`
      (+ `/generate`, `/{id}/attendance`); permissions `Groups.*`, `Sessions.*`, `Attendance.*`.
- [x] Compensation rules now accept a group scope (`Groups.NotFound` instead of `GroupsNotAvailable`).
- [x] Angular `features/groups` (roster + weekly slots) and `features/sessions` (generate,
      held/cancel, attendance sheet); `SchedulingEndpointTests` (3).

### Mission Phase 4 — Fees & payments (→ original Phase 6, part 1) ✅ (2026-11-03, pending local verification)
- [x] `FeeRule`, `Charge` (amount snapshot), `Payment`, `PaymentAllocation`
      (migration `20261103150000_AddFinanceModule`, shared with Phase 5).
- [x] `FeeEngine` (group → grade → default precedence; monthly generation idempotent per
      `(enrollment, period)`), FIFO allocation with credit remainder, void with reversal,
      student balance.
- [x] `/api/fee-rules`, `/api/charges` (+ `/generate`, `/{id}/cancel`), `/api/payments` (+ `/{id}/void`),
      `/api/students/{id}/balance`; permissions `Fees.*`, `Payments.*`.
- [x] Angular `features/fees` (charges / payments / fee rules tabs, balance strip, record
      payment); `FinanceEndpointTests`, `FinanceEngineTests`.

### Mission Phase 5 — Income, expenses, cashbox, settlements (→ original Phase 6, part 2) ✅ (2026-11-03, pending local verification)
- [x] `CashAccount`, `CashTransaction` (unique `(TenantId, SourceType, SourceId)` double-post
      guard), `ExpenseCategory`, `Expense`, `TeacherPayment`, `SessionCompensation`.
      Deviation: no separate `IncomeEntry` table — income is the `Payment` cash postings.
- [x] `CompensationEngine` (prices held sessions from compensation rules), teacher settlement +
      payment, `CashPostingService`, finance summary report, `/api/dashboard/kpis` (closes BL-007
      for `Dashboard.View`; money KPIs only with `Reports.View`).
- [x] Angular `features/finance` (cash / expenses / teacher settlements) + `/reports` (summary)
      + dashboard KPI cards; permissions `Finance.*`, `Reports.View`.

### Mission Phase 6 — Core learning + notifications (→ original Phases 5 + 7) ✅ (2026-11-03, pending local verification)
- [x] `Homework` + `HomeworkSubmission`, `Exam` + `ExamResult`, `Notification` (in-app)
      (migration `20261103160000_AddLearningModule`).
- [x] `/api/homework` (+ `/{id}/submissions`), `/api/exams` (+ `/{id}/results`), `/api/notifications`
      (own feed, `/{id}/read`, `/read-all`; any signed-in account). New homework and recorded
      results notify the student account and linked parent accounts.
- [x] Angular homework + exams pages, notification bell in the top bar; `LearningEndpointTests` (2).

### Mission Phase 7 — Admin UI verification sweep (→ original Phase 8, Admin only) ✅ (API smoke verified; manual UI walkthrough by user pending)
- [x] Every new backend feature has a UI workflow; nav groups "Teaching" and "Money";
      route guards per permission; EN/AR catalogs in parity (`parts/*/operations.ts`).
- [x] `ng build` green; `npm run test:ci` 41/41 (adds `operations.services.spec.ts`).
- [x] API started for real against `EducationHub_Dev`: `/health/ready` 200, Swagger 200.
- [ ] Manual click-through of the new screens (user).
- Portals (teacher/parent/student) remain **deferred** (user decision 2026-10-20).

### Mission Phase 8 — DB, security, testing ✅ (verified 2026-11-03 on the dev machine)
- [x] Additive migrations only; navigation-free FKs with `Restrict` on business links, `Cascade`
      only for owned children; unique guards listed above.
- [x] `dotnet build` 0 warnings / 0 errors (dev machine + cloud workspace, offline NuGet pack).
- [x] `dotnet ef migrations has-pending-model-changes` → "No changes" (hand-written snapshots were
      correct). All six new migrations applied to `EducationHub_Dev` by the API's dev auto-migrate.
- [x] Bugs found by the real runs and fixed:
      roster query not translatable by EF (homework/exam create → 409);
      teacher payment amount could differ from the unsettled total yet mark every session settled
      (now `TeacherPayments.AmountMismatch`, 400);
      racing payments could over-pay a charge (row-version tokens on `Charges` and
      `SessionCompensations`, migration `20261103172505_AddFinanceConcurrencyTokens`, generated with
      EF tooling; `DbUpdateConcurrencyException` and unique-index violations now answer 409 instead of 500);
      unknown/foreign cash account answered 400 (now 404);
      finance summary accepted an inverted range (now 400);
      `GET /api/students/{foreignId}/enrollments` answered 200 with an empty list (now 404).
- [x] New integration suites: `OperationsSecurityTests` (401/403/404 matrix, Accountant/Teacher/
      Assistant/Student/Parent grants, cross-tenant read/update/delete/list) and
      `FinancialIntegrityTests` (FIFO + partial + void consistency, voided payments excluded from
      reports, cash = ledger, rejected requests write nothing, 5-way races on generate/pay/void).
- [x] Accountant role also gets `Grades.View` + `Teachers.View`; `RoleSeeder` repaired all 10 existing
      dev tenants on startup ("Seed complete: 1 platform role, 10 tenant(s) processed").

### Mission Phase 9 — Docs + final report ✅ (2026-11-04)
- [x] This file, `docs/FEATURE_STATUS.md`, `LOCAL_RUN_AND_TEST_STATUS.md` (§ "2026-11-03 verification").

## Standing rules

1. Root `IMPLEMENTATION_PLAN.md` is historical — never edit.
2. Migrations are additive per phase; existing migrations never modified; production never
   auto-migrates.
3. Seeding is add-only and idempotent, keyed by `(TenantId, Code)`; existing records are
   never overwritten or deleted; passwords never touched.
4. New permissions flow only through `PermissionConstants.Areas`.
5. No git operations without explicit user approval (repo is not under version control —
   see `docs/PROJECT_AUDIT.md` limitations).
