# 05 · Production Blockers & High-Priority Defects

Every item below was verified against current source and, for the top two, against a live run of the
API. Line references are to the current working tree.

---

## P0-1 — Request validation never executes (FluentValidation is inert)

**Severity:** P0 (every write endpoint accepts unvalidated input; malformed input can 500)
**Status:** Confirmed (static + runtime)

### What is wrong
All validators target **DTO/Request** types, but the MediatR requests are **command wrappers**. The
pipeline behavior resolves validators for the *request* type, so it finds none.

- `src/EducationHub.Application/DependencyInjection.cs:26-35` — `AddMediatR(... AddOpenBehavior(typeof(ValidationBehavior<,>)))` and `AddValidatorsFromAssembly(assembly, includeInternalTypes: true)`.
- `src/EducationHub.Application/Common/Behaviors/ValidationBehavior.cs:23-37` — injects
  `IEnumerable<IValidator<TRequest>> validators` and returns `next()` immediately when
  `!validators.Any()`.
- `src/EducationHub.Application/Features/Auth/Commands/LoginCommand.cs:14` — request type is
  `LoginCommand(LoginRequest Request)`.
- `src/EducationHub.Application/Features/Auth/Validators/AuthValidators.cs:5` — validator is
  `AbstractValidator<DTOs.LoginRequest>`.

There is no `IValidator<LoginCommand>` (and likewise for every other command). The same pattern
applies to all 26 validators: each targets a `*Request` DTO, while every MediatR request is a
`*Command`/`*Query` record (see `02-requirements-traceability.md` for the full list).

Result: `validators.Any()` is always `false` → **no request body is validated anywhere**.

### Runtime proof
```
POST /api/auth/login   body {"email":"","password":""}
→ HTTP 500
→ {"title":"Errors.Internal","status":500,
   "detail":"System.ArgumentException: The value cannot be an empty string ... (Parameter 'normalizedEmail')
             ... AuthenticationRepository.GetByEmailAsync ... AuthenticationRepository.cs:line 32 ..."}
```
If `LoginRequestValidator` had run, this would have been a 400 (`Email is required.`).

### Impact
- Invalid payloads reach handlers. Some handlers defensively normalise or return `Result` failures;
  others throw and surface as 500 (as above).
- Uploaded "400 validation" contract promised by the controllers does not exist.
- `CreateTenantRequestValidator`, `CreateUserRequestValidator`, etc. give a false sense of coverage:
  they pass their unit tests because those tests call the validator directly, never through MediatR.

### Fix direction
Either (a) make the request records themselves the validated types and move validators to
`AbstractValidator<LoginCommand>`, or (b) keep DTO validators and validate `command.Request` inside
the behavior (e.g. a behavior matching commands that expose a `Request` property, or an
`IRequestValidator` adapter). (a) is the smaller change. See `08-remediation-plan.md`.

---

## P0-2 — Swagger document generation fails (`/swagger` → 500)

**Severity:** P0 (for developer/consumer experience; API still works otherwise)
**Status:** Confirmed (runtime)

### What is wrong
Two DTOs share the simple name `ResetPasswordRequest`, in different namespaces:
- `src/EducationHub.Application/Features/Auth/DTOs/AuthDtos.cs:53`
- `src/EducationHub.Application/Features/Users/DTOs/UserDtos.cs:66`

Swashbuckle derives a schema id from the type's simple name, yielding `$ResetPasswordRequest` for
both, and throws `InvalidOperationException` while generating the operation for
`UsersController.ResetPassword`.

### Runtime proof
```
GET /swagger/v1/swagger.json
→ HTTP 500
→ Swashbuckle.AspNetCore.SwaggerGen.SwaggerGeneratorException:
   Failed to generate Operation for action - UsersController.ResetPassword ... See inner exception
   ---> Failed to generate schema for type - ...Users.DTOs.ResetPasswordRequest
   ---> System.InvalidOperationException: Can't use schemaId "$ResetPasswordRequest" for type
        "...Users.DTOs.ResetPasswordRequest". The same schemaId is already used for type
        "...Auth.DTOs.ResetPasswordRequest"
```

### Note on the earlier (incorrect) diagnosis
The prior working summary blamed `new OpenApiSecuritySchemeReference("Bearer")` without a host
document. The current code at `src/EducationHub.API/Program.cs:206-209` already uses the factory form,
and the actual exception is the schema-id collision above. The security-scheme code is not the cause.

### Fix direction
Add `options.CustomSchemaIds(t => t.FullName)` (or `t => t.FullName!.Replace('+','.')`) in the
`AddSwaggerGen` block at `Program.cs:183-210`. Renaming one DTO to `AdminResetPasswordRequest` also
works and is arguably clearer.

---

## P1-1 — `FluentValidation.ValidationException` is unhandled → would be a 500

**Severity:** P1 (latent; becomes live the moment P0-1 is fixed a certain way)
**Status:** Confirmed (static)

