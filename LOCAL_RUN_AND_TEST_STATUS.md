?# Local Run & Test Status

EducationHub — backend API + Angular admin SPA. This document explains how to run both
locally and what was verified end-to-end.

---

## 1. Components

| Component | Path | Stack |
|-----------|------|-------|
| API | `src/EducationHub.API` (plus `Application`, `Domain`, `Infrastructure`, `Shared`) | ASP.NET Core (net10.0), EF Core, SQL Server, MediatR, FluentValidation |
| Admin SPA | `admin/` | Angular 20.3 (standalone), PrimeNG 20.4, RxJS |

The SPA is a new application added under `admin/`. The backend was modified only by three
targeted bug fixes (G1–G3 in §8) and one packaging change (§10); no architectural changes,
no new endpoints, no altered contracts. The SPA talks only to real existing endpoints.

---

## 2. Prerequisites

- .NET SDK **10.0.401** (see `global.json`)
- Node.js **24.13.0** / npm **11.6.2**
- SQL Server (LocalDB or full) with the `EducationHub_Dev` database
- Angular CLI: `@angular/cli@20` installed globally (`ng` → 20.3.x)

> `@angular/cli@22` requires Node ≥ 24.15.0 and will not run on Node 24.13.0; CLI 20 is used.

### npm cache note (environment-specific)
On this machine the default npm cache errors with `ECOMPROMISED / Lock compromised`. All npm/ng
commands were run with a clean cache:

```powershell
$env:npm_config_cache="C:\Users\MOSTAF~1\AppData\Local\Temp\opencode\npm-cache"
```

---

## 3. Quick start (one command)

From the repository root:

```powershell
.\start-dev.ps1        # or double-click / run start-dev.cmd
```

This single command: verifies prerequisites and that ports 57734/57735/4200 are free (fails
fast, naming the blocking PID), starts the backend (`dotnet run`, Development profile), waits
for `/health/ready`, starts the Angular dev server (`ng serve --host 127.0.0.1 --port 4200`),
waits for it, then opens **Swagger UI** and the **admin client** in the browser. Both run until
you press **Ctrl+C**, which tears down both process trees. Startup output is captured to log
files in `%TEMP%`; if either service fails to start, the relevant log tail is printed to the
terminal and the script exits non-zero.

| URL | What |
|-----|------|
| https://localhost:57734/swagger | Swagger UI (also http://localhost:57735/swagger) |
| http://127.0.0.1:4200 | Angular admin client (hot reload, `/api` + `/health` proxied) |
| http://localhost:57735/health/ready | Backend health |

Options: `-SkipBrowserOpen` (no browser tabs), `-StartupTimeoutSeconds <n>` (default 120).

> F5 in Visual Studio / VS Code opens Swagger automatically: the `EducationHub.API` launch
> profile now sets `"launchUrl": "swagger"`.

---

## 4. Running the backend (manual alternative)

```powershell
Start-Process dotnet -ArgumentList @('run','--project','src\EducationHub.API','-c','Debug','--launch-profile','EducationHub.API')
```

- API base URL: **http://localhost:57735**
- Health: http://localhost:57735/health/ready → `{"status":"ready"}`
- Swagger UI: **http://localhost:57735/swagger/index.html**
- OpenAPI: http://localhost:57735/swagger/v1/swagger.json
- **Single-port admin console** (after `npm run build:publish`, see §11): http://localhost:57735

The dev signing key, connection string and CORS origins come from
`src/EducationHub.API/appsettings.Development.json` and `Properties/launchSettings.json`.

---

## 5. Running the admin SPA (manual alternative)

**Option A — dev server (hot reload):**

```powershell
cd admin
$env:npm_config_cache="C:\Users\MOSTAF~1\AppData\Local\Temp\opencode\npm-cache"  # this machine only
ng serve --host 127.0.0.1 --port 4200
```

- Admin URL: **http://127.0.0.1:4200** (the host is pinned so the server never binds a
  different interface/port, and the browser always opens the intended application)

**Option B — served by the API itself (single port, production shape):**

```powershell
cd admin
npm run build:publish     # ng build + copies dist → src/EducationHub.API/wwwroot
# then start the API; the SPA is at http://localhost:57735
```

