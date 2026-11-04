# EducationHub — Arabic/English Localization Plan

> New workstream (audit BL-004 / P1-2). Separate from the completed P0 remediation
> (`REMEDIATION_PROGRESS.md`). Repository: `D:\test API\NEWApp\New folder`.
> Started: 2026-10-14.

## Goal
Deliver real localization infrastructure so the API returns Arabic (default) or English
messages per request, and route user-facing strings through `IStringLocalizer`.

## Approved decisions
1. **Resource storage = JSON** at `src/EducationHub.API/Localization/Resources/{ar,en}.json`
   (matches the `LocalizationKeys` doc comment). Flat dotted keys (e.g. `"Auth.InvalidCredentials"`)
   so the JSON key *is* the lookup key. Files are embedded resources so tests/publish are robust.
2. **Scope = Application + API messages**: validator messages (Application), handler
   `BusinessRuleException`/`Result.Failure` messages, and API middleware/filter/rate-limiter
   messages.
3. **Culture selection = Accept-Language → tenant `PreferredLanguage` → Arabic.** This logic
   already exists in `TenantResolutionMiddleware.ResolveLanguage`; it will be extracted and
   reused rather than duplicated.

## Current state (verified)
- `LocalizationKeys` + `SupportedLanguages` exist; nothing else. No resources, no
  `AddLocalization`/`UseRequestLocalization`, no `IStringLocalizer` usage.
- Application already references `Microsoft.Extensions.Localization.Abstractions`.
- `Microsoft.Extensions.Localization` + `Microsoft.AspNetCore.Localization` ship in the
  `Microsoft.AspNetCore.App` framework (10.0.12) — **no package changes needed**.
- `BusinessRuleException` contract: `Code` is stable/machine-readable and must NOT change;
  only the human-readable message is localized. Boundary already returns `title = code`.
- Contract: ProblemDetails `title`/`code` stay stable; `detail` and `errors[].message` are localized.

## Design
### Localizer
- `EducationHub.Shared/Common/Localization/LocalizationResource.cs` — marker type for
  `IStringLocalizer<LocalizationResource>`.
- API `Localization/JsonStringLocalizerFactory` + `JsonStringLocalizer` + `JsonResourceStore`
  read embedded `*.{culture}.json`, resolve culture from `CultureInfo.CurrentUICulture`
  (normalized to `ar`/`en`), fall back to default (`ar`), then to the key itself.
- Registered by overriding `IStringLocalizerFactory` right after `AddLocalization()`.

### Culture
- `Localization/RequestLanguageCultureProvider : IRequestCultureProvider` calls a shared
  `RequestLanguageResolver` (extracted from `TenantResolutionMiddleware`).
- `app.UseRequestLocalization(...)` placed after `UseAuthentication` and before
  `TenantResolutionMiddleware`, using only our provider (so precedence is exact).
  Supported cultures `ar-EG`/`en-US`; default `ar-EG`.

### Validators (Application)
- Base `Common/Validation/LocalizedValidator<T> : AbstractValidator<T>` with an **optional**
  `IStringLocalizer<LocalizationResource>?` ctor param (so existing direct `new()` in unit
  tests still compiles; DI injects the real one). `Message(key)` returns the localized string.
- Every `*RequestValidator` and `*CommandValidator` derives from it; every `.WithMessage("...")`
  becomes `.WithMessage(_ => Message(LocalizationKeys.<Key>))`.

### Boundary (API)
- `ExceptionHandlingMiddleware`, `ApiResultFilter`, `MustChangePasswordMiddleware`,
  `TenantResolutionMiddleware` problem writers, and the rate-limiter `OnRejected` resolve
  `IStringLocalizer<LocalizationResource>` and localize `detail` by code, falling back to the
  existing English text when a key is absent.

### Handler messages
- Localized at the boundary **by code** (keys mirror the runtime codes) with English fallback.
  Codes that carry several distinct messages (e.g. `Auth.InvalidRefreshToken`) intentionally keep
  the English fallback so no reason-specific text is lost. Documented as a known limitation.

## Verification criteria
| Check | Command | Pass |
|---|---|---|
| Build | `dotnet build EducationHub.sln -c Debug --nologo` | 0/0 |
| Unit | `dotnet test tests\EducationHub.Tests.Unit -c Debug` | all pass (incl. new localizer tests) |
| Integration | `dotnet test tests\EducationHub.Tests.Integration -c Debug` | all pass (incl. new culture tests) |
| Resource completeness | unit test asserting every `LocalizationKeys` string exists in `ar.json` and `en.json` | pass |
| Ar default | `POST /api/auth/login` invalid, no Accept-Language | Arabic `title` code unchanged, Arabic `detail`/messages |
| En | same with `Accept-Language: en` | English messages |
| No regressions | tenant isolation / refresh / lockout / rate-limit / anti-enumeration | pass |

## Safeguards
- Preserve `Code`/`title` values exactly (frontend branches on them).
- Minimal churn; reuse existing abstractions; keep English JSON values byte-identical to the
  current literals (no wording drift).
- No DB/migration changes. Preserve the frozen audit docs.

