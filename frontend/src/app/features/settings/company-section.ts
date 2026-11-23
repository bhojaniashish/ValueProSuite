import { Component, effect, inject, input, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';
import { CompanySettings, INDIAN_STATES, SettingsApi } from './settings.api';

@Component({
  selector: 'vp-company-section',
  imports: [ReactiveFormsModule],
  template: `
    <form class="card" [formGroup]="form" (ngSubmit)="save()" novalidate>
      <div class="card-head">
        <h3>Company profile</h3>
        <span class="sub">Shown on reports, invoices and the sign-in page.</span>
      </div>
      <div class="card-pad form-grid">
        <div class="fld col-2" [class.invalid]="bad('name')">
          <label for="name">Company / Firm Name <span class="req">*</span></label>
          <input id="name" formControlName="name" placeholder="Om Engineers & Valuers" />
          @if (bad('name')) { <span class="err">{{ err()['name'] ?? 'Enter the firm name.' }}</span> }
        </div>
        <div class="fld">
          <label for="rv">Registered Valuer No.</label>
          <input id="rv" class="tnum" formControlName="registeredValuerNo" placeholder="RV/2018/MH/0412" />
        </div>
        <div class="fld">
          <label for="ibbi">IBBI Registration</label>
          <input id="ibbi" class="tnum" formControlName="ibbiRegistration" placeholder="IBBI/RV/05/2019/11234" />
        </div>
        <div class="fld" [class.invalid]="bad('contactEmail')">
          <label for="email">Contact Email</label>
          <input id="email" type="email" formControlName="contactEmail" placeholder="office@firm.in" inputmode="email" />
          @if (bad('contactEmail')) { <span class="err">{{ err()['contactEmail'] ?? 'Enter a valid email address.' }}</span> }
        </div>
        <div class="fld" [class.invalid]="bad('phone')">
          <label for="phone">Phone</label>
          <div class="fld-pre"><span>+91</span><input id="phone" formControlName="phone" placeholder="0261 245 6789" inputmode="tel" /></div>
          @if (bad('phone')) { <span class="err">{{ err()['phone'] ?? 'Use digits and spaces only.' }}</span> }
        </div>
        <div class="fld col-2">
          <label for="addr">Office Address</label>
          <textarea id="addr" rows="3" formControlName="address" placeholder="301, Silver Business Hub, Adajan Road, Surat - 395009"></textarea>
        </div>
        <div class="fld">
          <label for="state">State</label>
          <select id="state" formControlName="state">
            <option value="">Select state</option>
            @for (s of states; track s.code) { <option [value]="s.name">{{ s.name }}</option> }
          </select>
        </div>
      </div>
      @if (canEdit()) {
        <div class="section-foot">
          @if (form.dirty) { <span class="status">Unsaved changes</span> }
          <button class="btn btn-ghost" type="button" [disabled]="!form.dirty || busy()" (click)="reset()">Discard</button>
          <button class="btn btn-pri" type="submit" [disabled]="!form.dirty || busy()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-floppy-disk'"></i> Save Changes
          </button>
        </div>
      }
    </form>
  `,
  styles: `textarea { height: auto !important; padding: 10px 13px !important; resize: vertical; }`,
})
export class CompanySection {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(ToastService);

  readonly value = input.required<CompanySettings>();
  readonly canEdit = input(false);
  readonly saved = output<CompanySettings>();

  protected readonly states = INDIAN_STATES;
  protected readonly busy = signal(false);
  protected readonly err = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    registeredValuerNo: ['', Validators.maxLength(60)],
    ibbiRegistration: ['', Validators.maxLength(60)],
    contactEmail: ['', [Validators.email, Validators.maxLength(256)]],
    phone: ['', [Validators.pattern(/^[0-9 +()-]*$/), Validators.maxLength(30)]],
    address: ['', Validators.maxLength(500)],
    state: [''],
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
    const body: CompanySettings = {
      name: v.name.trim(),
      registeredValuerNo: v.registeredValuerNo.trim() || null,
      ibbiRegistration: v.ibbiRegistration.trim() || null,
      contactEmail: v.contactEmail.trim() || null,
      phone: v.phone.trim() || null,
      address: v.address.trim() || null,
      state: v.state || null,
    };
    this.busy.set(true);
    try {
      const saved = await firstValueFrom(this.api.updateCompany(body));
      this.saved.emit(saved);
      this.toast.ok('Company profile saved');
    } catch (e) {
      const p = toProblem(e);
      this.err.set(p.fields);
      this.toast.error('Could not save', p.message);
    } finally {
      this.busy.set(false);
    }
  }

  private fill(c: CompanySettings): void {
    this.form.reset({
      name: c.name,
      registeredValuerNo: c.registeredValuerNo ?? '',
      ibbiRegistration: c.ibbiRegistration ?? '',
      contactEmail: c.contactEmail ?? '',
      phone: c.phone ?? '',
      address: c.address ?? '',
      state: c.state ?? '',
    });
  }
}
