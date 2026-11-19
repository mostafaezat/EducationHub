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

/** Currency labels per UI language; any other ISO code is shown as is. */
const CURRENCY_LABELS: Record<string, { en: string; ar: string }> = {
  EGP: { en: 'EGP', ar: 'ج.م' },
  SAR: { en: 'SAR', ar: 'ر.س' },
  AED: { en: 'AED', ar: 'د.إ' },
  USD: { en: 'USD', ar: 'دولار' }
};

/**
 * An amount with the centre's currency in the UI language: "EGP 500.00" / "500.00 ج.م".
 * Latin digits in both languages, like every other number in the app.
 */
export function displayMoney(value: number | null | undefined, currency: string | null | undefined, language: string): string {
  if (value === null || value === undefined) {
    return '—';
  }

  const amount = value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const code = (currency || 'EGP').toUpperCase();
  const arabic = language === 'ar';
  const label = CURRENCY_LABELS[code]?.[arabic ? 'ar' : 'en'] ?? code;

  return arabic ? `${amount} ${label}` : `${label} ${amount}`;
}

/**
 * A DateOnly (yyyy-MM-dd) as a readable date in the UI language, e.g. "22 Nov 2026" /
 * "22 نوفمبر 2026". Parsed as a calendar date, so no time-zone shift can move it a day.
 */
export function displayDate(value: string | null | undefined, language: string): string {
  if (!value) {
    return '—';
  }

  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);

  if (!match) {
    return value;
  }

  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  const locale = language === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB';

  return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric' }).format(date);
}
