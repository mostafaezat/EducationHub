import { Routes } from '@angular/router';
import { ShellComponent } from './layout/shell.component';
import { authGuard, guestGuard, requireAuthGuard } from './core/guards/auth.guard';
import { anyRoleGuard, permissionGuard } from './core/guards/permission.guard';
import { FAMILY_ROLES } from './core/nav';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent)
  },
  {
    path: 'change-password',
    canActivate: [requireAuthGuard],
    loadComponent: () =>
      import('./features/auth/change-password.component').then((m) => m.ChangePasswordComponent)
  },
  // Anonymous email-flow landing pages. Their paths are dictated by WebLinkBuilder on the
  // server (`{FrontendBaseUrl}/auth/reset-password` and `/auth/confirm-email`), so they must
  // stay exactly here, and they are deliberately unguarded: the emailed link has to open even
  // when a stale session exists in the same browser.
  {
    path: 'auth/forgot-password',
    loadComponent: () =>
      import('./features/auth/forgot-password.component').then((m) => m.ForgotPasswordComponent)
  },
  {
    path: 'auth/reset-password',
    loadComponent: () =>
      import('./features/auth/reset-password.component').then((m) => m.ResetPasswordComponent)
  },
  // Public pre-registration page of a centre: anonymous, outside the shell, no guard.
  {
    path: 'register/:tenantSlug',
    loadComponent: () =>
      import('./features/registration/public-registration.component').then((m) => m.PublicRegistrationComponent)
  },
  {
    path: 'auth/confirm-email',
    loadComponent: () =>
      import('./features/auth/confirm-email.component').then((m) => m.ConfirmEmailComponent)
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent)
      },
      {
        path: 'tenants',
        canActivate: [permissionGuard('Platform.ManageTenants')],
        loadComponent: () =>
          import('./features/tenants/tenants.component').then((m) => m.TenantsComponent)
      },
      {
        path: 'users',
        canActivate: [permissionGuard('Users.View')],
        loadComponent: () => import('./features/users/users.component').then((m) => m.UsersComponent)
      },
      {
        path: 'roles',
        canActivate: [permissionGuard('Roles.View')],
        loadComponent: () => import('./features/roles/roles.component').then((m) => m.RolesComponent)
      },
      {
        path: 'permissions',
        canActivate: [permissionGuard('Permissions.View')],
        loadComponent: () =>
          import('./features/permissions/permissions.component').then((m) => m.PermissionsComponent)
      },
      {
        path: 'branches',
        canActivate: [permissionGuard('Branches.View')],
        loadComponent: () =>
          import('./features/branches/branches.component').then((m) => m.BranchesComponent)
      },
      {
        path: 'settings',
        canActivate: [permissionGuard('Settings.View')],
        loadComponent: () =>
          import('./features/settings/settings.component').then((m) => m.SettingsComponent)
      },
      {
        path: 'academic-years',
        canActivate: [permissionGuard('AcademicYears.View')],
        loadComponent: () =>
          import('./features/academic/academic-years.component').then((m) => m.AcademicYearsComponent)
      },
      {
        path: 'grades',
        canActivate: [permissionGuard('Grades.View')],
        loadComponent: () =>
          import('./features/academic/grades.component').then((m) => m.GradesComponent)
      },
      {
        path: 'subjects',
        canActivate: [permissionGuard('Subjects.View')],
        loadComponent: () =>
          import('./features/academic/subjects.component').then((m) => m.SubjectsComponent)
      },
      {
        path: 'teachers',
        canActivate: [permissionGuard('Teachers.View')],
        loadComponent: () =>
          import('./features/teachers/teachers.component').then((m) => m.TeachersComponent)
      },
      {
        path: 'students',
        canActivate: [permissionGuard('Students.View')],
        loadComponent: () =>
          import('./features/students/students.component').then((m) => m.StudentsComponent)
      },
      {
        path: 'registration-requests',
        canActivate: [permissionGuard('RegistrationRequests.View')],
        loadComponent: () =>
          import('./features/registration/registration-requests.component').then((m) => m.RegistrationRequestsComponent)
      },
      {
        path: 'parents',
        canActivate: [permissionGuard('Parents.View')],
        loadComponent: () =>
          import('./features/parents/parents.component').then((m) => m.ParentsComponent)
      },
      {
        path: 'my-children',
        canActivate: [permissionGuard('Parents.ViewOwnChildren')],
        loadComponent: () =>
          import('./features/parents/my-children.component').then((m) => m.MyChildrenComponent)
      },
      {
        path: 'my-family',
        canActivate: [anyRoleGuard(FAMILY_ROLES)],
        loadComponent: () =>
          import('./features/family/my-family.component').then((m) => m.MyFamilyComponent)
      },
      {
        path: 'groups',
        canActivate: [permissionGuard('Groups.View')],
        loadComponent: () => import('./features/groups/groups.component').then((m) => m.GroupsComponent)
      },
      {
        path: 'sessions',
        canActivate: [permissionGuard('Sessions.View')],
        loadComponent: () =>
          import('./features/sessions/sessions.component').then((m) => m.SessionsComponent)
      },
      {
        path: 'homework',
        canActivate: [permissionGuard('Homework.View')],
        loadComponent: () =>
          import('./features/learning/homework.component').then((m) => m.HomeworkComponent)
      },
      {
        path: 'exams',
        canActivate: [permissionGuard('Exams.View')],
        loadComponent: () => import('./features/learning/exams.component').then((m) => m.ExamsComponent)
      },
      {
        path: 'fees',
        canActivate: [permissionGuard('Fees.View')],
        loadComponent: () => import('./features/fees/fees.component').then((m) => m.FeesComponent)
      },
      {
        path: 'finance',
        canActivate: [permissionGuard('Finance.View')],
        data: { tab: 'cash' },
        loadComponent: () =>
          import('./features/finance/finance.component').then((m) => m.FinanceComponent)
      },
      {
        path: 'reports',
        canActivate: [permissionGuard('Reports.View')],
        data: { tab: 'summary' },
        loadComponent: () =>
          import('./features/finance/finance.component').then((m) => m.FinanceComponent)
      },
      {
        path: 'audit',
        canActivate: [permissionGuard('AuditLogs.View')],
        loadComponent: () => import('./features/audit/audit.component').then((m) => m.AuditComponent)
      },
      {
        path: 'profile',
        loadComponent: () =>
          import('./features/profile/profile.component').then((m) => m.ProfileComponent)
      }
    ]
  },
  { path: '**', redirectTo: 'dashboard' }
];
