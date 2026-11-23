import { financialYear } from './gst-section';

describe('financialYear', () => {
  it('starts the Indian financial year in April', () => {
    expect(financialYear(new Date(2026, 2, 31))).toBe('25-26'); // 31 Mar 2026
    expect(financialYear(new Date(2026, 3, 1))).toBe('26-27'); //  1 Apr 2026
    expect(financialYear(new Date(2099, 11, 1))).toBe('99-00');
  });
});
