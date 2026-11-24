import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { INDIAN_STATES } from '../settings/settings.api';
import { Client, CLIENT_TYPES, ClientsApi, ClientType, SaveClient } from './clients.api';

const PAN = /^[A-Z]{5}[0-9]{4}[A-Z]$/;
const GSTIN = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/;

@Component({
  selector: 'vp-client-form-dialog',
  imports: [Dialog, ReactiveFormsModule],
  template: `
    <vp-dialog
      [title]="client() ? 'Edit Client' : 'Add New Client'"
      [subtitle]="client() ? client()!.code + ' · ' + client()!.name : 'Create a new client record'"
      [icon]="client() ? 'fa-user-pen' : 'fa-user-plus'"
      (closed)="closed.emit()"
    >
      <form id="clientForm" [formGroup]="form" (ngSubmit)="save()" novalidate>
        @if (error()) { <div class="alert-inline danger"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }

        <div class="fld">
          <label>Client Type <span class="req">*</span></label>
          <div class="types" role="radiogroup" aria-label="Client type">
            @for (t of types; track t.value) {
              <label class="type" [class.on]="form.controls.type.value === t.value">
                <input type="radio" formControlName="type" [value]="t.value" [attr.aria-label]="t.label" />
                <i class="fa-solid" [class]="t.icon"></i> {{ t.label }}
              </label>
            }
          </div>
        </div>

        <div class="form-grid">
          <div class="fld col-2" [class.invalid]="bad('name')">
            <label for="cname">Name <span class="req">*</span></label>
            <input id="cname" formControlName="name" [placeholder]="namePlaceholder()" autocomplete="off" />
            @if (bad('name')) { <span class="err">{{ server()['name'] ?? 'Enter the client name.' }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('mobile')">
            <label for="mobile">Mobile <span class="req">*</span></label>
            <div class="fld-pre"><span>+91</span><input id="mobile" formControlName="mobile" placeholder="98250 41201" inputmode="tel" /></div>
            @if (bad('mobile')) { <span class="err">{{ server()['mobile'] ?? 'Enter a 10-digit mobile or a landline with STD code.' }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('email')">
            <label for="cemail">Email</label>
            <input id="cemail" type="email" formControlName="email" placeholder="name@example.com" inputmode="email" />
            @if (bad('email')) { <span class="err">{{ server()['email'] ?? 'Enter a valid email address.' }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('pan')">
            <label for="pan">PAN Number</label>
            <input id="pan" class="tnum upper" formControlName="pan" placeholder="AGRPS4521K" maxlength="10" />
            @if (bad('pan')) { <span class="err">{{ server()['pan'] ?? 'Enter a valid PAN, e.g. AGRPS4521K.' }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('gstin')">
            <label for="gst">GST Number</label>
            <input id="gst" class="tnum upper" formControlName="gstin" placeholder="24AABCS1429N1Z5" maxlength="15" />
            @if (bad('gstin')) { <span class="err">{{ server()['gstin'] ?? gstError() }}</span> }
          </div>
          <div class="fld col-2">
            <label for="addr">Address</label>
            <input id="addr" formControlName="address" placeholder="12, Satellite Road, Jodhpur" />
          </div>
          <div class="fld">
            <label for="city">City</label>
            <input id="city" formControlName="city" placeholder="Ahmedabad" />
          </div>
          <div class="fld">
            <label for="state">State</label>
            <select id="state" formControlName="state">
              <option value="">Select state</option>
              @for (s of states; track s.code) { <option [value]="s.name">{{ s.name }}</option> }
            </select>
          </div>
          <div class="fld" [class.invalid]="bad('pincode')">
            <label for="pin">Pincode</label>
            <input id="pin" class="tnum" formControlName="pincode" placeholder="380015" maxlength="6" inputmode="numeric" />
            @if (bad('pincode')) { <span class="err">{{ server()['pincode'] ?? 'Enter a 6-digit pincode.' }}</span> }
          </div>
          <div class="fld col-2">
            <label for="remarks">Remarks</label>
            <textarea id="remarks" rows="2" formControlName="remarks" placeholder="e.g. Home loan applicant – SBI"></textarea>
          </div>
        </div>
      </form>

      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="submit" form="clientForm" [disabled]="busy()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> {{ client() ? 'Save Changes' : 'Add Client' }}
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    form { display: flex; flex-direction: column; gap: 16px; }
    .types { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; }
    @media (max-width: 599px) { .types { grid-template-columns: 1fr 1fr; } }
    .type { position: relative; display: flex; align-items: center; justify-content: center; gap: 8px; padding: 10px 8px; border: 1px solid var(--line);
      border-radius: 9px; font-size: 13px !important; font-weight: 600; color: var(--ink-2) !important; cursor: pointer; text-align: center; }
    .type input { position: absolute; inset: 0; width: 100%; height: 100%; margin: 0; opacity: 0; cursor: pointer; }
    .type:has(input:focus-visible) { outline: 2px solid var(--brand); outline-offset: 2px; }
    .type.on { border-color: var(--brand); background: var(--brand-soft); color: var(--brand) !important; }
    .upper { text-transform: uppercase; }
  `,
})
export class ClientFormDialog implements OnInit {
  private readonly api = inject(ClientsApi);

