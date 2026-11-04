import { StudentStatus } from '../../core/models';

/** Formats a picker date as the API's DateOnly (yyyy-MM-dd), in local time. */
export function toDateOnly(value: Date | null | undefined): string | null {
  if (!value) {
    return null;
  }

  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, '0');
  const day = String(value.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function statusSeverity(status: StudentStatus): 'success' | 'secondary' | 'info' | 'warn' {
  switch (status) {
    case 'Active':
      return 'success';
    case 'Graduated':
      return 'info';
    case 'Withdrawn':
      return 'warn';
    default:
      return 'secondary';
  }
}
