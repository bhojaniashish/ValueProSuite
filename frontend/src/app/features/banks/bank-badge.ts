import { Component, computed, input } from '@angular/core';

/** Coloured square with the bank's short code, as on the prototype's bank cards. Size via CSS var --size. */
@Component({
  selector: 'vp-bank-badge',
  template: `<span class="badge-box" [style.background]="color()" [style.font-size]="fontSize()">{{ code() }}</span>`,
  styles: `
    :host { display: inline-flex; flex: none; }
    .badge-box { display: grid; place-items: center; width: var(--size, 48px); height: var(--size, 48px); border-radius: 22%;
      color: #fff; font-weight: 800; letter-spacing: .01em; font-family: 'Plus Jakarta Sans', sans-serif; overflow: hidden; }
  `,
})
export class BankBadge {
  readonly code = input.required<string>();
  readonly color = input('#2B50C4');
  /** Text scales with the badge, smaller for longer codes (SBI → CANARA). */
  protected readonly fontSize = computed(() => {
    const n = this.code().length;
    const factor = n <= 3 ? 0.31 : n === 4 ? 0.26 : n === 5 ? 0.22 : 0.18;
    return `calc(var(--size, 48px) * ${factor})`;
  });
}
