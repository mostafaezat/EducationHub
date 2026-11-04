# 01 · Executive Summary

## Scope in one line
A single-tenant-per-account, multi-tenant tutoring-platform **backend** (5 src projects, 2 test
projects, ~16.2k C# LOC) with authentication, tenancy, users/roles/permissions, branches/settings,
academic years/grades/subjects, and read-only audit. **No frontend.**

## Overall health
| Dimension | Result |
|---|---|
| Build | **PASS** — `dotnet build -c Debug`: 0 warnings, 0 errors (14.5 s). |
| Unit tests | **PASS** — 16/16. |
| Integration tests | **PASS** — 25/25 (real SQL Server). |
| Runtime probes | **2 FAILURES** — invalid login → 500; `/swagger/v1/swagger.json` → 500. |
| Test coverage of input validation | **NONE** — the validation layer never executes, so no test could catch it. |

The codebase is unusually well-documented and several hard problems are genuinely solved (tenant
isolation via EF global query filters + save-time guards, per-tenant role uniqueness, refresh-token
rotation and reuse detection, lockout, anti-enumeration email flows). The test suite is real
(SQL Server, not in-memory) and passes.

However, **two defects are production blockers**, and both were invisible to the passing test suite.

## Production blockers
### P0-1 — Request validation never runs (FluentValidation is inert)
Every FluentValidation validator targets a DTO (`LoginRequest`, `CreateUserRequest`, …) but the
MediatR request types are the `*Command` wrappers (`LoginCommand(LoginRequest Request)`). MediatR's
`ValidationBehavior<TRequest>` resolves `IValidator<LoginCommand>`, finds none, and returns early —
so **no request body is ever validated**. Invalid input flows straight into handlers.

*Proven:* `POST /api/auth/login` with `{"email":"","password":""}` returns **HTTP 500**
(`Errors.Internal`, `System.ArgumentException: ... 'normalizedEmail'` from
`AuthenticationRepository.GetByEmailAsync`) instead of a 400 validation error.

Evidence: `src/EducationHub.Application/DependencyInjection.cs:31-35`,
`src/EducationHub.Application/Common/Behaviors/ValidationBehavior.cs:23-37`,
`src/EducationHub.Application/Features/Auth/Commands/LoginCommand.cs:14`,
`src/EducationHub.Application/Features/Auth/Validators/AuthValidators.cs:5`.

### P0-2 — Swagger document cannot be generated (`/swagger` → 500)
Two DTOs are both named `ResetPasswordRequest` (Auth and Users namespaces). Swashbuckle emits schema
id `$ResetPasswordRequest` for both and throws. The `Operation` for `UsersController.ResetPassword`
cannot be generated, so the whole document fails.

*Proven:* `GET /swagger/v1/swagger.json` → **HTTP 500**,
`Can't use schemaId "$ResetPasswordRequest" ... same schemaId is already used for type
"...Auth.DTOs.ResetPasswordRequest"`.

Evidence: `src/EducationHub.Application/Features/Auth/DTOs/AuthDtos.cs:53`,
`src/EducationHub.Application/Features/Users/DTOs/UserDtos.cs:66`, `src/EducationHub.API/Program.cs:183-210`.

## High-priority (non-blocking) findings
- **P1-1** `FluentValidation.ValidationException` is unhandled → even once P0-1 is fixed, validation
  failures would return 500, not 400 (`ExceptionHandlingMiddleware.cs` catches it only as a generic
  `Exception`).
- **P1-2** Localization is declared but absent — `LocalizationKeys`/`SupportedLanguages` exist, but no
  resource files, no `UseRequestLocalization`, no `IStringLocalizer` usage.
- **P1-3** `IFileStorageService`/`LocalFileStorageService` are fully implemented but **no HTTP
  endpoint** exposes them (no upload/download controller).

## Medium findings
- `PermissionConstants` advertises `Platform.*` and `Dashboard.View` permissions with no endpoints or
  handlers behind them.
- Controllers return `Ok(...)` (200) while `[ProducesResponseType(...201Created)]` is declared
  (e.g. `UsersController.cs:51` vs `:56`).
- `UserProfileDto.TenantName` is always `null` (`LoginCommand.MapProfile` passes `null`).
- `ApplicationUser`/`ApplicationRole.TenantId` are non-nullable `Guid` while docs say "null for
  platform"; `Guid.Empty` is the sentinel.

## What is genuinely solid
- Tenant isolation (query filter + save-time `TenantId`-change and cross-tenant-insert guards), proven
  by dedicated SQL Server tests including a fail-closed "no tenant = no rows" case.
- Authentication hardening: refresh rotation + family revocation on reuse, per-account lockout,
  per-IP credential rate limiting, identical responses for unknown-email flows, must-change-password
  gate, security headers, correlation ids.
- Permission model: one authorization policy per permission; privilege-boundary handlers re-read from
  the database.
- Clean layering (Domain → Application → Infrastructure/API) with no Application→ASP.NET dependency.

## Bottom line
The backend's architecture and security posture are strong, and its tests pass — but the **input
validation layer does not execute at all** and **Swagger is non-functional**. Both are small to fix
and high impact. See `08-remediation-plan.md`.
