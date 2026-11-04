export interface NavItem {
  label: string;
  icon: string;
  route: string;
  permission?: string;
  role?: string;
}

export interface NavGroup {
  label: string;
  items: NavItem[];
}

export const NAV_GROUPS: NavGroup[] = [
  {
    label: 'nav.overview',
    items: [
      { label: 'nav.dashboard', icon: 'pi pi-home', route: '/dashboard', permission: 'Dashboard.View' }
    ]
  },
  {
    label: 'nav.platform',
    items: [
      {
        label: 'nav.tenants',
        icon: 'pi pi-building',
        route: '/tenants',
        permission: 'Platform.ManageTenants'
      }
    ]
  },
  {
    label: 'nav.administration',
    items: [
      { label: 'nav.users', icon: 'pi pi-users', route: '/users', permission: 'Users.View' },
      { label: 'nav.roles', icon: 'pi pi-id-card', route: '/roles', permission: 'Roles.View' },
      {
        label: 'nav.permissionsCatalog',
        icon: 'pi pi-key',
        route: '/permissions',
        permission: 'Permissions.View'
      },
      {
        label: 'nav.branches',
        icon: 'pi pi-sitemap',
        route: '/branches',
        permission: 'Branches.View'
      },
      {
        label: 'nav.settings',
        icon: 'pi pi-sliders-h',
        route: '/settings',
        permission: 'Settings.View'
      }
    ]
  },
  {
    label: 'nav.academicStructure',
    items: [
      {
        label: 'nav.academicYears',
        icon: 'pi pi-calendar',
        route: '/academic-years',
        permission: 'AcademicYears.View'
      },
      { label: 'nav.grades', icon: 'pi pi-list', route: '/grades', permission: 'Grades.View' },
      { label: 'nav.subjects', icon: 'pi pi-book', route: '/subjects', permission: 'Subjects.View' },
      { label: 'nav.teachers', icon: 'pi pi-user', route: '/teachers', permission: 'Teachers.View' }
    ]
  },
  {
    label: 'nav.people',
    items: [
      { label: 'nav.students', icon: 'pi pi-graduation-cap', route: '/students', permission: 'Students.View' },
      { label: 'nav.parents', icon: 'pi pi-users', route: '/parents', permission: 'Parents.View' },
      {
        label: 'nav.myChildren',
        icon: 'pi pi-heart',
        route: '/my-children',
        permission: 'Parents.ViewOwnChildren'
      }
    ]
  },
  {
    label: 'nav.teaching',
    items: [
      { label: 'nav.groups', icon: 'pi pi-objects-column', route: '/groups', permission: 'Groups.View' },
      { label: 'nav.sessions', icon: 'pi pi-calendar-clock', route: '/sessions', permission: 'Sessions.View' },
      { label: 'nav.homework', icon: 'pi pi-pencil', route: '/homework', permission: 'Homework.View' },
      { label: 'nav.exams', icon: 'pi pi-file-edit', route: '/exams', permission: 'Exams.View' }
    ]
  },
  {
    label: 'nav.money',
    items: [
      { label: 'nav.fees', icon: 'pi pi-receipt', route: '/fees', permission: 'Fees.View' },
      { label: 'nav.finance', icon: 'pi pi-chart-line', route: '/finance', permission: 'Finance.View' },
      { label: 'nav.reports', icon: 'pi pi-chart-bar', route: '/reports', permission: 'Reports.View' }
    ]
  },
  {
    label: 'nav.compliance',
    items: [
      {
        label: 'nav.audit',
        icon: 'pi pi-history',
        route: '/audit',
        permission: 'AuditLogs.View'
      }
    ]
  }
];
