# EducationHub — Remediation Progress

> Persistent resumable tracker for the approved remediation work.
> This is a NEW file; the read-only audit evidence under `docs/audit/` (including
> `AUDIT_PROGRESS.md`) is frozen historical evidence and is NOT modified by this work.
> Repository: `D:\test API\NEWApp\New folder`. Started: 2026-10-14.

## Scope of this file
Remediation of the three approved production blockers, in order:

| ID | Defect | Audit ref | Brief |
|---|---|---|---|
| P0-1 | Request validation never executes | BL-001 / audit finding P0-1 | Validators target `*Request` DTOs; MediatR requests are `*Command` wrappers, so `IValidator<TCommand>` resolves to nothing and the pipeline skips validation. |
| P0-2 | Swagger document cannot be generated | BL-002 | Two DTOs share the simple name `ResetPasswordRequest` → duplicate OpenAPI schema id → `GET /swagger/v1/swagger.json` returns 500. |
| P0-3 | Validation faults surface as 500 | BL-003 / audit finding P1-1 | `FluentValidation.ValidationException` is unhandled by `ExceptionHandlingMiddleware`, so a validation failure becomes a 500 instead of 400. |

Order is fixed: **P0-1 → P0-2 → P0-3**, each verified before the next.

Explicitly out of scope here (separate plans, do NOT start): Arabic/English localization
infrastructure; secure file-storage HTTP endpoints; any net-new business modules or frontend.

## Statuses
`NOT_STARTED` · `IN_PROGRESS` · `COMPLETED` · `BLOCKED` · `SKIPPED`.
Every result recorded here is the **actual observed result**; nothing is marked passed unless executed.

## Task board

| # | Task | Depends on | Status |
|---|---|---|---|
| R0 | Create this progress file | — | COMPLETED |
| R1 | P0-1: command validators composing DTO validators | R0 | COMPLETED |
| R2 | P0-1: verify (unit + integration + probe) | R1 | COMPLETED |
| R3 | P0-2: resolve duplicate schema id | R0 | COMPLETED |
| R4 | P0-2: verify (swagger 200 + tests) | R3 | COMPLETED |
| R5 | P0-3: map `ValidationException` → 400 | R1 (needs validation to fire) | COMPLETED |
| R6 | P0-3: verify (invalid login → 400 + tests) | R5 | COMPLETED |
| R7 | Regression tests added and green | R1,R3,R5 | COMPLETED |
| R8 | Final build + full test run + live probes | R7 | COMPLETED |

### Dependency notes
- R5/R6 are intentionally **after** P0-1: `ValidationException` only becomes reachable once
  validators actually run. P0-1 can be proven independently (DI resolution / unit test); the
  end-to-end 400 requires P0-3.
- P0-2 is independent of P0-1/P0-3.

## Verification criteria (must all hold)

| Check | Command / probe | Pass condition |
|---|---|---|
| Build clean | `dotnet build EducationHub.sln -c Debug --nologo` | Build succeeded, 0 warnings, 0 errors |
| Unit | `dotnet test tests\EducationHub.Tests.Unit -c Debug` | all pass (16 existing + 5 new = 21) |
| Integration | `dotnet test tests\EducationHub.Tests.Integration -c Debug` | all pass (25 existing + 2 new = 27) |
| Validation E2E | `POST /api/auth/login {"email":"","password":""}` | **400** (was 500) |
| Swagger E2E | `GET /swagger/v1/swagger.json` | **200** valid OpenAPI JSON (was 500) |
| Regression tests | the new tests exist and pass | present + green |
| No regressions | isolation / refresh rotation / lockout / rate-limit / anti-enumeration / must-change tests | still pass |

## Safeguards (apply to every change)
- Inspect existing implementation before editing; reuse existing abstractions / response
  contracts / CQRS conventions / test infrastructure.
- Minimal changes only; no unrelated refactoring; preserve working security behaviours.
- No DB schema or migration changes (if one becomes necessary: explain + wait for approval).
- Preserve the read-only audit docs as historical evidence.
- Report actual commands, exit codes, test counts, changed files, unresolved issues.
- If a critical dependency / unexpected architectural issue blocks a step, stop and report.

## Decisions
- **P0-1 approach = Option A** from `docs/audit/08-remediation-plan.md`: add one
  `*CommandValidator` per command that composes the existing `*Request` validator via
  `RuleFor(x => x.Request).SetValidator(new XxxRequestValidator())`. Explicit, no reflection,
  reuses the DTO validators and their existing unit tests. (Verified `SetValidator` is
  null-safe: FluentValidation `ChildValidatorAdaptor` skips when the value is null.)
