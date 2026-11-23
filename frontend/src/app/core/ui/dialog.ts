import { Component, input, output } from '@angular/core';

/**
 * Modal shell matching the prototype: coloured icon, title, subtitle, close button.
 * Put the body in the default slot and buttons in `[footer]`.
 */
@Component({
  selector: 'vp-dialog',
  host: { '(document:keydown.escape)': 'closed.emit()' },
  template: `
    <div class="overlay" (click)="closed.emit()">
      <div class="modal" [class]="size()" role="dialog" aria-modal="true" [attr.aria-label]="title()" (click)="$event.stopPropagation()">
        <div class="head">
          <span class="ic" [class]="tone()"><i class="fa-solid" [class]="icon()"></i></span>
          <div>
            <h3>{{ title() }}</h3>
            @if (subtitle()) { <p>{{ subtitle() }}</p> }
          </div>
          <button class="x" type="button" (click)="closed.emit()" aria-label="Close"><i class="fa-solid fa-xmark"></i></button>
        </div>
        <div class="body"><ng-content /></div>
        <div class="foot"><ng-content select="[footer]" /></div>
      </div>
    </div>
  `,
  styles: `
    .overlay { position: fixed; inset: 0; z-index: 200; display: flex; align-items: flex-start; justify-content: center;
      padding: 40px 16px; overflow-y: auto; background: rgba(22, 33, 62, .5); backdrop-filter: blur(4px); animation: fade .2s; }
    .modal { width: 100%; max-width: 620px; margin: auto 0; background: #fff; border-radius: var(--radius-lg);
      box-shadow: var(--shadow-lg); animation: rise .25s var(--ease); }
    .modal.sm { max-width: 440px; }
    .head { display: flex; align-items: center; gap: 14px; padding: 20px 24px; border-bottom: 1px solid var(--line-2); }
    .head h3 { margin: 0; font-size: 17px; }
    .head p { margin: 2px 0 0; font-size: 12.5px; color: var(--muted); }
    .ic { display: grid; place-items: center; flex: none; width: 42px; height: 42px; border-radius: 11px; font-size: 17px; }
    .x { margin-left: auto; width: 34px; height: 34px; border-radius: 8px; color: var(--muted); font-size: 16px; }
    .x:hover { background: var(--bg-2); }
    .body { padding: 22px 24px; }
    .foot { display: flex; justify-content: flex-end; gap: 10px; padding: 14px 24px; background: #fafbfe;
      border-top: 1px solid var(--line-2); border-radius: 0 0 var(--radius-lg) var(--radius-lg); }
    .foot:empty { display: none; }
    @keyframes fade { from { opacity: 0; } }
    @keyframes rise { from { opacity: 0; transform: translateY(16px) scale(.98); } }
    @media (max-width: 599px) {
      .overlay { padding: 16px 8px; }
      .head, .body { padding-left: 16px; padding-right: 16px; }
      .foot { padding: 12px 16px; }
    }
  `,
})
export class Dialog {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly icon = input('fa-circle-info');
  readonly tone = input('b-brand');
  readonly size = input<'' | 'sm'>('');
  readonly closed = output<void>();
}
