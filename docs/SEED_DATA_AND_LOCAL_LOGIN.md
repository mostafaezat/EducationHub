# Seed Data & Local Login

How EducationHub bootstraps its database, where the development administrator comes from,
and how to sign in locally. Applies to `src/EducationHub.API` seeding (`Seed/` classes).

---

## 1. When seeding runs

Seeding is reachable from exactly one place: `DatabaseInitializer.ApplyAsync`, invoked by
`Program.cs` only when **both** guards hold:

| Guard | Setting |
|-------|---------|
| Environment | `ASPNETCORE_ENVIRONMENT=Development` |
| Opt-in flag | `Database:AutoMigrateOnStartup=true` |

`DatabaseInitializer` runs `MigrateAsync` first, then the seed pipeline under an explicit
platform scope. In any other environment, or with the flag off, no migration and no seed
code executes — production schema changes are an explicit, reviewed `dotnet ef database
update` step.

## 2. Pipeline order

`DatabaseSeeder.SeedAsync` runs the classes in dependency order:

| # | Class | Responsibility |
|---|-------|----------------|
| 1 | `RoleSeeder` | Ensures the platform `PlatformAdmin` role exists and reconciles its full permission set; repairs the seven tenant roles (`TenantOwner`…`Student`) for every non-deleted tenant via `TenantRoleProvisioner` — the same component runtime tenant provisioning uses, so permission sets are defined exactly once. |
| 2 | `AdminUserSeeder` | Creates the first platform administrator from configuration (below). Must run after `RoleSeeder`: the account is assigned `PlatformAdmin` immediately after creation. |
| 3 | `AcademicSeeder` | Seeds the academic catalog into the `admin-ui-test` demo tenant — see §4.1. |
| 4 | `DemoTeacherSeeder` | Seeds that tenant's demo teacher accounts — see §4.2. |
| 5 | `DemoStudentParentSeeder` | Seeds that tenant's demo student and parent accounts — see §4.4. |
| 6 | `DevelopmentDataSeeder` | No-op by design — see §4.6. |

Everything is written as "ensure this exists", never "insert": restarting repeatedly is
safe, and a half-completed previous run is repaired instead of compounded.

## 3. The administrator account (`AdminUserSeeder`)

Configuration is read from **environment variables** (not `appsettings.json`, so it is
never committed):

| Variable | Meaning |
|----------|---------|
| `EDUCATIONHUB_ADMIN_EMAIL` | Email address of the platform administrator to seed |
| `EDUCATIONHUB_ADMIN_PASSWORD` | Its initial password (must satisfy the password policy, §6) |

One-time workflow: set both variables → start the API once → unset them. The seeder is
idempotent by email lookup, so the variables are not needed again.

Behavioural guarantees:

- **Warn + skip, never fail startup.** If either variable is missing, the seeder logs a
  warning and creates nothing. It never invents a default password and never blocks boot.
- **Create-if-absent only.** If the email already exists, the seeder does nothing at all.
  An existing account's password is **never reset, rotated or logged** — by design, and no
  code path in the repository changes it.
- **Passwords are never written to logs or configuration files** at any point.

## 4. Demo data (`DevelopmentDataSeeder`, `AcademicSeeder`, `DemoTeacherSeeder`, `DemoStudentParentSeeder`)

### 4.1 Academic catalog (`AcademicSeeder`)

Runs after `RoleSeeder`/`AdminUserSeeder` and targets **exactly one tenant**, found by slug
`admin-ui-test` (`AcademicSeeder.DemoTenantSlug`). If no active tenant has that slug the
seeder logs a warning and does nothing — it never creates tenants (tenant creation stays a
through-the-API concern, §4.6). All lookups use `IgnoreQueryFilters()` so soft-deleted rows
are still recognised as occupying their unique keys.

What it ensures, always **add-only** (existing rows are never updated or deleted):

| Entity | Key | Behaviour |
|---|---|---|
| Academic year `2026/2027` | `(TenantId, Name)` | 2026-09-01 → 2027-06-30, `Status=Current`, `IsCurrent=true`. Before insert, any other current year in the tenant is demoted with the product's own semantics (`AcademicCommandSupport.ClearCurrentAsync`): `IsCurrent=false` and `Status Current→Upcoming` — **never** archived, deleted, or date-changed. Demotion is saved **before** the insert, because the filtered unique index `IX_AcademicYears_TenantId_IsCurrent` allows only one current year per tenant. |
| Grade levels KG1, KG2, G1–G6 | `(TenantId, Code)` | Skipped if the code exists (incl. soft-deleted rows — the unique indexes are not `IsDeleted`-filtered). New rows take `Order = max(existing Order) + 1`, so a pre-existing `G1` at Order 1 keeps Order 1 and the rest get 2…8. |
| Subjects Arabic, English, Mathematics, Science, Social Studies | `(TenantId, Code)` | Same skip-if-exists rule; `SortOrder` is a running count of the tenant's subjects. `NameAr` is set on every row (required by the model). |