### Dev proxy
`admin/proxy.conf.json` (wired via `angular.json` `serve.options.proxyConfig`) forwards
`/api` and `/health` to `http://localhost:57735`, so the SPA runs same-origin and avoids CORS
during development. `API_BASE_URL` in `admin/src/app/core/app-config.ts` is `''` (relative) —
the same build works behind the dev proxy and behind the API on a single origin.

### Build note (CSP)
The production build disables Angular's critical-CSS inlining
(`angular.json` → `optimization.styles.inlineCritical: false`). Beasties would otherwise inject
an inline `<style>` block and an inline `onload` handler into `index.html`, which a strict CSP
must then be weakened to allow. With it disabled, `index.html` stays free of inline
scripts/handlers — see §12.

---

## 6. Credentials (development seed)

> How seeding works, its guarantees (no default passwords, no logging, no silent resets)
> and the full login rules: `docs/SEED_DATA_AND_LOCAL_LOGIN.md`.


| Account | Email | Password | Roles | Notes |
|---------|-------|----------|-------|-------|
| Platform admin | `platform.admin@educationhub.local` | `Admin1234` | PlatformAdmin | 42 permissions; manages Tenants |
| Tenant admin | `tenant.admin@educationhub.local` | `Admin1234` | TenantAdmin | 36 permissions; tenant slug `admin-ui-test` |

Password policy: min length 8, requires digit + lowercase. Enums are serialized as strings; API
responses use the `Result<T>` envelope (`succeeded/isFailure/message/errors/data`) and
`PaginatedList<T>` (`items/totalItems/pageNumber/pageSize/totalPages/hasPreviousPage/hasNextPage`).
ProblemDetails failures carry `title` = error code (e.g. `Users.EmailAlreadyExists`) and a
server-localized `detail` (via `Accept-Language`) — the SPA displays `detail` and keys off
`title`.

Credential endpoints are rate-limited per IP (`auth-credential`): repeated logins answer
**429** with `Retry-After: 60`.

---

## 7. Modules (SPA routes → API endpoints)

| SPA route | Feature | Endpoints |
|-----------|---------|-----------|
| `/login` | Sign in | `POST /api/auth/login` |
| `/auth/forgot-password` | Request reset link | `POST /api/auth/forgot-password` (always 200 — anti-enumeration) |
| `/auth/reset-password` | Set new password | `POST /api/auth/reset-password` |
| `/auth/confirm-email` | Confirm address | `POST /api/auth/confirm-email`, `POST /api/auth/resend-confirmation` |
| `/change-password` | Forced/voluntary change | `POST /api/auth/change-password` |
| `/dashboard` | Overview | `GET /api/auth/me` + module counts |
| `/tenants` | Tenants (PlatformAdmin) | `GET/POST /api/tenants`, `GET/PUT/DELETE /api/tenants/{id}` |
| `/users` | Users | `GET/POST /api/users`, `PUT/DELETE /api/users/{id}`, `POST /api/users/{id}/status`, `POST /api/users/{id}/reset-password`, `PUT /api/users/{id}/roles` |
| `/roles` | Roles | `GET/POST /api/roles`, `PUT/DELETE /api/roles/{id}`, `PUT /api/roles/{id}/permissions`, `GET /api/permissions` |
| `/permissions` | Permission catalog | `GET /api/permissions` |
| `/branches` | Branches | `GET/POST /api/branches`, `PUT/DELETE /api/branches/{id}` |
| `/settings` | Tenant settings | `GET/PUT /api/settings` (G1 fixed, see §8) |
| `/academic-years` | Academic years | `GET/POST /api/academic-years`, `PUT/DELETE /api/academic-years/{id}`, `POST .../set-current`, `GET .../current`, `GET .../for-date/{date}` |
| `/grades` | Grade levels | `GET/POST /api/grades`, `PUT/DELETE /api/grades/{id}` |
| `/subjects` | Subjects | `GET/POST /api/subjects`, `PUT/DELETE /api/subjects/{id}` |
| `/audit` | Audit & activity | `GET /api/audit/logs`, `/logs/{id}`, `/summary` (G2 fixed), `/logins` |
| `/profile` | Profile | `GET /api/auth/me` |

