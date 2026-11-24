import { Component, ElementRef, inject, input, model } from '@angular/core';

export interface TabItem {
  value: string;
  label: string;
  icon?: string;
  count?: number | null;
}

/** A row of tabs (scrolls sideways on phones). Content is shown by the parent with @switch on the value. */
@Component({
  selector: 'vp-tabs',
  host: { role: 'tablist', '(keydown)': 'onKey($event)' },
  template: `
    @for (t of tabs(); track t.value) {
      <button type="button" role="tab" class="tab" [class.active]="t.value === value()" [attr.aria-selected]="t.value === value()"
        [attr.tabindex]="t.value === value() ? 0 : -1" (click)="value.set(t.value)">
        @if (t.icon) { <i class="fa-solid" [class]="t.icon"></i> }
        {{ t.label }}
        @if (t.count != null) { <span class="count">{{ t.count }}</span> }
      </button>
    }
  `,
  styles: `
    :host { display: flex; gap: 4px; padding: 0 12px; border-bottom: 1px solid var(--line); overflow-x: auto; scrollbar-width: none; }
    :host::-webkit-scrollbar { display: none; }
    .tab { display: inline-flex; align-items: center; gap: 8px; flex: none; padding: 14px 12px 12px; border-bottom: 2px solid transparent; margin-bottom: -1px;
      font-size: 13.5px; font-weight: 600; color: var(--muted); white-space: nowrap; transition: color 0.15s var(--ease), border-color 0.15s var(--ease); }
    .tab:hover { color: var(--ink-2); }
    .tab.active { color: var(--brand); border-bottom-color: var(--brand); }
    .tab:focus-visible { outline: 2px solid var(--brand); outline-offset: -2px; border-radius: 6px; }
    .count { padding: 1px 7px; border-radius: 99px; background: var(--bg-2); color: var(--muted); font-size: 11.5px; }
    .tab.active .count { background: var(--brand-soft); color: var(--brand); }
  `,
})
export class Tabs {
  private readonly el = inject(ElementRef<HTMLElement>);
  readonly tabs = input.required<TabItem[]>();
  readonly value = model.required<string>();

  /** Left / right arrow keys move between tabs. */
  protected onKey(e: KeyboardEvent): void {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    const list = this.tabs();
    const i = list.findIndex((t) => t.value === this.value());
    const next = list[(i + (e.key === 'ArrowRight' ? 1 : list.length - 1)) % list.length];
    this.value.set(next.value);
    queueMicrotask(() => (this.el.nativeElement.querySelector('.tab.active') as HTMLElement | null)?.focus());
    e.preventDefault();
  }
}
