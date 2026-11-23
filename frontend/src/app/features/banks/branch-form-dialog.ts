import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { INDIAN_STATES } from '../settings/settings.api';
import { Bank, BanksApi, Branch, SaveBranch } from './banks.api';

const IFSC = /^[A-Z]{4}0[A-Z0-9]{6}$/;

@Component({
  selector: 'vp-branch-form-dialog',
  imports: [Dialog, ReactiveFormsModule],
  template: `
    <vp-dialog [title]="branch() ? 'Edit Branch' : 'Add Branch'" [subtitle]="bank().name" icon="fa-code-branch" (closed)="closed.emit()">
      <form id="branchForm" class="form-grid" [formGroup]="form" (ngSubmit)="save()" novalidate>
        @if (error()) { <div class="alert-inline danger col-2"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }
        <div class="fld col-2" [class.invalid]="bad('name')">
          <label for="brname">Branch Name <span class="req">*</span></label>
          <input id="brname" formControlName="name" placeholder="Vesu Branch" />
          @if (bad('name')) { <span class="err">{{ server()['name'] ?? 'Enter the branch name.' }}</span> }
        </div>
        <div class="fld" [class.invalid]="bad('ifsc')">
          <label for="ifsc">IFSC</label>
          <input id="ifsc" class="upper tnum" formControlName="ifsc" [placeholder]="(bank().ifscPrefix ?? 'SBIN') + '0001234'" maxlength="11" />
          @if (bad('ifsc')) { <span class="err">{{ server()['ifsc'] ?? ifscError() }}</span> }
        </div>
        <div class="fld">
          <label for="city">City</label>
          <input id="city" formControlName="city" placeholder="Surat" />
        </div>
        <div class="fld">
          <label for="state">State</label>
          <select id="state" formControlName="state">
            <option value="">Select state</option>
            @for (s of states; track s.code) { <option [value]="s.name">{{ s.name }}</option> }
          </select>
        </div>
        <div class="fld">
          <label for="contact">Contact Person</label>
          <input id="contact" formControlName="contactPerson" placeholder="Branch Manager" />
        </div>
        <div class="fld" [class.invalid]="bad('phone')">
          <label for="phone">Phone</label>
          <input id="phone" formControlName="phone" placeholder="0261 278 1122" inputmode="tel" />
          @if (bad('phone')) { <span class="err">Use digits and spaces only.</span> }
        </div>
        <div class="fld" [class.invalid]="bad('email')">
          <label for="bemail">Email</label>
          <input id="bemail" type="email" formControlName="email" placeholder="branch@bank.co.in" />
          @if (bad('email')) { <span class="err">{{ server()['email'] ?? 'Enter a valid email address.' }}</span> }
        </div>
        <div class="fld col-2">
          <label for="baddr">Address</label>
          <input id="baddr" formControlName="address" placeholder="Ground Floor, VR Mall Road, Vesu" />
        </div>
        <label class="check col-2"><input type="checkbox" formControlName="isActive" /> Active — sends us valuation work</label>
      </form>
      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="submit" form="branchForm" [disabled]="busy()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> {{ branch() ? 'Save' : 'Add Branch' }}
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .upper { text-transform: uppercase; }
    .check { display: flex; align-items: center; gap: 8px; font-size: 13px; color: var(--ink-2); cursor: pointer; }
    .check input { width: 16px; height: 16px; }
  `,
})
export class BranchFormDialog implements OnInit {
  private readonly api = inject(BanksApi);

  readonly bank = input.required<Bank>();
  readonly branch = input<Branch | null>(null);
  readonly saved = output<Branch>();
  readonly closed = output<void>();

  protected readonly states = INDIAN_STATES;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly server = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    ifsc: ['', (c: { value: string }) => (!c.value || this.ifscOk(c.value) ? null : { ifsc: true })],
    city: ['', Validators.maxLength(100)],
    state: [''],
    address: ['', Validators.maxLength(500)],
    contactPerson: ['', Validators.maxLength(100)],
    phone: ['', [Validators.pattern(/^[0-9 +()-]*$/), Validators.maxLength(20)]],
    email: ['', [Validators.email, Validators.maxLength(256)]],
    isActive: [true],
  });

  ngOnInit(): void {
    const b = this.branch();
    if (b) {
      this.form.setValue({
        name: b.name, ifsc: b.ifsc ?? '', city: b.city ?? '', state: b.state ?? '', address: b.address ?? '',
        contactPerson: b.contactPerson ?? '', phone: b.phone ?? '', email: b.email ?? '', isActive: b.isActive,
      });
    }
    this.form.valueChanges.subscribe(() => this.server.set({}));
  }

  private ifscOk(value: string): boolean {
    const v = value.trim().toUpperCase();
    const prefix = this.bank().ifscPrefix;
    return IFSC.test(v) && (!prefix || v.startsWith(prefix));
  }

  protected ifscError(): string {
    const prefix = this.bank().ifscPrefix;
    const v = this.form.controls.ifsc.value.trim().toUpperCase();
    return prefix && IFSC.test(v) ? `${this.bank().shortCode} IFSC codes start with ${prefix}.` : 'Enter a valid 11-character IFSC.';
  }

  protected bad(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return (c.touched && c.invalid) || !!this.server()[name];
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const blank = (s: string) => s.trim() || null;
    const body: SaveBranch = {
      name: v.name.trim(), ifsc: blank(v.ifsc)?.toUpperCase() ?? null, city: blank(v.city), state: blank(v.state),
      address: blank(v.address), contactPerson: blank(v.contactPerson), phone: blank(v.phone), email: blank(v.email), isActive: v.isActive,
    };
    this.busy.set(true);
    this.error.set(null);
    try {
      const b = this.branch();
      const bankId = this.bank().id;
      this.saved.emit(await firstValueFrom(b ? this.api.updateBranch(bankId, b.id, body) : this.api.createBranch(bankId, body)));
    } catch (e) {
      const p = toProblem(e);
      this.server.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
