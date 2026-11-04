/** Small helpers shared by the scheduling, finance and learning pages. */

export interface Option {
  label: string;
  value: string;
}

/** Today in local time as the API's DateOnly (yyyy-MM-dd). */
export function todayIso(offsetDays = 0): string {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  return isoDate(d);
}

export function isoDate(d: Date): string {
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** First and last day of the current month. */
export function currentMonthRange(): { from: string; to: string } {
  const now = new Date();
  return {
    from: isoDate(new Date(now.getFullYear(), now.getMonth(), 1)),
    to: isoDate(new Date(now.getFullYear(), now.getMonth() + 1, 0))
  };
}

/** The current month as the fee engine's period key (yyyy-MM). */
export function currentPeriod(): string {
  return todayIso().slice(0, 7);
}

/** "16:00:00" -> "16:00". */
export function shortTime(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : '';
}

/** "16:00" -> "16:00:00" (the API's TimeOnly). */
export function toTimeOnly(value: string): string {
  return value.length === 5 ? `${value}:00` : value;
}

export function money(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—';
  }

  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
