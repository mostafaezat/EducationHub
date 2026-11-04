# 02 · Requirements Traceability Matrix

> **Limitation.** The literal 59-section specification is not available in the repository or this
> session. The requirement areas (A–O) below are the audit taxonomy recorded at audit start, derived
> from the module ID scheme. Area titles are *(inferred)*; each row reflects what the current
> implementation actually provides, with `file:line` or command evidence. Nothing here is asserted as
> "meets spec" — only "implemented / partial / missing" against observed behaviour.

Status: `Implemented` · `Partial` · `Missing` · `Broken` · `N/A` (not applicable) · `Not Verified`.

## Summary by area
| Area | ID prefix | Area (inferred) | Backend | Frontend | Notes |
|---|---|---|---|---|---|
| A | ARCH | Solution architecture & layering | Implemented | N/A | Clean 5-project layering; no DAL leaks into Application. |
| B | TEN | Multi-tenancy & isolation | Implemented | N/A | Query filters + save-time guards; proven by tests. |
| C | AUTH | Authentication | Implemented | Missing | Strong; but input validation inert (P0-1). |
| D | AUTHZ | Authorization / roles / permissions | Implemented | Missing | One policy per permission; catalog has unimplemented entries. |
| E | STRUCT | Organisation structure (branches, settings) | Implemented | Missing | Branch + per-tenant settings CRUD. |
| F | PEOPLE | People (users, teachers, students, parents) | Partial | Missing | Only a generic user. No students/teachers/parents/enrolment. |
| G | SCHED | Scheduling (classes, timetable, attendance) | Missing | Missing | No entities/endpoints. |
| H | LEARN | Learning (curriculum, homework, exams, grades) | Partial | Missing | Only AcademicYear/GradeLevel/Subject catalog. No lessons/homework/exams/results. |
| I | FIN | Finance (fees, invoices, payments) | Partial | Missing | Settings fields only (`FeeDueDayOfMonth`, `GracePeriodDays`, `SiblingDiscountPercentage`); no fee entities/endpoints. |
| J | PORTAL | Parent/student portal | Missing | Missing | No portal endpoints or app. |
| K | NOTIF | Notifications | Partial | Missing | Settings toggles + `IEmailSender`/`LogEmailSender` (dev only). No real provider, no SMS. |
| L | FILE | File management | Partial | Missing | Storage service complete; **no HTTP endpoints** (P1-3). |
| M | UI | Frontend / SPA | Missing | Missing | No Angular app anywhere in workspace. |
| N | AUDIT | Audit & compliance | Implemented | Missing | Interceptor + read-only endpoints; some audit noise. |
| O | PERF/TEST/SAAS | Non-functional, tests, tenant lifecycle | Partial | Missing | Build/tests pass; no load/CI config; subscription fields exist, no lifecycle automation. |

