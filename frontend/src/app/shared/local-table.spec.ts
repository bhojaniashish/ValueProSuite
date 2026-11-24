import { signal } from '@angular/core';
import { parseAmount } from './amount-input';
import { compare, LocalTable } from './local-table';

interface Row { name: string; city: string | null; rate: number | null }

describe('LocalTable', () => {
  const rows = (n: number): Row[] =>
    Array.from({ length: n }, (_, i) => ({ name: `Plot ${i + 1}`, city: i % 2 ? 'Surat' : 'Mumbai', rate: i % 5 ? 1000 * i : null }));

  it('searches the chosen fields, ignoring case, and goes back to page 1', () => {
    const t = new LocalTable(signal(rows(60)), { searchIn: ['name', 'city'], pageSize: 25 });
    t.goTo(3);
    t.setSearch('surat');
    expect(t.total()).toBe(30);
    expect(t.page()).toBe(1);
    expect(t.view().length).toBe(25);
    expect(t.range()).toBe('1–25 of 30');
  });

  it('sorts naturally and reverses on a second click; empty values stay last', () => {
    const t = new LocalTable(signal(rows(12)), { searchIn: ['name'], sort: 'name' });
    expect(t.rows().map((r) => r.name).slice(0, 3)).toEqual(['Plot 1', 'Plot 2', 'Plot 3']);
    t.sortBy('name');
    expect(t.rows()[0].name).toBe('Plot 12');
    t.sortBy('rate');
    expect(t.dir()).toBe('asc');
    expect(t.rows().at(-1)?.rate).toBeNull();
  });

  it('keeps the page in range when rows shrink', () => {
    const src = signal(rows(60));
    const t = new LocalTable(src, { searchIn: ['name'], pageSize: 25 });
    t.goTo(9);
    expect(t.page()).toBe(3);
    src.set(rows(10));
    expect(t.view().length).toBe(10);
    expect(t.range()).toBe('1–10 of 10');
  });

  it('compares numbers numerically and text naturally', () => {
    expect(compare(9, 10)).toBeLessThan(0);
    expect(compare('A2', 'a10')).toBeLessThan(0);
    expect(compare(null, 'x')).toBeGreaterThan(0);
  });
});

describe('parseAmount', () => {
  it('reads Indian-formatted amounts', () => {
    expect(parseAmount('₹ 1,15,000')).toBe(115000);
    expect(parseAmount('12.5', 2)).toBe(12.5);
    expect(parseAmount('12.567', 2)).toBe(12.57);
    expect(parseAmount('99.6')).toBe(100);
    expect(parseAmount('')).toBeNull();
    expect(parseAmount('abc')).toBeNull();
  });
});
