import { Component, input, output } from '@angular/core';

/** "1–25 of 140   ‹ Page 1 of 6 ›" — the same pager for server-paged and browser-paged lists. */
@Component({
  selector: 'vp-pager',
  template: `
    <span class="small muted">{{ range() }}</span>
    @if (pages() > 1) {
      <div class="pg">
        <button class="btn-icon" type="button" aria-label="Previous page" [disabled]="page() <= 1" (click)="goTo.emit(page() - 1)"><i class="fa-solid fa-chevron-left"></i></button>
        <span class="small tnum">Page {{ page() }} of {{ pages() }}</span>
        <button class="btn-icon" type="button" aria-label="Next page" [disabled]="page() >= pages()" (click)="goTo.emit(page() + 1)"><i class="fa-solid fa-chevron-right"></i></button>
      </div>
    }
  `,
  styles: `
    :host { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px; padding: 12px 18px; border-top: 1px solid var(--line-2); }
    .pg { display: flex; align-items: center; gap: 8px; }
  `,
})
export class Pager {
  readonly page = input.required<number>();
  readonly pages = input.required<number>();
  readonly range = input.required<string>();
  readonly goTo = output<number>();
}
