import { Component, computed, inject, signal } from '@angular/core';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { toProblem } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/ui/toast';

const matches = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirm')?.value ? null : { mismatch: true };

/** Own password change; required before anything else when the admin issued a temporary password. */
@Component({
  selector: 'vp-change-password',
  imports: [ReactiveFormsModule],
  template: `
    <div class="card wrap">
      <div class="card-head">
        <span class="ic b-brand"><i class="fa-solid fa-lock"></i></span>
        <div>
          <h3>{{ forced() ? 'Choose your password' : 'Change password' }}</h3>
          <span class="sub">{{ forced() ? 'You signed in with a temporary password. Pick your own to continue.' : 'Other devices will be signed out.' }}</span>
        </div>
      </div>
      <form class="card-pad" [formGroup]="form" (ngSubmit)="save()" novalidate>
        <div class="fld" [class.invalid]="err('currentPassword')">
          <label for="cur">{{ forced() ? 'Temporary password' : 'Current password' }}</label>
          <input id="cur" type="password" formControlName="currentPassword" autocomplete="current-password" />
          @if (err('currentPassword')) { <span class="err">{{ err('currentPassword') }}</span> }
        </div>
        <div class="fld" [class.invalid]="err('newPassword')">
          <label for="new">New password</label>
          <input id="new" type="password" formControlName="newPassword" autocomplete="new-password" />
          <span class="hint">At least 10 characters, with upper and lower case letters and a number.</span>
          @if (err('newPassword')) { <span class="err">{{ err('newPassword') }}</span> }
        </div>
        <div class="fld" [class.invalid]="err('confirm')">
          <label for="confirm">Confirm new password</label>
          <input id="confirm" type="password" formControlName="confirm" autocomplete="new-password" />
          @if (err('confirm')) { <span class="err">{{ err('confirm') }}</span> }
        </div>
        @if (error()) { <div class="alert" role="alert"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }
        <div class="row">
          @if (forced()) {
            <button class="btn btn-ghost" type="button" (click)="auth.logout()">Sign out</button>
          }
          <button class="btn btn-pri" type="submit" [disabled]="busy()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> Save password
          </button>
        </div>
      </form>
    </div>
  `,
  styles: `
    .wrap { max-width: 480px; margin: 8px auto; }
    .ic { display: grid; place-items: center; flex: none; width: 38px; height: 38px; border-radius: 10px; }
    .card-head { flex-wrap: nowrap; align-items: flex-start; }
    .card-head .sub { display: block; margin-top: 2px; }
    form { display: flex; flex-direction: column; gap: 16px; }
    .alert { padding: 10px 12px; border-radius: 9px; background: var(--danger-soft); color: var(--danger); font-size: 13.5px; }
    .row { display: flex; justify-content: flex-end; gap: 10px; }
  `,
})
export class ChangePassword {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly forced = computed(() => !!this.auth.user()?.mustChangePassword);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly server = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.minLength(10), Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).*$/)]],
      confirm: ['', Validators.required],
    },
    { validators: matches },
  );

  constructor() {
    this.form.valueChanges.subscribe(() => this.server.set({}));
  }

  protected err(name: 'currentPassword' | 'newPassword' | 'confirm'): string | null {
    const server = this.server()[name];
    if (server) return server;
    const c = this.form.controls[name];
    if (!c.touched) return null;
    if (c.hasError('required')) return 'Required.';
    if (name === 'newPassword' && c.invalid) return 'At least 10 characters, with upper and lower case letters and a number.';
    if (name === 'confirm' && this.form.hasError('mismatch')) return 'Passwords do not match.';
    return null;
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const v = this.form.getRawValue();
      await this.auth.changePassword(v.currentPassword, v.newPassword);
      this.toast.ok('Password changed');
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      const p = toProblem(e);
      this.server.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