- `ValidationBehavior.cs:57` throws `FluentValidation.ValidationException(failures)`.
- `src/EducationHub.Application/Common/Behaviors/LoggingBehavior.cs:94-128` (`BusinessRuleExceptionBehavior`)
  catches only `BusinessRuleException` and rethrows everything else.
- `src/EducationHub.API/Middleware/ExceptionHandlingMiddleware.cs:29-107` special-cases
  `BusinessRuleException` (400), `UnauthorizedAccessException` (403), `KeyNotFoundException` (404),
  `InvalidOperationException` (409), `OperationCanceledException`; everything else → generic
  `catch (Exception)` → **500** (and, in Development, the full `ex.ToString()`).

`ValidationException` derives from `Exception`, so it lands in the 500 branch. The comment in
`ValidationBehavior.cs:55-57` claims the exception middleware produces "the same 400 shape" — it does
not. There is no `IExceptionHandler`, no `app.UseExceptionHandler`, and only `AddProblemDetails()`
(`Program.cs:139`).

### Fix direction
Catch `ValidationException` in `ExceptionHandlingMiddleware` and map to 400 with per-field `errors`
(which the `WriteAsync` helper already supports).

---

## P1-2 — Localization declared but not implemented

**Severity:** P1 (Arabic-default requirement)
**Status:** Confirmed (absence of artifacts)

- `src/EducationHub.Shared/Common/Localization/LocalizationKeys.cs` and `SupportedLanguages.cs` exist
  and advertise `ar`/`en`.
- There is **no** `Localization/` folder, **no** `.resx`/JSON resource files, **no**
  `AddLocalization`/`UseRequestLocalization`, and **no** `IStringLocalizer` usage anywhere in `src`.
- `Directory.Packages.props` references `Microsoft.Extensions.Localization` but nothing consumes it.

All user-facing messages are hard-coded English strings (e.g. validators, `ExceptionHandlingMiddleware`).

---

## P1-3 — File storage has no HTTP surface

**Severity:** P1 (spec area "File management")
**Status:** Confirmed

`IFileStorageService` (`src/EducationHub.Application/Common/Interfaces/IFileStorageService.cs`) and
`LocalFileStorageService` (`src/EducationHub.Infrastructure/Services/LocalFileStorageService.cs`) are
fully implemented, `FileStorageOptions` is bound (`RootPath=App_Data/Files`,
`MaxTotalSizeBytes=10737418240`), and `StoredFile` is persisted — but **no controller** exposes
upload/download. Grep for controllers shows only Auth, Users, Roles, Permissions, Tenants, Branches,
Settings, AcademicYears, Grades, Subjects, Audit.

---

## P2 — Medium findings
1. **Permission catalog advertises unimplemented permissions.** `PermissionConstants` includes
   `Platform.*` and `Dashboard.View`, but no controller/handler backs them; the policies exist but
   nothing requires them. (`src/EducationHub.Shared/Common/Constants/PermissionConstants.cs`.)
2. **201 declared, 200 returned.** Controllers return `Ok(...)` for create operations while
   `[ProducesResponseType(...Status201Created)]` is declared — e.g. `UsersController.cs:51` vs `:56`;
   same in Roles, Tenants, Branches, AcademicYears, Grades, Subjects.
3. **`UserProfileDto.TenantName` always null.** `LoginCommand.MapProfile` passes `null`
   (`src/EducationHub.Application/Features/Auth/Commands/LoginCommand.cs:216`), so the field is
   useless to the client.
4. **TenantId nullability is inconsistent with its own docs.** `ApplicationUser.TenantId` and
   `ApplicationRole.TenantId` are non-nullable `Guid` ("null for platform" in comments) using
   `Guid.Empty`. `RoleDto.FromEntity` maps `Guid.Empty → null`
   (`RoleDtos.cs:29`), but `UserProfileDto.TenantId` is non-nullable (`AuthDtos.cs:31`).
5. **Global unique email index.** `IX_Users_NormalizedEmail` is unique platform-wide
   (`Persistence/Migrations/20261005150442_InitialSchema.cs:674-678`). Intended ("email unique across
   the platform"), but `UserManager.FindByEmailAsync` is tenant-filtered in a tenant context, so a
   cross-tenant duplicate pre-check in user creation cannot see the conflict and the failure surfaces
   as a generic Identity error rather than a friendly 409. (The tenant-provisioning path runs under
   platform scope, so its cross-tenant check works — proven by a passing test.)

---

## Verified-correct behaviours (no action)
- Tenant isolation: global query filters + save-time `TenantId`-change and cross-tenant-insert guards;
  fail-closed when no tenant context. Covered by `TenantIsolationTests` (passing).
- Refresh-token rotation and reuse detection (family revocation) — `AuthEndpointTests` (passing).
- Lockout after repeated failures, credential rate limiting (429 + `Retry-After`) — passing tests.
- Anti-enumeration on `forgot-password`/`resend-confirmation` — passing tests.
- Must-change-password allow-list gate — passing test.
- Startup fails fast on a missing/short/placeholder signing key (`Program.cs:60-64`).