Restarting the API any number of times leaves the data byte-identical; nothing outside
`admin-ui-test` is ever written.

### 4.2 Demo teacher accounts (`DemoTeacherSeeder`)

Runs after `AcademicSeeder` and targets the same `admin-ui-test` tenant (slug lookup with
the same warn+skip contract). It ensures two **user accounts** in that tenant:

| E-mail | Full name | Role |
|---|---|---|
| `teacher.one@educationhub.local` | Demo Teacher 1 | Tenant `Teacher` |
| `teacher.two@educationhub.local` | Demo Teacher 2 | Tenant `Teacher` |

Guarantees:

- **Add-only, keyed by e-mail (globally unique).** An existing account with the same
  e-mail is skipped entirely — never modified, never re-roled, password never reset —
  even when it belongs to a *different* tenant (its owner keeps it).
- **Role assignment is tenant-scoped.** The `Teacher` row used is the one owned by the
  user's own tenant (`ApplicationUserStore` resolves roles inside the user's tenant).
  If that row is somehow missing, the seeder logs a warning and skips — role repair is
  `RoleSeeder`'s job, and seeding never crashes the pipeline.
- Accounts are created active with a confirmed e-mail so they can sign in immediately.
- Teacher *profiles*, subject assignments and compensation rules are **not** part of
  this seeder: they belong to the teachers module (delivered in Mission Phase 2). The two
  demo teacher accounts therefore carry only the `Teacher` role — create their profiles
  from Admin → Teachers with **Link existing account** (no password change involved).

### 4.3 The demo-teacher password

The initial password comes from one environment variable — never hard-coded, and never
invented:

| Variable | Meaning |
|----------|---------|
| `EDUCATIONHUB_DEMO_TEACHER_PASSWORD` | Initial password for both demo teachers (must satisfy the password policy, §6) |

Workflow: set the variable → start the API once → unset it. If it is unset, the seeder
logs a warning and creates nothing (a logged no-op, like `AdminUserSeeder`). If the
accounts already exist, the variable is irrelevant — their passwords are never reset or
rotated.

### 4.4 Demo student & parent accounts (`DemoStudentParentSeeder`)

Runs after `DemoTeacherSeeder` against the same `admin-ui-test` tenant (same slug lookup,
same warn+skip contract). It ensures four user accounts in that tenant, in two
independently gated groups:

| E-mail | Full name | Role | Password variable |
|--------|-----------|------|-------------------|
| `student.one@educationhub.local` | Demo Student 1 | `Student` | `EDUCATIONHUB_DEMO_STUDENT_PASSWORD` |
| `student.two@educationhub.local` | Demo Student 2 | `Student` | `EDUCATIONHUB_DEMO_STUDENT_PASSWORD` |
| `parent.one@educationhub.local` | Demo Parent 1 | `Parent` | `EDUCATIONHUB_DEMO_PARENT_PASSWORD` |
| `parent.two@educationhub.local` | Demo Parent 2 | `Parent` | `EDUCATIONHUB_DEMO_PARENT_PASSWORD` |

Guarantees (identical in spirit to §4.2):

- **Add-only, keyed by e-mail (globally unique).** Existing accounts are skipped entirely —
  never modified, never re-roled, password never reset — even when they belong to a
  different tenant (its owner keeps it). A skipped e-mail does not stop the rest of its
  group or the other group.
- **Tenant-scoped roles.** Each group is assigned the `Student` / `Parent` row owned by
  the user's own tenant; a missing role row skips only that group with a logged warning
  (role repair belongs to `RoleSeeder`), never a pipeline crash.
- Accounts are created active with a confirmed e-mail so they can sign in immediately.
- Student/parent *profile* entities and the student↔parent link are **not** part of this
  seeder: they belong to their module phases (later mission phases).

### 4.5 The demo student/parent passwords

Each group has its own environment variable — never hard-coded, never invented:

| Variable | Meaning |
|----------|---------|
| `EDUCATIONHUB_DEMO_STUDENT_PASSWORD` | Initial password for both demo students (§6 policy) |
| `EDUCATIONHUB_DEMO_PARENT_PASSWORD` | Initial password for both demo parents (§6 policy) |

Workflow: set the variable(s) → start the API once → unset them. The groups are gated
**independently**: an unset variable skips only that group (logged warning), so either can
be seeded alone. Existing accounts' passwords are never reset or rotated regardless of
what the variables hold.

### 4.6 Nothing else (`DevelopmentDataSeeder`)

