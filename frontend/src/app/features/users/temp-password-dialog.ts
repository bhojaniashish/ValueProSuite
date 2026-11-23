import { Component, input, output, signal } from '@angular/core';
import { Dialog } from '../../core/ui/dialog';

/** Shows a generated password once, with copy, so the admin can hand it over. */
@Component({
  selector: 'vp-temp-password-dialog',
  imports: [Dialog],
  template: `
    <vp-dialog [title]="title()" [subtitle]="name()" icon="fa-key" tone="b-gold" size="sm" (closed)="closed.emit()">
      <p class="lead">Share this temporary password with {{ firstName() }} in person or by phone. It is shown only once.</p>
      <div class="pw">
        <code>{{ password() }}</code>
        <button class="btn btn-soft btn-sm" type="button" (click)="copy()">
          <i class="fa-regular" [class]="copied() ? 'fa-circle-check' : 'fa-copy'"></i> {{ copied() ? 'Copied' : 'Copy' }}
        </button>
      </div>
      <p class="small muted">{{ firstName() }} will be asked to choose a new password after signing in with {{ email() }}.</p>
      <ng-container footer>
        <button class="btn btn-pri" type="button" (click)="closed.emit()">Done</button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .lead { margin: 0 0 14px; line-height: 1.55; }
    .pw { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 14px;
      margin-bottom: 14px; border: 1px dashed var(--gold); border-radius: 10px; background: var(--gold-soft); }
    code { font-size: 20px; font-weight: 700; letter-spacing: .06em; color: var(--ink); word-break: break-all; }
    .small { margin: 0; }
  `,
})
export class TempPasswordDialog {
  readonly title = input('Temporary password');
  readonly name = input.required<string>();
  readonly email = input.required<string>();
  readonly password = input.required<string>();
  readonly closed = output<void>();

  protected readonly copied = signal(false);
  protected firstName(): string {
    return this.name().split(' ')[0];
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.password());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      /* clipboard blocked: the password stays visible to copy by hand */
    }
  }
}