## Task board
| # | Task | Status |
|---|---|---|
| L0 | Write this plan | COMPLETED |
| L1 | Infrastructure: marker, JSON store/localizer/factory, culture provider, pipeline, resources for codes | COMPLETED |
| L2 | Validators → `LocalizedValidator` + keys | COMPLETED |
| L3 | Boundary mappers (middleware, filter, rate limiter) | COMPLETED |
| L4 | Unit + integration tests; resource-completeness test | COMPLETED |
| L5 | Build + full test run + live probe (ar/en); update docs | COMPLETED |

## Results (2026-10-14)

### Verification
| Check | Result |
|---|---|
| `dotnet build EducationHub.sln -c Debug` | **0 warning / 0 error** |
| Unit tests | **30/30 pass** (21 pre-existing + 9 new localization) |
| Integration tests | **32/32 pass** (27 pre-existing + 5 new culture) |
| Resource completeness | every `LocalizationKeys` value present and non-empty in both `ar.json` and `en.json` |
| Live probe `POST /api/auth/login {"email":"","password":""}` | no header → **400** Arabic `detail` + Arabic field messages; `Accept-Language: ar` → Arabic; `Accept-Language: en` → byte-identical English. `title`/`code` = `Errors.Validation` in all three. |

### What was delivered
- **Infra** (`src/EducationHub.API/Localization/`): `JsonResourceStore`, `JsonStringLocalizer`,
  `JsonStringLocalizerFactory`, `RequestLanguageCultureProvider`, `RequestLanguageResolver`,
  `BoundaryMessages`, `LocalizationSetup`; embedded catalogs `Resources/{ar,en}.json`.
- **Pipeline**: `AddApiLocalization()` + `UseRequestLocalization(...)` inserted after
  `UseAuthentication` and before `TenantResolutionMiddleware`. The private
  `ResolveLanguage`/`TryGetSupportedAcceptLanguage` in `TenantResolutionMiddleware` were removed
  in favour of the shared `RequestLanguageResolver` (single precedence rule).
- **Validators** (`src/EducationHub.Application/Common/Validation/LocalizedValidator.cs`):
  6 validator files, ~80 messages keyed. Ctor param `IStringLocalizer<LocalizationResource>?` is
  optional so direct construction (unit tests) keeps the literal; DI injects the JSON localizer.
- **Boundary**: `ExceptionHandlingMiddleware`, `ApiResultFilter`, `MustChangePasswordMiddleware`,
  `TenantResolutionMiddleware`, and the rate-limiter `OnRejected` now localize their `detail`.

### Design notes / deviations
1. **Boundary resolves the catalog directly** (`BoundaryMessages`), not via `IStringLocalizer`.
   `ExceptionHandlingMiddleware` sits *outside* `UseRequestLocalization`, so when an exception
   propagates the localization middleware's `finally` has already restored
   `CultureInfo.CurrentUICulture`; and the rate limiter runs before authentication (no claim to
   set a culture from). Reading `JsonResourceStore` for the request language is correct in both
   places without moving them in the pipeline.
2. **Codes/titles preserved.** Only `detail`/`errors[].message` are localized; `title`, `code`
   and the error-code strings are unchanged. English catalog values are byte-identical to the
   literals they replaced (validator literals were copied verbatim from the six validator files;
   boundary literals from the middleware/filter/limiter).
3. **Rate-limit title kept literal** (`"Too many attempts"`) to honour the "preserve title"
   safeguard; only its `detail` is localized. `Errors.TooManyAttemptsTitle` remains a catalog key
   for the frontend to consume.

### Known limitation (follow-up, not blocking)
- **Handler `BusinessRuleException` / `Result.Failure` messages are not translated yet.** The
  boundary already looks them up by their error code and falls back to the existing English
  `message`, but no handler-code entries were added to the catalogs because doing so safely
  requires each code's exact literal (to avoid wording drift and to exclude codes that carry
  several distinct messages, e.g. `Auth.InvalidRefreshToken`, `Errors.NotFound`). Adding
  `"<Code>": "<exact message>"` entries to both catalogs for the fixed-message, non-interpolated
  codes is a no-code-change follow-up. This keeps current English output byte-identical.

### Files changed
- Added: `src/EducationHub.Shared/Common/Localization/LocalizationResource.cs`.
- Extended: `LocalizationKeys.cs` (~80 validation keys + boundary keys).
- Added: `src/EducationHub.API/Localization/*` (+ `Resources/{ar,en}.json`); `EducationHub.API.csproj` embeds them.
- Added: `src/EducationHub.Application/Common/Validation/LocalizedValidator.cs`.
- Converted: the six `*Validators.cs` files to `LocalizedValidator<T>` + keys.
- Localized: `ExceptionHandlingMiddleware`, `ApiResultFilter`, `MustChangePasswordMiddleware`,
  `TenantResolutionMiddleware`, `Program.cs` (rate limiter + registration/pipeline).
- Tests: `tests/EducationHub.Tests.Unit/Localization/LocalizationResourceTests.cs`,
  `tests/EducationHub.Tests.Integration/LocalizationEndpointTests.cs`;
  `EducationHub.Tests.Unit.csproj` now references the API project.