## Selected detailed rows
| ID | Requirement (as assessed) | Layer | Status | Evidence | Gap / risk |
|---|---|---|---|---|---|
| ARCH-01 | Layered solution, DI per layer | Backend | Implemented | `src/*/DependencyInjection.cs`; `Program.cs:45-46` | — |
| ARCH-02 | Application has no ASP.NET dependency | Backend | Implemented | `IDiagnosticContext` comment; `HttpDiagnosticContext` in API | — |
| ARCH-03 | Central Result → HTTP mapping | Backend | Implemented | `EducationHub.API/Auth/ApiResultFilter.cs` | Convention-substring based (brittle). |
| ARCH-04 | ProblemDetails error shape | Backend | Partial | `ExceptionHandlingMiddleware.cs`; `Program.cs:139` | `ValidationException` not mapped → 500 (P1-1). |
| TEN-01 | Tenant-scoped query filter | Backend | Implemented | `Persistence/EducationHubDbContext.cs`; `TenantIsolationTests` (pass) | — |
| TEN-02 | Fail-closed without tenant | Backend | Implemented | `NullTenantContext` test (pass) | — |
| TEN-03 | Prevent tenant-id change / cross-tenant insert | Backend | Implemented | `TenantIsolationTests` `TenantId_cannot_be_changed_by_an_update`, `Insert_into_another_tenant_is_refused` (pass) | — |
| TEN-04 | Per-tenant role name uniqueness | Backend | Implemented | `Identity/TenantScopedRoleValidator.cs`; migration `DropGlobalRoleNameIndex` | — |
| TEN-05 | Header/claim tenant cross-check | Backend | Implemented | `Middleware/TenantResolutionMiddleware.cs`; test `Contradicting_tenant_header_is_rejected` (pass) | — |
| AUTH-01 | Email/password login | Backend | Implemented | `Features/Auth/Commands/LoginCommand.cs` | Validation bypass (P0-1). |
| AUTH-02 | Refresh rotation + reuse revocation | Backend | Implemented | `RefreshTokenCommand.cs`; test (pass) | — |
| AUTH-03 | Lockout policy | Backend | Implemented | `LoginCommand.cs:55-60`; test `Five_failed_logins...` (pass) | — |
| AUTH-04 | Credential rate limiting | Backend | Implemented | `Program.cs:145-177`; test 429 (pass) | — |
| AUTH-05 | Email confirm / reset / anti-enumeration | Backend | Implemented | `EmailFlowCommands.cs`; tests (pass) | — |
| AUTH-06 | Input validation → 400 | Backend | **Broken** | `DependencyInjection.cs:31-35`, `ValidationBehavior.cs:23-37`; runtime 500 | **P0-1.** |
| AUTHZ-01 | Permission-based authorization | Backend | Implemented | `Auth/PermissionAuthorization.cs`; `Program.cs:100-118` | — |
| AUTHZ-02 | Platform-admin-only endpoints | Backend | Implemented | `TenancyController.cs` `[Authorize(Roles=PlatformAdmin)]`; test (pass) | — |
| AUTHZ-03 | Permission catalog matches enforcement | Backend | Partial | `PermissionConstants`; no `Platform.*` handlers | Advertised-but-unimplemented permissions. |
| STRUCT-01 | Branch CRUD | Backend | Implemented | `TenancyController.cs` `BranchesController` | — |
| STRUCT-02 | Tenant settings CRUD | Backend | Implemented | `TenantSettingsController` | — |
| PEOPLE-01 | User CRUD + status + reset | Backend | Implemented | `UsersController.cs`, `UserCommands.cs` | 200 vs declared 201. |
| PEOPLE-02 | Role assignment / privilege boundaries | Backend | Implemented | `AssignRolesCommand.cs` | Re-verification not re-run this pass (relies on prior read + tests). |
| PEOPLE-03 | Students / teachers / parents / enrolment | Backend | Missing | — | No entities or endpoints; `RoleNames` lists role labels only. |
| SCHED-01 | Classes / timetable / attendance | Backend | Missing | — | No entities/endpoints. |
| LEARN-01 | Academic year lifecycle (single current) | Backend | Implemented | `AcademicYearCommands.cs`; `AcademicCommandSupport.ClearCurrentAsync` | — |
| LEARN-02 | Grades / subjects catalog | Backend | Implemented | `GradeLevelCommands.cs`, `SubjectCommands.cs` | — |
| LEARN-03 | Lessons / homework / exams / results | Backend | Missing | — | No entities/endpoints. |
| FIN-01 | Fee/invoice/payment entities & endpoints | Backend | Missing | — | Only settings fields. |
| NOTIF-01 | Email sender abstraction | Backend | Partial | `IEmailSender`, `LogEmailSender` | Dev-only sink; no production provider/SMS. |
| FILE-01 | File storage service | Backend | Implemented | `LocalFileStorageService.cs` | — |
| FILE-02 | Upload/download endpoints | Backend | **Missing** | No controller references `IFileStorageService` | **P1-3.** |
| UI-01 | Frontend SPA | Frontend | Missing | no `angular.json`/`package.json` in workspace | — |
| AUDIT-01 | Change auditing | Backend | Implemented | `AuditSaveChangesInterceptor.cs` | Audits all `IAuditableEntity` incl. refresh tokens/login history (noise). |
| AUDIT-02 | Read-only audit endpoints | Backend | Implemented | `AuditController.cs` | — |
| PERF-01 | Server-side pagination bounds | Backend | Implemented | `PaginationRequest.cs` (clamp 1..100) | — |
| TEST-01 | Unit + integration suites | Backend | Partial | 16 + 25 pass | No coverage of validation-failure or Swagger (see `06`). |
| TEST-02 | CI pipeline | — | Not Verified | no CI config found | — |
| SAAS-01 | Tenant subscription fields | Backend | Implemented | `Tenant.Subscription*`, `TenantSettings` | No automated renewal/expiry lifecycle. |

## Endpoint inventory (for reference)
Auth (`/api/auth`): login, refresh, logout, change-password, forgot-password, reset-password,
confirm-email, resend-confirmation, me, permissions.
Users (`/api/users`): list, get, create, update, delete, status, reset-password, assign-roles, by-role.
Roles (`/api/roles`): list, get, create, update, assign-permissions, delete.
Permissions (`/api/permissions`): catalog.
Tenants (`/api/tenants`, platform admin): list, get, create, update, delete.
Branches (`/api/branches`): list, get, create, update, delete.
Settings (`/api/settings`): get, update.
Academic years (`/api/academic-years`): list, current, for-date, get, create, update, set-current, delete.
Grades (`/api/grades`): list, get, create, update, delete.
Subjects (`/api/subjects`): list, get, create, update, delete.
Audit (`/api/audit`): logs, logs/{id}, summary, logins.
Health: `/health/live`, `/health/ready`.
