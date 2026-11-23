import { Component, effect, inject, input, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/ui/toast';
import { CommunicationSettings, SettingsApi, SmsProvider, UpdateCommunication } from './settings.api';

/**
 * SMTP and SMS gateway. Secrets are write-only: the form shows whether one is saved, and
 * leaving the field empty keeps it.
 */
@Component({
  selector: 'vp-communication-section',
  imports: [ReactiveFormsModule],
  template: `
    <form class="card" [formGroup]="form" (ngSubmit)="save()" novalidate>
      <div class="card-head">
        <span class="ic b-brand"><i class="fa-solid fa-envelope"></i></span>
        <h3>Email (SMTP)</h3>
        <span class="sub">Used for notifications and sending reports.</span>
      </div>
      <div class="card-pad form-grid">
        <div class="fld" [class.invalid]="bad('smtpHost')">
          <label for="host">SMTP Host</label>
          <input id="host" formControlName="smtpHost" placeholder="smtp.gmail.com" autocomplete="off" />
          @if (bad('smtpHost')) { <span class="err">{{ err()['smtpHost'] ?? 'Enter a host name like smtp.gmail.com.' }}</span> }
        </div>
        <div class="fld" [class.invalid]="bad('smtpPort')">
          <label for="port">Port</label>
          <input id="port" type="number" formControlName="smtpPort" min="1" max="65535" />
          <span class="hint">587 (STARTTLS) or 465 (SSL) for most providers.</span>
        </div>
        <div class="fld">
          <label for="user">Username</label>
          <input id="user" formControlName="smtpUsername" autocomplete="off" placeholder="Usually the full email address" />
        </div>
        <div class="fld">
          <label for="pass">Password</label>
          <input id="pass" type="password" formControlName="smtpPassword" autocomplete="new-password"
            [placeholder]="value().hasSmtpPassword ? '•••••••• saved · type to replace' : 'App password'" />
          @if (value().hasSmtpPassword && canEdit()) {
            <label class="inline"><input type="checkbox" formControlName="clearSmtpPassword" /> Remove saved password</label>
          }
        </div>
        <div class="fld" [class.invalid]="bad('fromEmail')">
          <label for="from">From Email</label>
          <input id="from" type="email" formControlName="fromEmail" placeholder="reports@firm.in" />
          @if (bad('fromEmail')) { <span class="err">{{ err()['fromEmail'] ?? 'Enter a valid email address.' }}</span> }
        </div>
        <div class="fld">
          <label for="fromName">From Name</label>
          <input id="fromName" formControlName="fromName" placeholder="Om Engineers" />
        </div>
        <div class="fld col-2">
          <label class="inline"><input type="checkbox" formControlName="smtpUseSsl" /> Use a secure connection (TLS/SSL)</label>
        </div>
      </div>

      <div class="card-head sms">
        <span class="ic b-teal"><i class="fa-solid fa-comment-sms"></i></span>
        <h3>SMS Gateway</h3>
        <span class="sub">For site visit reminders and alerts. Sending starts with notifications in Phase 4.</span>
      </div>
      <div class="card-pad form-grid">
        <div class="fld">
          <label for="provider">Provider</label>
          <select id="provider" formControlName="smsProvider">
            @for (p of providers; track p.value) { <option [value]="p.value">{{ p.label }}</option> }
          </select>
        </div>
        <div class="fld" [class.invalid]="bad('smsSenderId')">
          <label for="sender">Sender ID</label>
          <input id="sender" class="tnum" formControlName="smsSenderId" placeholder="OMENGG" maxlength="11" />
          @if (bad('smsSenderId')) { <span class="err">{{ err()['smsSenderId'] ?? 'Up to 11 letters or digits.' }}</span> }
        </div>
        <div class="fld col-2">
          <label for="key">API Key</label>
          <input id="key" type="password" formControlName="smsApiKey" autocomplete="new-password"
            [placeholder]="value().hasSmsApiKey ? '•••••••• saved · type to replace' : 'From your SMS provider'" />
          @if (value().hasSmsApiKey && canEdit()) {
            <label class="inline"><input type="checkbox" formControlName="clearSmsApiKey" /> Remove saved API key</label>
          }
        </div>
      </div>

      @if (canEdit()) {
        <div class="section-foot">
          <button class="btn btn-ghost" type="button" (click)="test()" [disabled]="testing() || form.dirty || !value().smtpHost"
            [title]="form.dirty ? 'Save first' : 'Send a test email to ' + userEmail">
            <i class="fa-solid" [class]="testing() ? 'fa-spinner fa-spin' : 'fa-paper-plane'"></i> Send test email
          </button>
          @if (form.dirty) { <span class="status">Unsaved changes</span> }
          <button class="btn btn-ghost" type="button" [disabled]="!form.dirty || busy()" (click)="reset()">Discard</button>
          <button class="btn btn-pri" type="submit" [disabled]="!form.dirty || busy()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-floppy-disk'"></i> Save Changes
          </button>
        </div>
      }
      @if (testResult(); as r) {
        <div class="result"><div class="alert-inline" [class]="r.ok ? 'ok' : 'danger'"><i class="fa-solid" [class]="r.ok ? 'fa-circle-check' : 'fa-circle-exclamation'"></i> {{ r.text }}</div></div>
      }
    </form>
  `,
  styles: `
    .ic { display: grid; place-items: center; width: 32px; height: 32px; border-radius: 9px; font-size: 14px; }
    .sms { border-top: 1px solid var(--line-2); }
    label.inline { display: flex; align-items: center; gap: 8px; font-weight: 500; color: var(--ink-2); cursor: pointer; }
    label.inline input { width: 16px; height: 16px; }
    .section-foot .btn:first-child { margin-right: auto; }
    .result { padding: 0 20px 16px; background: #fafbfe; border-radius: 0 0 var(--radius) var(--radius); }
  `,
})
export class CommunicationSection {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(ToastService);
  protected readonly userEmail = inject(AuthService).user()?.email ?? 'you';

  readonly value = input.required<CommunicationSettings>();
  readonly canEdit = input(false);
  readonly saved = output<CommunicationSettings>();

  protected readonly providers: { value: SmsProvider; label: string }[] = [
    { value: 'None', label: 'Not set up' },
    { value: 'Msg91', label: 'MSG91' },
    { value: 'Twilio', label: 'Twilio' },
    { value: 'TextLocal', label: 'TextLocal' },
  ];
  protected readonly busy = signal(false);
  protected readonly testing = signal(false);
  protected readonly testResult = signal<{ ok: boolean; text: string } | null>(null);
  protected readonly err = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    smtpHost: ['', [Validators.maxLength(200), Validators.pattern(/^[A-Za-z0-9.-]*$/)]],
    smtpPort: [587, [Validators.required, Validators.min(1), Validators.max(65535)]],
    smtpUseSsl: [true],
    smtpUsername: ['', Validators.maxLength(256)],
    smtpPassword: [''],
    clearSmtpPassword: [false],
    fromEmail: ['', [Validators.email, Validators.maxLength(256)]],
    fromName: ['', Validators.maxLength(100)],
    smsProvider: ['None' as SmsProvider],
    smsSenderId: ['', [Validators.maxLength(11), Validators.pattern(/^[A-Za-z0-9]*$/)]],
    smsApiKey: [''],
    clearSmsApiKey: [false],
  });

  constructor() {
    effect(() => {
      this.fill(this.value());
      if (this.canEdit()) this.form.enable(); else this.form.disable();
    });
    this.form.valueChanges.subscribe(() => this.err.set({}));
  }

  protected bad(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return (c.touched && c.invalid) || !!this.err()[name];
  }

  protected reset(): void {
    this.fill(this.value());
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body: UpdateCommunication = {
      smtpHost: v.smtpHost.trim() || null,
      smtpPort: Number(v.smtpPort),
      smtpUseSsl: v.smtpUseSsl,
      smtpUsername: v.smtpUsername.trim() || null,
      smtpPassword: v.clearSmtpPassword ? '' : v.smtpPassword || null,
      fromEmail: v.fromEmail.trim() || null,
      fromName: v.fromName.trim() || null,
      smsProvider: v.smsProvider,
      smsSenderId: v.smsSenderId.trim() || null,
      smsApiKey: v.clearSmsApiKey ? '' : v.smsApiKey || null,
    };
    this.busy.set(true);
    this.testResult.set(null);
    try {
      this.saved.emit(await firstValueFrom(this.api.updateCommunication(body)));
      this.toast.ok('Email & SMS settings saved');
    } catch (e) {
      const p = toProblem(e);
      this.err.set(p.fields);
      this.toast.error('Could not save', p.message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async test(): Promise<void> {
    this.testing.set(true);
    this.testResult.set(null);
    try {
      const r = await firstValueFrom(this.api.testEmail());
      this.testResult.set({ ok: true, text: r.message });
    } catch (e) {
      this.testResult.set({ ok: false, text: toProblem(e).message });
    } finally {
      this.testing.set(false);
    }
  }

  private fill(c: CommunicationSettings): void {
    this.form.reset({
      smtpHost: c.smtpHost ?? '',
      smtpPort: c.smtpPort,
      smtpUseSsl: c.smtpUseSsl,
      smtpUsername: c.smtpUsername ?? '',
      smtpPassword: '',
      clearSmtpPassword: false,
      fromEmail: c.fromEmail ?? '',
      fromName: c.fromName ?? '',
      smsProvider: c.smsProvider,
      smsSenderId: c.smsSenderId ?? '',
      smsApiKey: '',
      clearSmsApiKey: false,
    });
  }
}