Access is gated by `authGuard`/`requireAuthGuard` and `permissionGuard(...)` using the exact
permission codes from `PermissionConstants` (e.g. `Users.View`, `AuditLogs.View`). Tenants are
gated by the `PlatformAdmin` role, matching `TenancyController`.

Auth recovery routes are intentionally unguarded: a user with no session must be able to reach
them (link format produced by `WebLinkBuilder`:
`{FrontendBaseUrl}/auth/reset-password?email=&token=` and
`{FrontendBaseUrl}/auth/confirm-email?email=&token=`).

---

## 8. Internationalization

- In-house service: `admin/src/app/core/i18n/` (`translate.service.ts`, `en.ts`, `ar.ts` +
  per-domain `parts/{en,ar}/*.ts`). Both languages verified key-for-key.
- Default language **Arabic** (rtl); boot sets `<html lang="ar" dir="rtl">`. Toggle on the
  login screen and in the shell; persisted in `localStorage` under `eh.language`.
- Server messages are never re-translated client-side — the API localizes `detail` from
  `Accept-Language` (sent by the auth interceptor), and the SPA displays it as-is.

---

## 9. Backend bug fixes (applied + tested)

### G1 — Settings endpoints always returned 404 `Settings.NotFound` (FIXED)
`TenantSettings` uses `TenantId` as its primary key, but `EfRepository.GetByIdAsync` matched
`EF.Property<Guid>(e, "Id") == id`. Fixed with tenant-key lookups in `TenancyQueries.cs` and
`BranchAndSettingsCommands.cs`. Test: `TenantSettingsEndpointTests`. Live verified:
`GET/PUT /api/settings` round-trip.

### G2 — `GET /api/audit/summary` returned 409 (FIXED)
`GroupBy().Select()` projection EF could not translate → rewritten client-eval-safe in
`AuditQueries.cs`. Test: `AuditSummaryEndpointTests`. Live verified: 200 with summary payload.

### G3 — `POST /api/users` with platform-scope roles returned 409 (FIXED)
Platform-scope callers (tenant = `Guid.Empty`) bypass the tenant query filter, and Identity's
stock `UserStore` role lookups matched one role row per tenant
(`InvalidOperationException` → 409). Fixed by:
- `Identity/ApplicationUserStore.cs` — tenant-scoped `IsInRole/AddToRole/RemoveFromRole`.
- `Users/Commands/UserCommandSupport.cs` — platform scope resolves only platform roles
  (`TenantId == Guid.Empty`); unknown roles still fail with `Users.UnknownRole`.
- `Infrastructure/DependencyInjection.cs` — replaces `IUserStore<ApplicationUser>` with the
  scoped store.
- `DeleteUserCommandHandler` — lifts role memberships before delete (both UserRoles FKs are
  deliberately `Restrict`; previously deleting a user with roles → 500).

Tests: `PlatformUserCreationTests` (create with `PlatformAdmin` → 200 + cleanup delete 200;
`TenantAdmin` under platform scope → 400 `Users.UnknownRole`). Live verified both flows.

### Test totals
| Suite | Result |
|-------|--------|
| `tests/EducationHub.Tests.Unit` | **30/30 passed** |
| `tests/EducationHub.Tests.Integration` | **45/45 passed** (incl. 5 seed-pipeline tests) |

> Kill the running API before `dotnet test` — it locks `EducationHub.API.dll`.

---

## 10. Auth recovery flows (verified live)

| Flow | Behaviour |
|------|-----------|
| Login with unconfirmed email | **403** title `Account.EmailNotConfirmed` → SPA shows banner + "Resend confirmation email" |
| Resend confirmation | 200 always (anti-enumeration); link/token visible in API log via `LogEmailSender` |
| `POST /auth/confirm-email` | Valid token → 200; already-confirmed → 200 (idempotent by design, `EmailFlowCommands.cs`); bogus token on unconfirmed → 400 `Auth.InvalidEmailToken` |
| `POST /auth/forgot-password` | 200 always; reset token in log |
| `POST /auth/reset-password` | Valid → 200, old password revoked; bogus token → 400 `Auth.InvalidResetToken`; policy min 8 chars + digit |
| Email delivery | **Log-only in development** (`LogEmailSender`): tokens are read from the API console/log |

---

## 11. Single-origin packaging (API serves the SPA)

