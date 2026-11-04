# EducationHub

Multi-tenant management platform for schools and education centers: an ASP.NET Core API
(`src/`) and an Angular admin console (`admin/`).

| Part | Path | Stack |
|---|---|---|
| API | `src/EducationHub.API` + `Application` / `Domain` / `Infrastructure` / `Shared` | .NET 10, EF Core (SQL Server), MediatR, FluentValidation, JWT |
| Admin SPA | `admin/` | Angular 20, PrimeNG 20 |
| Tests | `tests/EducationHub.Tests.Unit`, `tests/EducationHub.Tests.Integration`, `admin/**/*.spec.ts` | xUnit (integration runs against a real SQL Server), Jasmine/Karma |

## Quick start (Windows, development)

Prerequisites: .NET SDK 10.0.401 (`global.json`), Node 24, SQL Server on `localhost`.

```powershell
.\start-dev.ps1      # starts the API + Angular dev server and opens Swagger and the admin UI
```

Details, URLs and seeded logins: [`LOCAL_RUN_AND_TEST_STATUS.md`](LOCAL_RUN_AND_TEST_STATUS.md)
and [`docs/SEED_DATA_AND_LOCAL_LOGIN.md`](docs/SEED_DATA_AND_LOCAL_LOGIN.md).

The API refuses to start without a JWT signing key (`EducationHub__Jwt__SigningKey`, at least
32 bytes). The `EducationHub.API` launch profile supplies a development-only key.

## Build and test

```powershell
dotnet build EducationHub.sln                      # warnings are errors
dotnet test tests\EducationHub.Tests.Unit
dotnet test tests\EducationHub.Tests.Integration   # needs SQL Server, see below

cd admin
npm ci
npx ng build
npm run test:ci                                    # headless Chrome
```

Integration tests create (and drop) a throw-away database per run. They connect to
`Server=localhost;Trusted_Connection=True;TrustServerCertificate=True` unless
`EducationHub__TestConnection` points elsewhere.

CI (`.github/workflows/ci.yml`) runs all of the above on every push, with SQL Server 2022 in a
service container.

## Where things are

| Topic | Document |
|---|---|
| Roadmap and phase status (source of truth) | [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) |
| Feature-by-feature status | [`docs/FEATURE_STATUS.md`](docs/FEATURE_STATUS.md) |
| Audit and open defects | [`docs/PROJECT_AUDIT.md`](docs/PROJECT_AUDIT.md), [`docs/audit/07-backlog.md`](docs/audit/07-backlog.md) |
| Phase 0/1 security hardening (historical) | [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) |

## Rules of the road

- Migrations are additive; existing migrations are never edited; production never auto-migrates.
- Seeding is add-only and idempotent; passwords are never invented or reset.
- New permissions are added only through `PermissionConstants.Areas`.
- Every new endpoint ships with tenant-isolation and permission-denial tests.