The seeder intentionally manufactures no further demo records. The documented demo data — the
`admin-ui-test` tenant and `tenant.admin@educationhub.local` — is created **through the
API** (tenant creation, user creation), which exercises the same validation, provisioning,
audit and role-assignment paths a real record takes. Seeding would bypass all four.

### Accounts observed in the local dev database

| Account | Roles | Origin |
|---------|-------|--------|
| `platform.admin@educationhub.local` | PlatformAdmin | Created out of band (not by any source file in this repository); documented in `LOCAL_RUN_AND_TEST_STATUS.md` §6. The seeder only ever skips it. |
| `tenant.admin@educationhub.local` | TenantAdmin (tenant `admin-ui-test`) | Created through the API. |
| `admin@educationhub.local` | PlatformAdmin | Created out of band; undocumented, provenance unknown. |
| `*-@example.test` probes | various | Leftovers from interactive verification runs. |

None of these are modified by seeding. Deleting leftovers is a manual, reviewed operation —
never part of startup.

## 5. Integration tests

`tests/EducationHub.Tests.Integration/ApiTestFixture.cs` pushes the same two environment
variables (with test-only values, `platform.admin@educationhub.test` /
`Platf0rm#Adm1n1`) before building its host, so every test run seeds its own throwaway
database exactly the way a developer seeds theirs. `SeedingTests` covers idempotency, the
no-password-reset guarantee, role assignment, and both startup guards. `AcademicSeedingTests`
covers the catalog seeder (§4.1): catalog + demotion, double-run idempotency, tenant
scoping, missing-tenant no-op, and soft-deleted rows still holding unique codes — each test
uses a tenant with a unique slug because `IX_Tenants_Slug` is platform-unique and the
collection shares one database. `DemoTeacherSeedingTests` covers the teacher seeder (§4.2):
creation + tenant role membership + a working login, idempotency and no-modification of
pre-existing accounts, tenant scoping, e-mails owned by other tenants, and the missing
tenant / password / role-row no-ops (unique tenants *and* unique e-mails per test, because
Identity e-mail is globally unique). `DemoStudentParentSeedingTests` covers the student/
parent seeder (§4.4): both role groups + both logins, idempotency and no-modification,
tenant scoping, foreign-e-mail skip without stopping the group, missing tenant, missing
both passwords, single-password group gating, and a missing role row skipping only its
group.

## 6. Local login

**Endpoint:** `POST /api/auth/login` `{ "email", "password", "rememberMe" }`.

**Documented development credentials** (see `LOCAL_RUN_AND_TEST_STATUS.md` §6 for the full
table, including the tenant administrator):

| Account | Email | Password | Role |
|---------|-------|----------|------|
| Platform admin | `platform.admin@educationhub.local` | `Admin1234` | PlatformAdmin |
| Demo teacher 1 | `teacher.one@educationhub.local` | `EDUCATIONHUB_DEMO_TEACHER_PASSWORD` at seed time — `Demo#Teacher123` in the local dev DB (§4.3) | Teacher (tenant `admin-ui-test`) |
| Demo teacher 2 | `teacher.two@educationhub.local` | as above | Teacher (tenant `admin-ui-test`) |
| Demo student 1 | `student.one@educationhub.local` | `EDUCATIONHUB_DEMO_STUDENT_PASSWORD` at seed time — `Demo#Family123` in the local dev DB (§4.5) | Student (tenant `admin-ui-test`) |
| Demo student 2 | `student.two@educationhub.local` | as above | Student (tenant `admin-ui-test`) |
| Demo parent 1 | `parent.one@educationhub.local` | `EDUCATIONHUB_DEMO_PARENT_PASSWORD` at seed time — `Demo#Family123` in the local dev DB (§4.5) | Parent (tenant `admin-ui-test`) |
| Demo parent 2 | `parent.two@educationhub.local` | as above | Parent (tenant `admin-ui-test`) |

**Prerequisites enforced by the login flow** (`LoginCommand`):

- Email must be confirmed, account must be active, tenant (if any) must be active and in
  subscription — otherwise `403 Account.EmailNotConfirmed` / `Account.Inactive` / etc.
- 5 consecutive failures lock the account for 15 minutes.
- Credential endpoints are rate-limited per IP (`auth-credential`): bursts answer `429`
  with `Retry-After: 60`.

**Password policy** (Identity options, `Infrastructure/DependencyInjection.cs`): minimum
8 characters, at least one digit and one lowercase letter; uppercase and non-alphanumeric
characters are not required.

**Password recovery** is log-only in development: `POST /api/auth/forgot-password` always
answers `200` (anti-enumeration) and the reset link/token is written to the API log by
`LogEmailSender`.
