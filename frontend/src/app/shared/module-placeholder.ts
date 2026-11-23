import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NAVIGATION } from '../layout/navigation';

/** Stands in for a module until its phase is built. Route data `path` picks the module. */
@Component({
  selector: 'vp-module-placeholder',
  imports: [RouterLink],
  template: `
    @if (item(); as m) {
      <div class="card">
        <span class="icon"><i class="fa-solid" [class]="m.icon"></i></span>
        <h2>{{ m.label }}</h2>
        <p>{{ m.summary }}</p>
        <span class="badge">Arrives in Phase {{ m.phase }}</span>
        <a routerLink="/dashboard">Back to dashboard</a>
      </div>
    }
  `,
  styles: `
    .card { max-width: 560px; margin: 32px auto; padding: 32px; text-align: center; background: var(--card);
      border: 1px solid var(--line); border-radius: var(--radius-lg); box-shadow: var(--shadow-sm); }
    .icon { display: inline-grid; place-items: center; width: 56px; height: 56px; border-radius: 16px;
      background: var(--brand-soft); color: var(--brand); font-size: 22px; }
    h2 { margin: 16px 0 8px; }
    p { margin: 0 0 16px; color: var(--muted); line-height: 1.6; }
    .badge { display: inline-block; padding: 4px 12px; border-radius: 999px; background: var(--gold-soft);
      color: var(--gold); font-size: 13px; font-weight: 600; }
    a { display: block; margin-top: 20px; color: var(--brand); font-weight: 500; }
  `,
})
export class ModulePlaceholder {
  readonly path = input.required<string>();
  protected readonly item = computed(() => NAVIGATION.flatMap((g) => g.items).find((i) => i.path === this.path()));
}