- `admin/scripts/publish-to-api.mjs` + `npm run build:publish` copies
  `dist/educationhub-admin/browser` → `src/EducationHub.API/wwwroot`.
- `Program.cs`: `UseDefaultFiles()` + `UseStaticFiles()` (after security/exception/correlation
  middlewares, before auth — assets are anonymous) and a **guarded** SPA fallback registered
  only when `wwwroot/index.html` exists. Unknown `/api`, `/health`, `/swagger` paths stay real
  404s (never HTML).
- Without `wwwroot` the API behaves exactly as before (dev flow unchanged; Swagger unaffected).
- Verified same-origin: `GET /` → 200 `text/html`, deep link `/auth/reset-password?...` →
  200, hashed bundle → 200, `/api/nope` → 404, `/swagger` → 200,
  `POST /api/auth/login` + `GET /api/auth/me` + `GET /api/users` → 200.

---

## 12. Content Security Policy

`SecurityHeadersMiddleware` classifies every response by path and applies the minimum policy
that class needs:

| Path | Policy |
|------|--------|
| `/swagger` | none (Swagger UI is Development-gated and needs its own scripts/styles) |
| `/api/*`, `/health/*` | `default-src 'none'; frame-ancestors 'none'` (JSON — nothing loads) |
| everything else (SPA HTML + assets from wwwroot) | SPA policy below |

SPA policy:

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:;
font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self';
frame-ancestors 'none'
```

- **Why `style-src 'unsafe-inline'` (and only for styles):** Angular injects `<style>`
  elements at runtime for lazily loaded component styles and for the PrimeNG theme — there is
  no nonce/hash covering dynamically generated stylesheets. Scripts stay strict: no
  `unsafe-eval`, no `unsafe-inline`, no wildcards, no host origins.
- **Why no inline handler allowance:** the production build sets
  `optimization.styles.inlineCritical: false` (`angular.json`) so Beasties does **not** inject
  its critical-CSS `<style>` block / `media="print" onload="…"` swap into `index.html`.
- **No websocket in `connect-src`:** live-reload WebSockets belong to the Vite dev server's
  document (`http://127.0.0.1:4200`), which is served **without** this header; the published
  single-port build has no dev server.
- Previously the middleware sent `default-src 'none'` on **every** non-Swagger response —
  correct when the API only served JSON, but it blocked every script/style/font/fetch of the
  SPA once the API started serving `wwwroot` (that was the browser error the task began with).
- `/.well-known/appspecific/com.chrome.devtools.json` → 404: Chrome DevTools probes every
  origin it inspects; incidental, not an application request.

---

## 13. Verification performed

| Check | Result |
|-------|--------|
| `dotnet build` (solution) | **Success**, 0 warnings / 0 errors (warnings = errors on) |
| Unit tests | **30/30 passed** |
| Integration tests | **45/45 passed** |
| `ng build` (production) | **Success**, 0 errors |
| One-command start (`.\start-dev.ps1`) | **OK** — preflight, backend + health, frontend, both supervised |
| Port preflight (4200 occupied) | **OK** — fails fast, names blocking PID, exit 1 |
| Supervision (backend killed mid-run) | **OK** — failure printed with log tail, frontend tree torn down, all ports freed |
| CSP: `/`, deep links, bundles, favicon (57735 + 57734) | **SPA policy** (was `default-src 'none'`) |
| CSP: `/api/*`, `/health/*` | `default-src 'none'` (unchanged) |
| CSP: `/swagger` | none (unchanged) |
| Headless Chrome, SPA at `http://localhost:57735` | **0 CSP violations, 0 console errors**; login UI rendered (rtl/ar) |
| Headless Chrome, Swagger UI | **0 CSP violations**, swagger-ui rendered |
| Login via dev-server proxy (`127.0.0.1:4200/api/auth/login`) | **200** `succeeded:true` |
| `/health/ready` via proxy | `{"status":"ready"}` |
| `GET /api/settings` | **200** (G1 fixed; values persist across reload) |
| `GET /api/audit/summary` | **200** (G2 fixed) |
| Create platform user with `PlatformAdmin` role | **200** (G3 fixed); `DELETE` → 200 |
| Auth recovery screens (forgot/reset/confirm/resend) | OK — see §10 |
| i18n ar/en toggle + rtl/ltr | OK, key parity verified |
| Single-origin serve (`/`, deep links, assets, API, Swagger) | OK — see §11 |
| Rate limiting | Observed `429` with `Retry-After: 60` after login bursts |
| Seed pipeline (live startup) | Roles reconciled (10 tenants); admin env vars unset → warn + skip; documented admin login → 200, `PlatformAdmin`, 42 permissions |

