import { displayDate, displayMoney } from './ops-utils';

/** QA-10: family pages show dates in the UI language and amounts with the centre currency. */
describe('family display helpers', () => {
  it('formats money with the currency label of each language', () => {
    expect(displayMoney(500, 'EGP', 'en')).toBe('EGP 500.00');
    expect(displayMoney(500, 'EGP', 'ar')).toBe('500.00 ج.م');
    expect(displayMoney(1234.5, 'egp', 'en')).toBe('EGP 1,234.50');
    expect(displayMoney(10, 'KWD', 'ar')).toBe('10.00 KWD');
    expect(displayMoney(0, null, 'en')).toBe('EGP 0.00');
    expect(displayMoney(null, 'EGP', 'en')).toBe('—');
  });

  it('formats a DateOnly in the UI language without shifting the day', () => {
    expect(displayDate('2026-11-22', 'en')).toBe('22 Nov 2026');

    const arabic = displayDate('2026-11-22', 'ar');
    expect(arabic).toContain('22');
    expect(arabic).toContain('2026');
    expect(arabic).toContain('نوفمبر');
    expect(arabic).not.toContain('Nov');

    expect(displayDate('2026-01-01', 'en')).toBe('1 Jan 2026');
    expect(displayDate(null, 'en')).toBe('—');
    expect(displayDate('not-a-date', 'en')).toBe('not-a-date');
  });
});
