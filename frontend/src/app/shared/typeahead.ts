import { Component, input, model, OnDestroy, output, signal } from '@angular/core';

export interface TypeaheadOption {
  id: string;
  label: string;
  sub?: string;
  badge?: string;
}

/**
 * Text field with search-as-you-type suggestions. The text is free (typing clears the picked option),
 * or the user picks a suggestion with mouse or keyboard (↑ ↓ Enter, Esc to close).
 */
@Component({
  selector: 'vp-typeahead',
  host: { '(focusout)': 'onBlur($event)' },
  template: `
    <div class="wrap" [class.picked]="pickedId()">
      <input
        [id]="inputId()"
        [value]="text()"
        [placeholder]="placeholder()"
        autocomplete="off"
        role="combobox"
        [attr.aria-expanded]="open()"
        [attr.aria-controls]="inputId() + '-list'"
        [attr.aria-activedescendant]="open() && active() >= 0 ? inputId() + '-opt-' + active() : null"
        (input)="onInput($any($event.target).value)"
        (focus)="onFocus()"
        (keydown)="onKey($event)"
      />
      @if (pickedId()) {
        <span class="tick" title="Linked to an existing record"><i class="fa-solid fa-link"></i></span>
      } @else if (loading()) {
        <span class="tick muted"><i class="fa-solid fa-spinner fa-spin"></i></span>
      }
      @if (open() && (results().length || hint())) {
        <ul class="menu" role="listbox" [id]="inputId() + '-list'">
          @for (o of results(); track o.id; let i = $index) {
            <li role="option" [id]="inputId() + '-opt-' + i" [class.active]="i === active()" [attr.aria-selected]="i === active()"
              (mousedown)="$event.preventDefault(); pick(o)" (mouseenter)="active.set(i)">
              <span class="main"><b>{{ o.label }}</b>@if (o.badge) { <span class="tag">{{ o.badge }}</span> }</span>
              @if (o.sub) { <span class="sub">{{ o.sub }}</span> }
            </li>
          }
          @if (!results().length && hint()) { <li class="hint">{{ hint() }}</li> }
        </ul>
      }
    </div>
  `,
  styles: `
    .wrap { position: relative; }
    input { width: 100%; height: 40px; padding: 0 36px 0 13px; border: 1px solid var(--line); border-radius: 9px; font: inherit; font-size: 13.5px; background: #fff; }
    input:focus { outline: none; border-color: var(--brand); box-shadow: 0 0 0 3px var(--brand-soft); }
    .picked input { border-color: var(--ok); }
    .tick { position: absolute; right: 12px; top: 50%; transform: translateY(-50%); color: var(--ok); font-size: 13px; }
    .menu { position: absolute; z-index: 20; left: 0; right: 0; top: calc(100% + 4px); max-height: 260px; overflow-y: auto; margin: 0; padding: 4px;
      list-style: none; background: #fff; border: 1px solid var(--line); border-radius: 10px; box-shadow: var(--shadow-lg); }
    li { display: flex; flex-direction: column; gap: 2px; padding: 8px 10px; border-radius: 8px; cursor: pointer; }
    li.active { background: var(--brand-soft); }
    .main { display: flex; align-items: center; gap: 8px; font-size: 13.5px; }
    .tag { padding: 1px 7px; border-radius: 10px; background: var(--bg-2); color: var(--muted); font-size: 11px; font-weight: 600; }
    .sub { font-size: 12px; color: var(--muted); }
    .hint { cursor: default; color: var(--muted); font-size: 12.5px; }
  `,
})
export class Typeahead implements OnDestroy {
  readonly inputId = input.required<string>();
  readonly placeholder = input('');
  /** Returns suggestions for the typed text. */
  readonly search = input.required<(q: string) => Promise<TypeaheadOption[]>>();
  /** Shown when nothing matches, e.g. "No client found. The name will be used as typed." */
  readonly hint = input<string>('');
  readonly text = model('');
  readonly pickedId = model<string | null>(null);
  readonly picked = output<TypeaheadOption>();

  protected readonly results = signal<TypeaheadOption[]>([]);
  protected readonly open = signal(false);
  protected readonly loading = signal(false);
  protected readonly active = signal(-1);
  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }

  protected onInput(value: string): void {
    this.text.set(value);
    this.pickedId.set(null); // typed text is no longer the picked record
    this.queue(value);
  }

  protected onFocus(): void {
    if (!this.pickedId()) this.queue(this.text());
  }

  protected onBlur(event: FocusEvent): void {
    if (!(event.currentTarget as HTMLElement).contains(event.relatedTarget as Node)) this.open.set(false);
  }

  protected onKey(e: KeyboardEvent): void {
    const n = this.results().length;
    if (e.key === 'ArrowDown' && n) {
      e.preventDefault();
      this.open.set(true);
      this.active.set((this.active() + 1) % n);
    } else if (e.key === 'ArrowUp' && n) {
      e.preventDefault();
      this.active.set((this.active() - 1 + n) % n);
    } else if (e.key === 'Enter' && this.open() && this.active() >= 0) {
      e.preventDefault();
      this.pick(this.results()[this.active()]);
    } else if (e.key === 'Escape' && this.open()) {
      e.stopPropagation(); // don't close the surrounding dialog
      this.open.set(false);
    }
  }

  protected pick(o: TypeaheadOption): void {
    this.text.set(o.label);
    this.pickedId.set(o.id);
    this.open.set(false);
    this.picked.emit(o);
  }

  private queue(q: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(async () => {
      const seq = ++this.seq;
      this.loading.set(true);
      try {
        const r = await this.search()(q.trim());
        if (seq !== this.seq) return;
        this.results.set(r);
        this.active.set(r.length ? 0 : -1);
        this.open.set(true);
      } catch {
        if (seq === this.seq) this.results.set([]);
      } finally {
        if (seq === this.seq) this.loading.set(false);
      }
    }, 200);
  }
}
