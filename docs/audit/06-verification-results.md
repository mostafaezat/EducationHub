# 06 · Verification Results (actual)

All results below are the **observed** output of commands run during this audit on 2026-10-14.
Nothing is marked passed unless executed.

## Environment
- OS: Windows (`win32`), shell: PowerShell 5.1.
- .NET SDK: 10.0.401 (`global.json`), target `net10.0`.
- Database: local SQL Server (`localhost`, Trusted_Connection) — required by integration tests.
- Solution: `EducationHub.sln` (5 src + 2 test projects).

## 1. Build
```
dotnet build EducationHub.sln -c Debug --nologo
```
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:14.53
```
**Result: PASS.** Note `TreatWarningsAsErrors=true` (`Directory.Build.props`), so the 0-warning result
is enforced, not incidental.

## 2. Unit tests
```
dotnet test tests\EducationHub.Tests.Unit -c Debug --no-build --nologo
```
```
Passed!  - Failed: 0, Passed: 16, Skipped: 0, Total: 16, Duration: 767 ms
```
**Result: PASS (16/16).**
Coverage noted: `ResultFactoryTests` (6), `CreateTenantRequestValidatorTests` (10).
Caveat: `CreateTenantRequestValidatorTests` invokes the validator **directly**, so it does not detect
P0-1 (validators not wired into the MediatR pipeline).

## 3. Integration tests
```
dotnet test tests\EducationHub.Tests.Integration -c Debug --no-build --nologo
```
```
Passed!  - Failed: 0, Passed: 25, Skipped: 0, Total: 25, Duration: 54 s
```
**Result: PASS (25/25).** These run against a real, freshly-created SQL Server database (migrated, not
in-memory), covering: auth endpoints (rotation, logout, lockout, rate limit, change password, email
confirmation/reset, must-change gate), tenant provisioning (transaction rollback, cross-tenant email
conflict), tenant isolation (query filter, save-time guards, fail-closed), tenant security endpoints
(cross-tenant read→404, contradicting header→403, suspended tenant→409, no pending migrations).
Caveat: **no integration test submits a body that fails FluentValidation**, which is why P0-1 passes
the suite.

## 4. Runtime probes (live host)
Started with the dev launch profile (Development, `EducationHub_Dev`, signing key from
`launchSettings.json`); polled `GET /health/ready` until 200 (ready after ~8 s).

| Probe | Request | Observed |
|---|---|---|
| Invalid login body | `POST /api/auth/login` `{"email":"","password":""}` | **HTTP 500** (expected 400) |
| Swagger document | `GET /swagger/v1/swagger.json` | **HTTP 500** (expected 200) |

Invalid-login response body:
```json
{"title":"Errors.Internal","status":500,
 "detail":"System.ArgumentException: The value cannot be an empty string or composed entirely of whitespace. (Parameter 'normalizedEmail')\r\n   at ... AuthenticationRepository.GetByEmailAsync(String normalizedEmail, CancellationToken) in ...AuthenticationRepository.cs:line 32 ..."}
```
Swagger failure (from the app log):
```
Swashbuckle.AspNetCore.SwaggerGen.SwaggerGeneratorException: Failed to generate Operation for action
- EducationHub.API.Controllers.UsersController.ResetPassword ... See inner exception
 ---> System.InvalidOperationException: Can't use schemaId "$ResetPasswordRequest" for type
      "...Users.DTOs.ResetPasswordRequest". The same schemaId is already used for type
      "...Auth.DTOs.ResetPasswordRequest"
```
Startup log also confirmed **"No migrations were applied. The database is already up to date."** — the
runtime probe did not alter the development schema. The host process was stopped after probing.

## 5. Test gap analysis
| Layer | Present? | Catches P0-1? | Catches P0-2? |
|---|---|---|---|
| Unit tests | Yes (16) | No (direct validator calls) | No |
| Integration — HTTP | Yes (25) | No (all bodies well-formed) | No (Swagger never requested) |
| Integration — DB/isolation | Yes | No | No |
| Runtime smoke | No (ad-hoc only) | — | — |

**Recommendation:** add (a) a 400-on-invalid-body test for at least one endpoint, and (b) a smoke test
that fetches `/swagger/v1/swagger.json` and asserts 200. Both are cheap and would have caught the two
P0s.
