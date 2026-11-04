import { EN_ACADEMIC } from './parts/en/academic';
import { EN_AUDIT } from './parts/en/audit';
import { EN_OPERATIONS } from './parts/en/operations';
import { EN_BRANCHES } from './parts/en/branches';
import { EN_CHANGEPASSWORD } from './parts/en/changePassword';
import { EN_COMMON } from './parts/en/common';
import { EN_DASHBOARD } from './parts/en/dashboard';
import { EN_LOGIN } from './parts/en/login';
import { EN_PERMISSIONS } from './parts/en/permissions';
import { EN_PROFILE } from './parts/en/profile';
import { EN_ROLES } from './parts/en/roles';
import { EN_SETTINGS } from './parts/en/settings';
import { EN_SHELL } from './parts/en/shell';
import { EN_TENANTS } from './parts/en/tenants';
import { EN_STUDENTS } from './parts/en/students';
import { EN_TEACHERS } from './parts/en/teachers';
import { EN_USERS } from './parts/en/users';

/** English catalog. Keys are merged from the per-domain parts; later spreads win. */
export const EN: Record<string, string> = {
  ...EN_COMMON,
  ...EN_LOGIN,
  ...EN_CHANGEPASSWORD,
  ...EN_SHELL,
  ...EN_PROFILE,
  ...EN_DASHBOARD,
  ...EN_PERMISSIONS,
  ...EN_USERS,
  ...EN_ROLES,
  ...EN_BRANCHES,
  ...EN_TENANTS,
  ...EN_SETTINGS,
  ...EN_ACADEMIC,
  ...EN_TEACHERS,
  ...EN_STUDENTS,
  ...EN_AUDIT,
  ...EN_OPERATIONS
};
