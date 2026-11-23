import { Component, Injectable, inject, signal } from '@angular/core';

export type ToastKind = 'ok' | 'info' | 'warn' | 'danger';

interface Toast {
  id: number;
  kind: ToastKind;
  title: string;
  message?: string;
}

/** Short confirmations in the corner ("User added", "Permissions saved"). */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private nextId = 1;

  show(kind: ToastKind, title: string, message?: string): void {
    const id = this.nextId++;
    this.toasts.update((t) => [...t, { id, kind, title, message }]);
    setTimeout(() => this.dismiss(id), kind === 'danger' ? 6000 : 3500);
  }

  ok(title: string, message?: string): void { this.show('ok', title, message); }
  error(title: string, message?: string): void { this.show('danger', title, message); }

  dismiss(id: number): void {
    this.toasts.update((t) => t.filter((x) => x.id !== id));
  }
}

const ICONS: Record<ToastKind, string> = {
  ok: 'fa-circle-check',
  info: 'fa-circle-info',
  warn: 'fa-triangle-exclamation',
  danger: 'fa-circle-xmark',
};

@Component({
  selector: 'vp-toasts',
  template: `
    <div class="host" aria-live="polite">
      @for (t of toasts.toasts(); track t.id) {
        <div class="toast" [class]="t.kind" role="status">
          <i class="fa-solid" [class]="icon(t.kind)"></i>
          <div class="text">
            <b>{{ t.title }}</b>
            @if (t.message) { <span>{{ t.message }}</span> }
          </div>
          <button type="button" (click)="toasts.dismiss(t.id)" aria-label="Dismiss"><i class="fa-solid fa-xmark"></i></button>
        </div>
      }
    </div>
  `,
  styles: `
    .host { position: fixed; right: 16px; bottom: 16px; z-index: 300; display: flex; flex-direction: column; gap: 8px;
      max-width: min(380px, calc(100vw - 32px)); }
    .toast { display: flex; align-items: flex-start; gap: 10px; padding: 12px 14px; background: var(--card);
      border: 1px solid var(--line); border-left: 4px solid var(--c); border-radius: 10px; box-shadow: var(--shadow-lg);
      animation: in .2s var(--ease); }
    .toast > i { margin-top: 2px; color: var(--c); }
    .ok { --c: var(--ok); } .info { --c: var(--info); } .warn { --c: var(--warn); } .danger { --c: var(--danger); }
    .text { flex: 1; display: flex; flex-direction: column; gap: 2px; font-size: 13px; }
    .text span { color: var(--muted); }
    button { color: var(--muted-2); }
    @keyframes in { from { opacity: 0; transform: translateY(8px); } }
  `,
})
export class Toasts {
  protected readonly toasts = inject(ToastService);
  protected icon(kind: ToastKind): string { return ICONS[kind]; }
}