  readonly client = input<Client | null>(null);
  readonly saved = output<Client>();
  readonly closed = output<void>();

  protected readonly types = CLIENT_TYPES;
  protected readonly states = INDIAN_STATES;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly server = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    type: ['Individual' as ClientType, Validators.required],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    mobile: ['', [Validators.required, Validators.pattern(/^[0-9 +()-]+$/), mobileDigits]],
    email: ['', [Validators.email, Validators.maxLength(256)]],
    pan: ['', upper(PAN)],
    gstin: ['', upper(GSTIN)],
    address: ['', Validators.maxLength(500)],
    city: ['', Validators.maxLength(100)],
    state: [''],
    pincode: ['', Validators.pattern(/^[1-9][0-9]{5}$/)],
    remarks: ['', Validators.maxLength(1000)],
  });

  ngOnInit(): void {
    const c = this.client();
    if (c) {
      this.form.setValue({
        type: c.type, name: c.name, mobile: c.mobile, email: c.email ?? '', pan: c.pan ?? '', gstin: c.gstin ?? '',
        address: c.address ?? '', city: c.city ?? '', state: c.state ?? '', pincode: c.pincode ?? '', remarks: c.remarks ?? '',
      });
    }
    this.form.valueChanges.subscribe(() => this.server.set({}));
  }

  protected namePlaceholder(): string {
    return (
      { Individual: 'Suresh Agarwal', Builder: 'Shree Balaji Developers', BankBranch: 'HDFC – Vesu Branch', Advocate: 'Adv. Mehul Trivedi' } as const
    )[this.form.controls.type.value];
  }

  protected gstError(): string {
    const { pan, gstin } = this.form.getRawValue();
    if (GSTIN.test(gstin.toUpperCase()) && pan && gstin.toUpperCase().slice(2, 12) !== pan.toUpperCase())
      return "The GSTIN should contain the client's PAN (characters 3 to 12).";
    return 'Enter a valid 15-character GSTIN.';
  }

  protected bad(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    if (this.server()[name]) return true;
    if (name === 'gstin' && c.touched && this.panMismatch()) return true;
    return c.touched && c.invalid;
  }

  private panMismatch(): boolean {
    const { pan, gstin } = this.form.getRawValue();
    const g = gstin.trim().toUpperCase();
    return !!pan.trim() && GSTIN.test(g) && g.slice(2, 12) !== pan.trim().toUpperCase();
  }

  protected async save(): Promise<void> {
    if (this.form.invalid || this.panMismatch()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const blank = (s: string) => s.trim() || null;
    const body: SaveClient = {
      type: v.type, name: v.name.trim(), mobile: v.mobile.trim(), email: blank(v.email),
      pan: blank(v.pan)?.toUpperCase() ?? null, gstin: blank(v.gstin)?.toUpperCase() ?? null,
      address: blank(v.address), city: blank(v.city), state: blank(v.state), pincode: blank(v.pincode), remarks: blank(v.remarks),
    };

    this.busy.set(true);
    this.error.set(null);
    try {
      const c = this.client();
      this.saved.emit(await firstValueFrom(c ? this.api.update(c.id, body) : this.api.create(body)));
    } catch (e) {
      const p = toProblem(e);
      this.server.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}

/** Pattern check on the upper-cased value, so "aabcs1429n" is accepted. */
function upper(pattern: RegExp) {
  return (c: { value: string }) => (!c.value || pattern.test(c.value.trim().toUpperCase()) ? null : { pattern: true });
}

function mobileDigits(c: { value: string }) {
  const n = (c.value ?? '').replace(/\D/g, '').length;
  return !c.value || (n >= 10 && n <= 15) ? null : { digits: true };
}
