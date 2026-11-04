# 07 · Backlog (P0–P3)

Prioritized from `05-production-blockers.md`. "Effort" is a rough estimate for a single developer.

## P0 — Must fix before any production or public use
| ID | Item | Evidence | Effort |
|---|---|---|---|
| BL-001 | **Wire FluentValidation into the MediatR pipeline** so request bodies are actually validated. Validators target DTOs; requests are commands. | `DependencyInjection.cs:31-35`, `ValidationBehavior.cs:23-37`; runtime 500 on invalid login | S–M |
| BL-002 | **Fix Swagger document generation** — duplicate schema id `$ResetPasswordRequest` (Auth vs Users). Add `CustomSchemaIds` or rename a DTO. | `/swagger` → 500; `AuthDtos.cs:53`, `UserDtos.cs:66`, `Program.cs:183-210` | XS |

## P1 — High priority
| ID | Item | Evidence | Effort |
|---|---|---|---|
| BL-003 | **Map `FluentValidation.ValidationException` → 400** in `ExceptionHandlingMiddleware` (fields in `errors`). Prevents 500 once BL-001 is fixed. | `ValidationBehavior.cs:57`; `ExceptionHandlingMiddleware.cs:29-107` | XS |
| BL-004 | **Implement localization** (resource files + `UseRequestLocalization`) or remove the claim. | no `Localization/` folder; keys exist only | M |
| BL-005 | **Expose file upload/download endpoints** for `IFileStorageService`. | service implemented, no controller | S–M |
| BL-006 | **Add regression tests**: 400 on invalid body (≥1 endpoint) and `/swagger/v1/swagger.json` == 200. | `06-verification-results.md` §5 | S |

## P2 — Medium
| ID | Item | Evidence | Effort |
|---|---|---|---|
| BL-007 | Reconcile `PermissionConstants` with implemented endpoints (remove/implement `Platform.*`, `Dashboard.View`). | catalog vs controllers | S |
| BL-008 | Return 201 for creates or change the declared `ProducesResponseType`. | `UsersController.cs:51/:56` (+ Roles/Tenants/Branches/Academic) | S |
| BL-009 | Populate `UserProfileDto.TenantName` (currently always null). | `LoginCommand.cs:216` | XS |
| BL-010 | Make `TenantId` nullability consistent (Guid.Empty sentinel vs docs; DTOs treat it differently). | `RoleDtos.cs:29` vs `AuthDtos.cs:31` | S |
| BL-011 | Friendly conflict on cross-tenant duplicate email at user creation (tenant-filtered pre-check can't see it). | `IX_Users_NormalizedEmail`; `CreateUserCommand` | S |
| BL-012 | Reduce audit noise: exclude `RefreshToken`/`LoginHistory` from change-audit. | `AuditSaveChangesInterceptor.cs` | S |
| BL-013 | Harden `ApiResultFilter` HTTP mapping (substring convention is brittle). | `Auth/ApiResultFilter.cs` | S |

## P3 — Low / hardening
| ID | Item | Evidence |
|---|---|---|
| BL-014 | Add `launchUrl: "swagger"` so the dev launch opens Swagger. | `launchSettings.json` |
| BL-015 | Add a README and CI workflow. | repo has none |
| BL-016 | `GradeLevel` unique index `(TenantId, Order)` has no `IsDeleted` filter. | migration `InitialSchema.cs:553-557` |
| BL-017 | Review empty-page-past-end and sort-order handling in pagination. | `PaginationExtensions.cs` |
| BL-018 | Decide on `SortOrder` enum naming collision risk (`Shared.Common.Models.SortOrder` vs `Subject.SortOrder` int) — cosmetic. | `Shared/Common/Models/SortOrder.cs` |

## Functional gaps (product scope, not defects)
| ID | Item | Note |
|---|---|---|
| GAP-01 | Students / teachers / parents / enrolment (Phase 3) | Not started. |
| GAP-02 | Classes / timetable / attendance (Phase 4) | Not started. |
| GAP-03 | Lessons / homework / exams / results (Phase 5) | Not started. |
| GAP-04 | Fees / invoices / payments (Phase 6) | Only settings fields exist. |
| GAP-05 | Parent/student portal + frontend SPA (Phase 8) | Missing entirely. |
| GAP-06 | Production email provider + SMS (Phase 7) | Only a dev logging sender. |
