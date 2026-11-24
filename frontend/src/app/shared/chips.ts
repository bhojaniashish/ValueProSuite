import { Component, ElementRef, inject, input, model } from '@angular/core';

export interface ChipOption { value: string; label: string }

/** Tells forms around the control that it changed (like a native input's change event), e.g. to trigger autosave. */
function announce(el: HTMLElement): void {
  el.dispatchEvent(new Event('change', { bubbles: true }));
}

const CHIP_STYLES = `
  :host { display: flex; flex-wrap: wrap; gap: 8px; }
  button { display: inline-flex; align-items: center; gap: 6px; min-height: 38px; padding: 7px 14px; border: 1px solid var(--line); border-radius: 99px;
    background: #fff; color: var(--ink-2); font-size: 13.5px; font-weight: 500; line-height: 1.2; text-align: left;
    transition: border-color .15s var(--ease), background .15s var(--ease), color .15s var(--ease); }
  button:hover:not(:disabled) { border-color: var(--brand); }
  button.on { border-color: var(--brand); background: var(--brand-soft); color: var(--brand); font-weight: 600; }
  button:focus-visible { outline: 2px solid var(--brand); outline-offset: 2px; }
  button:disabled { cursor: default; opacity: .75; }
  button:disabled:not(.on) { opacity: .5; }
  i { font-size: 11px; }
`;

/** Pick one. Tapping the chosen chip again clears it. */
@Component({
  selector: 'vp-choice',
  host: { role: 'radiogroup', '[attr.aria-label]': 'label()' },
  template: `
    @for (o of options(); track o.value) {
      <button type="button" role="radio" [class.on]="value() === o.value" [attr.aria-checked]="value() === o.value" [disabled]="disabled()" (click)="pick(o.value)">
        @if (value() === o.value) { <i class="fa-solid fa-check"></i> }{{ o.label }}
      </button>
    }
  `,
  styles: CHIP_STYLES,
})
export class Choice {
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly options = input.required<ChipOption[]>();
  readonly label = input<string>();
  readonly disabled = input(false);
  readonly value = model<string | null>(null);

  protected pick(v: string): void {
    this.value.set(this.value() === v ? null : v);
    announce(this.el.nativeElement);
  }
}

/** Pick any number. */
@Component({
  selector: 'vp-multi',
  host: { role: 'group', '[attr.aria-label]': 'label()' },
  template: `
    @for (o of options(); track o.value) {
      <button type="button" [class.on]="has(o.value)" [attr.aria-pressed]="has(o.value)" [disabled]="disabled()" (click)="toggle(o.value)">
        @if (has(o.value)) { <i class="fa-solid fa-check"></i> }{{ o.label }}
      </button>
    }
  `,
  styles: CHIP_STYLES,
})
export class Multi {
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly options = input.required<ChipOption[]>();
  readonly label = input<string>();
  readonly disabled = input(false);
  readonly value = model<string[]>([]);

  protected has(v: string): boolean {
    return (this.value() ?? []).includes(v);
  }

  protected toggle(v: string): void {
    const cur = this.value() ?? [];
    this.value.set(cur.includes(v) ? cur.filter((x) => x !== v) : [...cur, v]);
    announce(this.el.nativeElement);
  }
}

/** Yes / No (tap again to clear). */
@Component({
  selector: 'vp-yes-no',
  host: { role: 'radiogroup', '[attr.aria-label]': 'label()' },
  template: `
    <button type="button" role="radio" [class.on]="value() === true" [attr.aria-checked]="value() === true" [disabled]="disabled()" (click)="pick(true)">
      @if (value() === true) { <i class="fa-solid fa-check"></i> }{{ yes() }}
    </button>
    <button type="button" role="radio" [class.on]="value() === false" [attr.aria-checked]="value() === false" [disabled]="disabled()" (click)="pick(false)">
      @if (value() === false) { <i class="fa-solid fa-check"></i> }{{ no() }}
    </button>
  `,
  styles: CHIP_STYLES,
})
export class YesNo {
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly label = input<string>();
  readonly yes = input('Yes');
  readonly no = input('No');
  readonly disabled = input(false);
  readonly value = model<boolean | null>(null);

  protected pick(v: boolean): void {
    this.value.set(this.value() === v ? null : v);
    announce(this.el.nativeElement);
  }
}
