import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { ROLES, SaveUserRequest, TemporaryPasswordResponse, USER_STATUSES, UserListItem, UsersApi, UserStatus } from './users.api';

export type UserSaved = { kind: 'created'; result: TemporaryPasswordResponse } | { kind: 'updated'; user: UserListItem };

@Component({
  selector: 'vp-user-form-dialog',
  imports: [Dialog, ReactiveFormsModule],
  template: `
    <vp-dialog
      [title]="user() ? 'Edit User' : 'Add User'"
      [subtitle]="user() ? user()!.email : 'Create a new team member'"
      [icon]="user() ? 'fa-user-pen' : 'fa-user-plus'"
      (closed)="closed.emit()"
    >
      <form id="userForm" [formGroup]="form" (ngSubmit)="save()" novalidate>
        @if (error()) {
          <div class="alert" role="alert"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div>
        }
        <div class="form-grid">
          <div class="fld col-2" [class.invalid]="invalid('fullName')">
            <label for="fullName">Full Name <span class="req">*</span></label>
            <input id="fullName" formControlName="fullName" placeholder="Full name" autocomplete="off" />
            @if (invalid('fullName')) { <span class="err">{{ fieldError('fullName') ?? 'Enter the full name.' }}</span> }
          </div>

          <div class="fld" [class.invalid]="invalid('role')">
            <label for="role">Role <span class="req">*</span></label>
            <select id="role" formControlName="role">
              @for (r of roles; track r) { <option [value]="r">{{ r }}</option> }
            </select>
            @if (isSelf()) { <span class="hint">You can't change your own role.</span> }
            @if (fieldError('role')) { <span class="err">{{ fieldError('role') }}</span> }
          </div>

          <div class="fld">
            <label for="status">Status</label>
            <select id="status" formControlName="status">
              @for (s of statuses; track s.value) {
                <option [value]="s.value" [disabled]="isSelf() && s.value === 'Inactive'">{{ s.label }}</option>
              }
            </select>
            <span class="hint">{{ statusHint() }}</span>
          </div>

          <div class="fld" [class.invalid]="invalid('email')">
            <label for="email">Email <span class="req">*</span></label>
            <input id="email" type="email" formControlName="email" placeholder="name@firm.in" inputmode="email" autocomplete="off" />
            @if (invalid('email')) { <span class="err">{{ fieldError('email') ?? 'Enter a valid email address.' }}</span> }
          </div>

          <div class="fld" [class.invalid]="invalid('phone')">
            <label for="phone">Phone</label>
            <div class="fld-pre"><span>+91</span><input id="phone" formControlName="phone" placeholder="98250 00000" inputmode="tel" /></div>
            @if (invalid('phone')) { <span class="err">{{ fieldError('phone') ?? 'Use digits and spaces only.' }}</span> }
          </div>

          <div class="fld col-2">
            <label for="reg">Registration / License No.</label>
            <input id="reg" class="tnum" formControlName="registrationNo" placeholder="RV/2024/XX/0000" />
            <span class="hint">IBBI valuer registration, architect licence, or leave blank.</span>
          </div>
        </div>
        @if (!user()) {
          <p class="note"><i class="fa-solid fa-key"></i> A temporary password is created and shown once after saving. The user must change it at first sign-in.</p>
        }
      </form>

      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="submit" form="userForm" [disabled]="busy()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> {{ user() ? 'Save' : 'Add User' }}
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .alert { display: flex; gap: 8px; align-items: center; margin-bottom: 16px; padding: 10px 12px; border-radius: 9px;
      background: var(--danger-soft); color: var(--danger); font-size: 13.5px; }
    .note { display: flex; gap: 8px; align-items: center; margin: 18px 0 0; padding: 10px 12px; border-radius: 9px;
      background: var(--gold-soft); color: #8a6516; font-size: 12.5px; }
  `,
})
export class UserFormDialog implements OnInit {
  private readonly api = inject(UsersApi);

  readonly user = input<UserListItem | null>(null);
  readonly isSelf = input(false);
  readonly saved = output<UserSaved>();
  readonly closed = output<void>();

  protected readonly roles = ROLES;
  protected readonly statuses = USER_STATUSES;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly serverErrors = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.maxLength(150)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    phone: ['', [Validators.pattern(/^[0-9 +()-]*$/), Validators.maxLength(20)]],
    registrationNo: ['', Validators.maxLength(60)],
    role: ['Junior Valuer', Validators.required],
    status: ['Active' as UserStatus],
  });

  private readonly statusValue = signal<UserStatus>('Active');
  protected readonly statusHint = computed(
    () =>
      ({
        Active: 'Can sign in and be assigned work.',
        OnLeave: 'Can sign in; shown as unavailable for new work.',
        Inactive: 'Cannot sign in. Signed out within a minute.',
      })[this.statusValue()],
  );

  ngOnInit(): void {
    const u = this.user();
    if (u) {
      this.form.setValue({
        fullName: u.fullName,
        email: u.email,
        phone: u.phone ?? '',
        registrationNo: u.registrationNo ?? '',
        role: u.role,
        status: u.status,
      });
      this.statusValue.set(u.status);
    }
    if (this.isSelf()) this.form.controls.role.disable();
    this.form.controls.status.valueChanges.subscribe((s) => this.statusValue.set(s));
    this.form.valueChanges.subscribe(() => this.serverErrors.set({}));
  }

  protected invalid(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return (c.touched && c.invalid) || !!this.serverErrors()[name];
  }

  protected fieldError(name: string): string | undefined {
    return this.serverErrors()[name];
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body: SaveUserRequest = {
      ...v,
      fullName: v.fullName.trim(),
      email: v.email.trim(),
      phone: v.phone.trim() || null,
      registrationNo: v.registrationNo.trim() || null,
    };

    this.busy.set(true);
    this.error.set(null);
    try {
      const u = this.user();
      if (u) this.saved.emit({ kind: 'updated', user: await firstValueFrom(this.api.update(u.id, body)) });
      else this.saved.emit({ kind: 'created', result: await firstValueFrom(this.api.create(body)) });
    } catch (e) {
      const p = toProblem(e);
      this.serverErrors.set(p.fields);
      if (Object.keys(p.fields).length === 0) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
