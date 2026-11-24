import { Component, computed, input } from '@angular/core';
import { LocalTable } from './local-table';

/** A sortable column header: <th [vpSort]="'city'" [table]="t">City</th>. */
@Component({
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'th[vpSort]',
  host: {
    class: 'sortable',
    '[attr.aria-sort]': 'ariaSort()',
  },
  template: `
    <button type="button" class="sort-btn" (click)="table().sortBy($any(vpSort()))">
      <ng-content /><i class="fa-solid" [class]="icon()" aria-hidden="true"></i>
    </button>
  `,
  styles: `
    .sort-btn { display: inline-flex; align-items: center; gap: 6px; padding: 0; font: inherit; color: inherit; letter-spacing: inherit; text-transform: inherit; }
    :host(.right) .sort-btn { flex-direction: row; }
    i { font-size: 10px; opacity: 0.45; }
    :host([aria-sort='ascending']) i, :host([aria-sort='descending']) i { opacity: 1; color: var(--brand); }
  `,
})
export class SortHeader {
  readonly vpSort = input.required<string>();
  readonly table = input.required<LocalTable<never>>();

  private readonly active = computed(() => this.table().sort() === this.vpSort());
  protected readonly icon = computed(() => (!this.active() ? 'fa-sort' : this.table().dir() === 'asc' ? 'fa-sort-up' : 'fa-sort-down'));
  protected readonly ariaSort = computed(() => (!this.active() ? null : this.table().dir() === 'asc' ? 'ascending' : 'descending'));
}
