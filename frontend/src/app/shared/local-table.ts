import { computed, Signal, signal } from '@angular/core';

export type SortDir = 'asc' | 'desc';

export interface LocalTableOptions<T> {
  /** Fields the search box looks in. */
  searchIn: (keyof T)[];
  sort?: keyof T;
  dir?: SortDir;
  pageSize?: number;
}

/**
 * Search, sort and paging done in the browser, for lists that are loaded whole (masters with a few hundred rows).
 * Lists that can grow large (clients, assignments) page on the server instead.
 */
export class LocalTable<T> {
  readonly search = signal('');
  readonly sort = signal<keyof T | null>(null);
  readonly dir = signal<SortDir>('asc');
  readonly page = signal(1);
  readonly pageSize: number;

  /** Rows matching the search, in the chosen order (what an export should contain). */
  readonly rows: Signal<T[]>;
  /** Rows on the current page. */
  readonly view: Signal<T[]>;
  readonly total: Signal<number>;
  readonly pages: Signal<number>;
  /** "1–25 of 140". */
  readonly range: Signal<string>;

  constructor(source: Signal<T[]>, private readonly options: LocalTableOptions<T>) {
    this.sort.set(options.sort ?? null);
    this.dir.set(options.dir ?? 'asc');
    this.pageSize = options.pageSize ?? 25;

    const matched = computed(() => {
      const q = this.search().trim().toLowerCase();
      const all = source();
      if (!q) return all;
      return all.filter((r) => options.searchIn.some((f) => String(r[f] ?? '').toLowerCase().includes(q)));
    });

    this.rows = computed(() => {
      const field = this.sort();
      const list = [...matched()];
      if (!field) return list;
      const sign = this.dir() === 'asc' ? 1 : -1;
      return list.sort((a, b) => sign * compare(a[field], b[field]));
    });

    this.total = computed(() => this.rows().length);
    this.pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));
    this.view = computed(() => {
      const page = Math.min(this.page(), this.pages());
      return this.rows().slice((page - 1) * this.pageSize, page * this.pageSize);
    });
    this.range = computed(() => {
      const total = this.total();
      if (!total) return '0 rows';
      const from = (Math.min(this.page(), this.pages()) - 1) * this.pageSize + 1;
      return `${from}–${Math.min(total, from + this.pageSize - 1)} of ${total}`;
    });
  }

  setSearch(value: string): void {
    this.search.set(value);
    this.page.set(1);
  }

  /** Click a column: sort by it; click again to reverse. */
  sortBy(field: keyof T): void {
    if (this.sort() === field) this.dir.set(this.dir() === 'asc' ? 'desc' : 'asc');
    else {
      this.sort.set(field);
      this.dir.set('asc');
    }
  }

  goTo(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.pages()));
  }
}

/** Empty values last; numbers numerically; text in natural order (A2 before A10), ignoring case. */
export function compare(a: unknown, b: unknown): number {
  const ea = a == null || a === '';
  const eb = b == null || b === '';
  if (ea || eb) return ea === eb ? 0 : ea ? 1 : -1;
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return String(a).localeCompare(String(b), 'en-IN', { numeric: true, sensitivity: 'base' });
}