- **P0-2 approach = rename** the Users DTO `ResetPasswordRequest` → `AdminResetPasswordRequest`
  (and its validator). Surgical; keeps Swashbuckle's default schema ids for every other type
  and the JSON wire contract unchanged. The `CustomSchemaIds(type => type.FullName)` option was
  rejected because `Type.FullName` for constructed generics embeds assembly-qualified type args
  (`Result`1[[...]]`), which are invalid OpenAPI component keys.
- **P0-3 approach:** add a `catch (FluentValidation.ValidationException)` branch to
  `ExceptionHandlingMiddleware` producing the existing ProblemDetails shape with code
  `Errors.Validation`.

## Changed files (running log)
Source (production):
- `src/EducationHub.Application/Features/Auth/Validators/AuthValidators.cs` — added `LoginCommandValidator`, `RefreshTokenCommandValidator`, `ChangePasswordCommandValidator`, `LogoutCommandValidator`.
- `src/EducationHub.Application/Features/Auth/Validators/EmailFlowValidators.cs` — added `ForgotPasswordCommandValidator`, `ResetPasswordCommandValidator`, `ConfirmEmailCommandValidator`, `ResendConfirmationCommandValidator`.
- `src/EducationHub.Application/Features/Users/Validators/UserValidators.cs` — added `CreateUserCommandValidator`, `UpdateUserCommandValidator`, `ResetPasswordCommandValidator`, `AssignRolesCommandValidator`; renamed `ResetPasswordRequestValidator` → `AdminResetPasswordRequestValidator` (P0-2).
- `src/EducationHub.Application/Features/Users/DTOs/UserDtos.cs` — renamed `ResetPasswordRequest` → `AdminResetPasswordRequest` (P0-2).
- `src/EducationHub.Application/Features/Users/Commands/UserCommands.cs` — `ResetPasswordCommand` payload type updated to `AdminResetPasswordRequest` (P0-2).
- `src/EducationHub.Application/Features/Roles/Validators/RoleValidators.cs` — added `CreateRoleCommandValidator`, `UpdateRoleCommandValidator`, `AssignPermissionsCommandValidator`.
- `src/EducationHub.Application/Features/Tenancy/Validators/TenancyValidators.cs` — added `CreateTenantCommandValidator`, `UpdateTenantCommandValidator`, `CreateBranchCommandValidator`, `UpdateBranchCommandValidator`, `UpdateTenantSettingsCommandValidator`.
- `src/EducationHub.Application/Features/Academic/Validators/AcademicValidators.cs` — added `CreateAcademicYearCommandValidator`, `UpdateAcademicYearCommandValidator`, `CreateGradeLevelCommandValidator`, `UpdateGradeLevelCommandValidator`, `CreateSubjectCommandValidator`, `UpdateSubjectCommandValidator`.
- `src/EducationHub.API/Controllers/UsersController.cs` — reset-password body type updated to `AdminResetPasswordRequest` (P0-2).
- `src/EducationHub.API/Middleware/ExceptionHandlingMiddleware.cs` — added `catch (ValidationException)` → HTTP 400, code `Errors.Validation`; added `ToFieldName` helper stripping the `Request.` prefix (P0-3).

Tests:
- `tests/EducationHub.Tests.Unit/Application/CommandValidatorRegistrationTests.cs` — NEW. Resolves `IValidator<*Command>` from the real DI container and checks invalid/valid/null bodies for representative commands.
- `tests/EducationHub.Tests.Integration/AuthEndpointTests.cs` — added `Login_with_empty_credentials_is_rejected_with_400` (asserts 400 + `Errors.Validation` + per-field `Email`/`Password`, no `Request.` prefix).
- `tests/EducationHub.Tests.Integration/SwaggerDocumentTests.cs` — NEW. `GET /swagger/v1/swagger.json` → 200 with `openapi` + `paths`.
- `tests/EducationHub.Tests.Integration/TenantSecurityEndpointTests.cs` — the isolation test created a user with no roles; now that `CreateUserRequestValidator` runs it correctly rejects that, so the test supplies `roles: ["TenantAdmin"]`.

Docs:
- `docs/remediation/REMEDIATION_PROGRESS.md` (this file) — NEW.

## Commands executed (actual results)
| Command | Result |
|---|---|
| `dotnet build EducationHub.sln -c Debug --nologo` (after P0-1) | Build succeeded — 0 warnings, 0 errors |
| `dotnet build EducationHub.sln -c Debug --nologo` (after P0-2) | Build succeeded — 0 warnings, 0 errors |
| `dotnet build EducationHub.sln -c Debug --nologo` (after P0-3) | Build succeeded — 0 warnings, 0 errors |
| `dotnet build EducationHub.sln -c Debug --nologo` (final) | Build succeeded — 0 warnings, 0 errors |
| `dotnet test tests\EducationHub.Tests.Unit -c Debug` | Passed — 21/21 |
| `dotnet test tests\EducationHub.Tests.Integration -c Debug` | Passed — 27/27 (2 first-run failures were test-harness issues, fixed) |
| Live probe `POST /api/auth/login {"email":"","password":""}` | **400** `Errors.Validation`; body lists `Email`/`Password` (no `Request.` prefix) |
| Live probe `GET /swagger/v1/swagger.json` | **200**, `openapi: 3.0.4`, paths present |

## Result: all three blockers resolved and verified
- **P0-1** — 26 command validators now compose the existing DTO validators; `ValidationBehavior` resolves them, so invalid input is rejected before any handler/repository runs.
- **P0-2** — the Users DTO was renamed to `AdminResetPasswordRequest`; the OpenAPI document now generates (was HTTP 500).
- **P0-3** — `ValidationException` maps to HTTP 400 with `Errors.Validation` (was HTTP 500).

### Behaviour change to note
Enabling the validators means rules that were always intended but never executed now
apply. One surfaced in the test suite: creating a user with **no roles** is now rejected
(`CreateUserRequestValidator`: "At least one role must be supplied."). This is the
validator's pre-existing intent; the affected isolation test was updated to create a valid
user. No production behaviour outside the validators' intended rules changed.

### Not verified / open
- Full live walk-through of every endpoint was not performed; only the two contracted probes.
- No DB schema or migration changes were made (none were required).