**Not machine-verified (needs a human browser):** visual rendering quality of the stylesheets
under `style-src 'unsafe-inline'`, F5-in-Visual-Studio auto-opening Swagger
(`launchSettings.json` has no browser in this environment), and Ctrl+C teardown as typed by a
person (the equivalent stop paths — preflight failure and child-process death — are verified
above).

### Not run yet
- **TestSprite E2E** — config created (`testsprite_tests/tmp/config.json`) and a PRD +
  `code_summary.yaml` are in place, but TestSprite reports **no API key**. Create one at
  https://www.testsprite.com/dashboard/settings/apikey, set it in the MCP config, restart the
  MCP tools, then generation/execution can proceed.

---

## 14. Known limitations (accepted)

- `user.preferredLanguage` has **no update endpoint** — language choice persists client-side
  only (`eh.language`), not in the user profile.
- Email sending is log-only in development; recovery links/tokens are read from the API log.
- `POST /auth/confirm-email` on an already-confirmed account returns 200 (idempotent) rather
  than a distinct "already confirmed" message — documented behaviour, by design.
- Creating a user requires ≥ 1 role (`POST /api/users` validation); the create form mirrors
  it client-side.

---

## 15. Project layout (admin)

```
admin/
  proxy.conf.json            dev proxy → :57735
  scripts/publish-to-api.mjs build:publish → API wwwroot (single-port mode)
  src/app/
    core/
      app-config.ts          API base, storage keys, supported languages
      models.ts              all DTOs / string enums
      nav.ts                 sidebar groups (permission-filtered)
      i18n/                  TranslateService + en/ar catalogs (+ parts/)
      services/              typed API clients per module
      interceptors/          error → toasts (first), auth (Bearer + Accept-Language + refresh)
      guards/                auth / guest / permission / role
    layout/shell.component.ts
    features/
      auth/                  login, forgot-password, reset-password,
                             confirm-email, change-password
      dashboard/ profile/ permissions/ branches/
      tenants/ users/ roles/ settings/
      academic/ (academic-years, grades, subjects)
      audit/
    app.routes.ts  app.config.ts  app.ts
```

Testing checklist for a manual pass: sign in as the tenant admin, then as the platform admin;
exercise each screen's list, create, edit and delete; confirm loading / empty / success / error
states, confirmation dialogs; walk the auth recovery links (from the API log); switch ar/en.




---

## 2026-11-03 verification (Mission Phases 2.5b-9)

Run with `tools/claude/run-checks.ps1` on MOSTAFAGS (Windows, .NET SDK 10.0.401, Node 24.13.0,
local SQL Server). Logs land in `tools/claude/logs` (`SUMMARY.txt`, `*.trx`, `api-runtime.log`).

| Check | Run 1 (17:31) | Run 2 (19:49) |
|---|---|---|
| `dotnet build EducationHub.sln` | 0 warnings / 0 errors | 0 / 0 |
| `dotnet ef migrations has-pending-model-changes` | No changes | No changes |
| Unit (`EducationHub.Tests.Unit`) | 84/84 | 84/84 |
| Integration (`EducationHub.Tests.Integration`) | 98/100 (2 failed: EF roster query) | 108/109 (1 failed: foreign student enrollments 200 instead of 404) |
| Angular `ng build` / `npm run test:ci` | green / 41/41 | green / 41/41 |
| API runtime (`/health/ready`, `/swagger/v1/swagger.json`) | 200 / 200, 5 migrations applied, no ERR in log | 200 / 200, `AddFinanceConcurrencyTokens` applied |

Both failures were real defects and are fixed (see `docs/IMPLEMENTATION_PLAN.md`, Mission Phase 8).
The fix for run 2's failure has been built and unit-tested; the integration suite needs one more run
on the dev machine to confirm 109/109.

`dotnet ef migrations list` could not reach SQL Server from the design-time tool (Named Pipes);
the running API did reach it, so this is a design-time connection-string difference only.
