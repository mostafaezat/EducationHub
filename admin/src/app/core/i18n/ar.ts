import { AR_ACADEMIC } from './parts/ar/academic';
import { AR_AUDIT } from './parts/ar/audit';
import { AR_OPERATIONS } from './parts/ar/operations';
import { AR_BRANCHES } from './parts/ar/branches';
import { AR_CHANGEPASSWORD } from './parts/ar/changePassword';
import { AR_COMMON } from './parts/ar/common';
import { AR_DASHBOARD } from './parts/ar/dashboard';
import { AR_LOGIN } from './parts/ar/login';
import { AR_PERMISSIONS } from './parts/ar/permissions';
import { AR_PROFILE } from './parts/ar/profile';
import { AR_ROLES } from './parts/ar/roles';
import { AR_SETTINGS } from './parts/ar/settings';
import { AR_SHELL } from './parts/ar/shell';
import { AR_TENANTS } from './parts/ar/tenants';
import { AR_STUDENTS } from './parts/ar/students';
import { AR_TEACHERS } from './parts/ar/teachers';
import { AR_USERS } from './parts/ar/users';

/** Arabic catalog. Key set must stay identical to en.ts - enforced by the i18n check script. */
export const AR: Record<string, string> = {
  ...AR_COMMON,
  ...AR_LOGIN,
  ...AR_CHANGEPASSWORD,
  ...AR_SHELL,
  ...AR_PROFILE,
  ...AR_DASHBOARD,
  ...AR_PERMISSIONS,
  ...AR_USERS,
  ...AR_ROLES,
  ...AR_BRANCHES,
  ...AR_TENANTS,
  ...AR_SETTINGS,
  ...AR_ACADEMIC,
  ...AR_TEACHERS,
  ...AR_STUDENTS,
  ...AR_AUDIT,
  ...AR_OPERATIONS
};
