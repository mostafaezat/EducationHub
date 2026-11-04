# 03 · Phase Gap Analysis (Phase 0–8)

> Phase definitions are those referenced by the requester's plan and by code comments in the repo
> (e.g. "the Phase 0 authentication fixes", "P0-10"). This file records what the current tree contains
> against those phases, based on observed code/tests. Percentages exclude `N/A` items and do not count
> `Not Verified` items as done.

## Phase status
| Phase | Theme | Status | Evidence |
|---|---|---|---|
| 0 | Auth hardening + provisioning foundations | Implemented | `AuthEndpointTests` (pass); `LoginCommand`, `RefreshTokenCommand`, `TenantRoleProvisioner`, `DataSeeder`. |
| 1 | Core tenancy (tenants/branches/settings/users/roles) | Implemented | Tenancy/Users/Roles controllers + handlers; isolation tests pass. Localization sub-item **Partial** (P1-2). |
| 2 | Academic structure (years/grades/subjects) | Implemented | `AcademicController.cs` + Academic feature slice. |
| 3 | People & enrolment (students/teachers/parents) | **Missing** | No domain entities or endpoints. |
| 4 | Scheduling (classes/timetable/attendance) | **Missing** | No domain entities or endpoints. |
| 5 | Learning (lessons/homework/exams/results) | **Missing** | No domain entities or endpoints. |
| 6 | Finance (fees/invoices/payments) | **Missing** | Settings fields only. |
| 7 | Notifications & files | Partial | File storage service done, **no endpoints** (P1-3); email dev sink only. |
| 8 | Frontend / portal | **Missing** | No Angular app in workspace. |

## Interpretation
The delivered backend covers **Phases 0–2** plus parts of **7** (file storage service, notification
settings). **Phases 3–6 and 8 are not started** in terms of domain entities, endpoints, or UI. This is
consistent with the repository name and content being a tenancy/auth/academic-core slice rather than a
full tutoring product.

## Cross-cutting items
| Item | Status | Evidence |
|---|---|---|
| Input validation | **Broken** | P0-1 (`05-production-blockers.md`). |
| API documentation (Swagger) | **Broken** | P0-2. |
| Localization | Partial | Keys/types only; no resources/pipeline (P1-2). |
| Auditing | Implemented | `AuditSaveChangesInterceptor` + audit endpoints. |
| Tests | Partial | Suites pass but miss validation & Swagger paths (`06-verification-results.md`). |
| CI/CD | Not Verified | No pipeline config found in the workspace. |
| Frontend | Missing | No SPA project. |
