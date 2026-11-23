import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';
import { GstSettings, INDIAN_STATES, SettingsApi } from './settings.api';

const GSTIN = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/;

/** Indian financial year label for a date, e.g. 26-27 for April 2026 – March 2027. */
export function financialYear(d = new Date()): string {
  const start = d.getMonth() >= 3 ? d.getFullYear() : d.getFullYear() - 1;
  return `${String(start % 100).padStart(2, '0')}-${String((start + 1) % 100).padStart(2, '0')}`;
}

@Component({
  selector: 'vp-gst-section',
  imports: [ReactiveFormsModule],
  template: `
    <form class="card" [formGroup]="form" (ngSubmit)="save()" novalidate>
      <div class="card-head">
        <h3>GST & numbering</h3>
        <span class="sub">Used on tax invoices (Phase 4) and new assignment numbers.</span>
      </div>
      <div class="card-pad form-grid">
        <div class="fld" [class.invalid]="bad('gstin')">
          <label for="gstin">GSTIN</label>
          <input id="gstin" class="tnum upper" formControlName="gstin" placeholder="24ABCDE1234F1Z5" maxlength="15" />
          @if (bad('gstin')) { <span class="err">{{ err()['gstin'] ?? 'Enter a valid 15-character GSTIN.' }}</span> }
          @else if (gstinState()) { <span class="hint">Registered in {{ gstinState() }}</span> }
        </div>
        <div class="fld">
          <label for="rate">Default GST Rate</label>
          <select id="rate" formControlName="defaultGstRate">
            <option [ngValue]="18">18% (Services)</option>
            <option [ngValue]="12">12%</option>
            <option [ngValue]="5">5%</option>
            <option [ngValue]="0">0% / Exempt</option>
          </select>
        </div>
        <div class="fld" [class.invalid]="bad('stateCode')">
          <label for="code">State Code</label>
          <select id="code" formControlName="stateCode">
            <option value="">Select</option>
            @for (s of states; track s.code) { <option [value]="s.code">{{ s.code }} · {{ s.name }}</option> }
          </select>
          @if (bad('stateCode')) { <span class="err">{{ err()['stateCode'] ?? 'Must match the first two digits of the GSTIN.' }}</span> }
        </div>
        <div class="fld">
          <label for="pos">Place of Supply</label>
          <select id="pos" formControlName="placeOfSupply">
            <option value="">Select</option>
            @for (s of states; track s.code) { <option [value]="s.name">{{ s.name }}</option> }
          </select>
        </div>
        <div class="fld" [class.invalid]="bad('invoicePrefix')">
          <label for="inv">Invoice Prefix <span class="req">*</span></label>
          <input id="inv" class="tnum upper" formControlName="invoicePrefix" maxlength="10" />
          <span class="hint">Next invoice looks like <b class="tnum">{{ invoiceSample() }}</b></span>
        </div>
        <div class="fld" [class.invalid]="bad('assignmentPrefix')">
          <label for="asg">Assignment Prefix <span class="req">*</span></label>
          <input id="asg" class="tnum upper" formControlName="assignmentPrefix" maxlength="10" />
          <span class="hint">Next assignment looks like <b class="tnum">{{ assignmentSample() }}</b></span>
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
  styles: `.upper { text-transform: uppercase; }`,
})
export class GstSection {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(ToastService);

  readonly value = input.required<GstSettings>();
  readonly canEdit = input(false);
  readonly saved = output<GstSettings>();

  protected readonly states = INDIAN_STATES;
  protected readonly busy = signal(false);
  protected readonly err = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    gstin: ['', Validators.pattern(GSTIN)],
    defaultGstRate: [18],
    stateCode: [''],
    placeOfSupply: [''],
    invoicePrefix: ['INV', [Validators.required, Validators.pattern(/^[A-Za-z0-9-]{1,10}$/)]],
    assignmentPrefix: ['VAL', [Validators.required, Validators.pattern(/^[A-Za-z0-9-]{1,10}$/)]],
  });

  private readonly v = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly gstinState = computed(() => {
    const g = (this.v().gstin ?? '').toUpperCase();
    return GSTIN.test(g) ? INDIAN_STATES.find((s) => s.code === g.slice(0, 2))?.name : undefined;
  });
  protected readonly invoiceSample = computed(() => `${(this.v().invoicePrefix || 'INV').toUpperCase()}/${financialYear()}/0001`);
  protected readonly assignmentSample = computed(() => `${(this.v().assignmentPrefix || 'VAL').toUpperCase()}/${new Date().getFullYear()}/0001`);

  constructor() {
    effect(() => {
      this.fill(this.value());
      if (this.canEdit()) this.form.enable(); else this.form.disable();
    });
    this.form.valueChanges.subscribe(() => this.err.set({}));

    // A valid GSTIN fills in the state code and place of supply.
    this.form.controls.gstin.valueChanges.subscribe((g) => {
      const upper = g.toUpperCase();
      if (!GSTIN.test(upper)) return;
      const state = INDIAN_STATES.find((s) => s.code === upper.slice(0, 2));
      if (state) this.form.patchValue({ stateCode: state.code, placeOfSupply: this.form.controls.placeOfSupply.value || state.name }, { emitEvent: false });
    });
  }

  protected bad(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return (c.touched && c.invalid) || !!this.err()[name];
  }

  protected reset(): void {
    this.fill(this.value());
  }

  protected async save(): Promise<void> {
    this.form.controls.gstin.setValue(this.form.controls.gstin.value.trim().toUpperCase());
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body: GstSettings = {
      gstin: v.gstin || null,
      defaultGstRate: Number(v.defaultGstRate),
      stateCode: v.stateCode || null,
      placeOfSupply: v.placeOfSupply || null,
      invoicePrefix: v.invoicePrefix.trim().toUpperCase(),
      assignmentPrefix: v.assignmentPrefix.trim().toUpperCase(),
    };
    this.busy.set(true);
    try {
      this.saved.emit(await firstValueFrom(this.api.updateGst(body)));
      this.toast.ok('GST & numbering saved');
    } catch (e) {
      const p = toProblem(e);
      this.err.set(p.fields);
      this.toast.error('Could not save', p.message);
    } finally {
      this.busy.set(false);
    }
  }

  private fill(g: GstSettings): void {
    this.form.reset({
      gstin: g.gstin ?? '',
      defaultGstRate: g.defaultGstRate,
      stateCode: g.stateCode ?? '',
      placeOfSupply: g.placeOfSupply ?? '',
      invoicePrefix: g.invoicePrefix,
      assignmentPrefix: g.assignmentPrefix,
    });
  }
}
