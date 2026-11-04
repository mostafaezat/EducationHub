# 08 · Remediation Plan (recommended, not applied)

> This audit is read-only. Nothing below has been applied. Order is chosen by risk × effort.

## Step 1 — Restore request validation (BL-001) — the highest-value fix
**Goal:** invalid request bodies return 400 with field errors instead of reaching handlers.

**Option A (recommended, smallest):** move validation onto the request types that MediatR sees.
- Change each validator's generic target from the DTO to the command, validating `x.Request`:
  ```csharp
  // AuthValidators.cs
  public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
  {
      public LoginCommandValidator()
          => RuleFor(x => x.Request).SetValidator(new LoginRequestValidator());
  }
  ```
  (Keep the existing DTO validators and compose them, so the direct unit tests keep working.)
- Repeat for every `*Command` in `02-requirements-traceability.md` (Auth, Users, Roles, Tenancy,
  Academic). Queries can stay unvalidated unless desired.

**Option B:** keep DTO validators and make `ValidationBehavior` resolve the inner `Request` — e.g.
detect a public `Request` property on `TRequest` and validate it with the matching
`IValidator<TRequest.Request>`. More general, more moving parts.

**Verification after the change:** `POST /api/auth/login {"email":"","password":""}` must return
**400**, then re-run unit + integration tests.

## Step 2 — Map validation failures to 400 (BL-003)
Add a branch to `ExceptionHandlingMiddleware.InvokeAsync` **before** the generic `catch`:
```csharp
catch (FluentValidation.ValidationException ex)
{
    var errors = ex.Errors
        .Select(e => new Error(e.PropertyName, e.ErrorMessage))
        .ToList();
    await WriteAsync(context, StatusCodes.Status400BadRequest,
        "Validation.Failed", "One or more validation errors occurred.", errors);
}
```
This makes the behavior's own comment (`ValidationBehavior.cs:55-57`) true. Do Step 2 with or just
after Step 1.

## Step 3 — Fix Swagger generation (BL-002)
In `Program.cs`, inside `AddSwaggerGen` (around `:183-210`):
```csharp
options.CustomSchemaIds(type => type.FullName!.Replace('+', '.'));
```
(Or rename one `ResetPasswordRequest` — e.g. the Users one to `AdminResetPasswordRequest`.)
**Verification:** `GET /swagger/v1/swagger.json` == 200; run the app in Development and load the UI.
Optionally add `"launchUrl": "swagger"` in `launchSettings.json`.

## Step 4 — Lock in with regression tests (BL-006)
- Integration: `POST /api/auth/login` with an invalid body asserts `400` and a `Validation.Failed`
  (or per-field) problem code.
- Integration: `GET /swagger/v1/swagger.json` asserts `200`.
Both would have caught the two P0s; add before touching anything else so the fixes are pinned.

## Step 5 — High-priority gaps
- **Localization (BL-004):** decide implement-or-remove. If implementing, add `.resx` files, call
  `AddLocalization`/`UseRequestLocalization`, and route user-facing strings through `IStringLocalizer`.
- **File endpoints (BL-005):** add a controller for upload/download/delete backed by
  `IFileStorageService`, with size/type limits and permission policies.

## Step 6 — Medium cleanups
BL-007…BL-013 (permission catalog, status codes, `TenantName`, TenantId nullability, duplicate-email
conflict, audit noise, result-filter mapping). Each is small and independent.

## Step 7 — Product phases
Phases 3–6 and 8 (`07-backlog.md` GAP-01…GAP-06) are net-new scope, not remediation, and should be
planned separately.

## Suggested acceptance checks
| Check | Command / probe |
|---|---|
| Build clean | `dotnet build EducationHub.sln -c Debug` → 0/0 |
| Unit | `dotnet test tests\EducationHub.Tests.Unit -c Debug` → all pass |
| Integration | `dotnet test tests\EducationHub.Tests.Integration -c Debug` → all pass |
| Validation | invalid login body → **400** |
| Swagger | `GET /swagger/v1/swagger.json` → **200** |
| Regression tests present | the two tests from Step 4 exist and pass |
