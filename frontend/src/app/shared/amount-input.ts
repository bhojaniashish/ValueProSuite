import { Directive, ElementRef, forwardRef, inject, input } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

/**
 * A number box that shows Indian digit grouping (1,15,000) and gives the form a plain number (or null when empty).
 * Use on a text input: <input vpAmount name="rate" [(ngModel)]="m.rate" />. Opens the number keypad on phones.
 */
@Directive({
  selector: 'input[vpAmount]',
  host: {
    inputmode: 'decimal',
    autocomplete: 'off',
    '(input)': 'onInput()',
    '(blur)': 'onBlur()',
  },
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => AmountInput), multi: true }],
})
export class AmountInput implements ControlValueAccessor {
  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);
  /** Decimal places allowed (0 for whole rupees). */
  readonly decimals = input(0);

  private onChange: (v: number | null) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(v: number | null): void {
    this.el.nativeElement.value = format(v, this.decimals());
  }
  registerOnChange(fn: (v: number | null) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(disabled: boolean): void {
    this.el.nativeElement.disabled = disabled;
  }

  protected onInput(): void {
    this.onChange(parseAmount(this.el.nativeElement.value, this.decimals()));
  }

  protected onBlur(): void {
    const v = parseAmount(this.el.nativeElement.value, this.decimals());
    this.el.nativeElement.value = format(v, this.decimals());
    this.onTouched();
  }
}

/** "₹ 1,15,000.50" → 115000.5; anything without digits → null. */
export function parseAmount(text: string, decimals = 0): number | null {
  const clean = text.replace(/[^\d.]/g, '');
  if (!/\d/.test(clean)) return null;
  const [whole, ...rest] = clean.split('.');
  const n = Number(rest.length ? `${whole || '0'}.${rest.join('')}` : whole);
  if (!Number.isFinite(n)) return null;
  const f = 10 ** decimals;
  return Math.round(n * f) / f;
}

function format(v: number | null | undefined, decimals: number): string {
  return v == null ? '' : v.toLocaleString('en-IN', { maximumFractionDigits: decimals });
}
