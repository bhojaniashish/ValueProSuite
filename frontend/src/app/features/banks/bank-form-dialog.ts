import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ReportFormatItem, ReportFormatsApi } from '../report-formats/report-formats.api';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { BankBadge } from './bank-badge';
import { Bank, BanksApi, COMMON_BANKS, DEPRECIATION_METHODS, DepreciationMethod, RATE_BASES, RateBasis, SaveBank } from './banks.api';

@Component({
  selector: 'vp-bank-form-dialog',
  imports: [Dialog, ReactiveFormsModule, BankBadge],
  template: `
    <vp-dialog [title]="bank() ? 'Edit Bank' : 'Add Bank'" subtitle="Bank master configuration" icon="fa-building-columns" (closed)="closed.emit()">
      <form id="bankForm" [formGroup]="form" (ngSubmit)="save()" novalidate>
        @if (error()) { <div class="alert-inline danger"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }

        @if (!bank()) {
          <div class="fld">
            <label for="preset">Quick pick</label>
            <select id="preset" (change)="pick($any($event.target).value)">
              <option value="">Choose a common bank, or fill in below…</option>
              @for (b of presets(); track b.code) { <option [value]="b.code">{{ b.name }} ({{ b.code }})</option> }
            </select>
          </div>
        }

        <div class="form-grid">
          <div class="fld col-2" [class.invalid]="bad('name')">
            <label for="bname">Bank Name <span class="req">*</span></label>
            <input id="bname" formControlName="name" placeholder="State Bank of India" />
            @if (bad('name')) { <span class="err">{{ server()['name'] ?? 'Enter the bank name.' }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('shortCode')">
            <label for="code">Short Code <span class="req">*</span></label>
            <input id="code" class="upper" formControlName="shortCode" placeholder="SBI" maxlength="10" />
            @if (bad('shortCode')) { <span class="err">{{ server()['shortCode'] ?? '2–10 letters or digits, e.g. SBI.' }}</span> }
          </div>
          <div class="fld">
            <label for="color">Brand Color</label>
            <div class="color">
              <vp-bank-badge [code]="v().shortCode || '?'" [color]="v().brandColor" style="--size: 40px" />
              <input type="color" formControlName="brandColor" aria-label="Pick brand colour" />
              <input id="color" class="tnum" formControlName="brandColor" maxlength="7" />
            </div>
          </div>
          <div class="fld" [class.invalid]="bad('ifscPrefix')">
            <label for="ifsc">IFSC Prefix</label>
            <input id="ifsc" class="upper tnum" formControlName="ifscPrefix" placeholder="SBIN" maxlength="4" />
            @if (bad('ifscPrefix')) { <span class="err">{{ server()['ifscPrefix'] ?? 'First 4 letters of the IFSC, e.g. SBIN.' }}</span> }
            @else { <span class="hint">Used to check branch IFSC codes.</span> }
          </div>
          <div class="fld" [class.invalid]="bad('reportFormatId')">
            <label for="format">Report format</label>
            <select id="format" formControlName="reportFormatId">
              <option value="">{{ defaultFormat() ? 'Firm default (' + defaultFormat() + ')' : 'Standard report' }}</option>
              @for (f of formats(); track f.id) { <option [value]="f.id">{{ f.name }}</option> }
            </select>
            @if (server()['reportFormatId']) { <span class="err">{{ server()['reportFormatId'] }}</span> }
            @else { <span class="hint">Formats are set up in Settings → Templates.</span> }
          </div>
          <div class="fld" [class.invalid]="bad('panelCode')">
            <label for="panel">Empanelment no.</label>
            <input id="panel" class="tnum" formControlName="panelCode" placeholder="e.g. SBI/ZO/VAL/112" maxlength="60" />
            <span class="hint">Your panel number with this bank, printed under the signature.</span>
          </div>
        </div>

        <div class="section">
          <h4>Valuation rules</h4>
          <div class="form-grid">
            <div class="fld">
              <label for="basis">Rate Basis</label>
              <select id="basis" formControlName="rateBasis">@for (r of rateBases; track r.value) { <option [value]="r.value">{{ r.label }}</option> }</select>
            </div>
            <div class="fld">
              <label for="dep">Depreciation</label>
              <select id="dep" formControlName="depreciation">@for (d of depreciations; track d.value) { <option [value]="d.value">{{ d.label }}</option> }</select>
            </div>
            <div class="fld" [class.invalid]="bad('distressPercent') || rulesError()">
              <label for="distress">Distress Value %</label>
              <div class="fld-suf"><input id="distress" type="number" min="1" max="100" step="0.5" formControlName="distressPercent" /><span>%</span></div>
            </div>
            <div class="fld" [class.invalid]="bad('realizablePercent') || rulesError()">
              <label for="real">Realizable Value %</label>
              <div class="fld-suf"><input id="real" type="number" min="1" max="100" step="0.5" formControlName="realizablePercent" /><span>%</span></div>
            </div>
            <div class="col-2 hint-line">
              @if (rulesError()) { <span class="err">{{ rulesError() }}</span> }
              @else { <span class="hint">On a market value of ₹1,00,00,000: realizable ₹{{ sample(v().realizablePercent) }}, distress ₹{{ sample(v().distressPercent) }}.</span> }
            </div>
          </div>
        </div>

        <label class="check"><input type="checkbox" formControlName="isActive" /> Empanelled (active) — inactive banks are hidden when creating new assignments</label>
      </form>

      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="submit" form="bankForm" [disabled]="busy()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> {{ bank() ? 'Save' : 'Add Bank' }}
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    form { display: flex; flex-direction: column; gap: 16px; }
    .upper { text-transform: uppercase; }
    .color { display: flex; align-items: center; gap: 8px; }
    .color input[type='color'] { flex: none; }
    .section { padding-top: 14px; border-top: 1px solid var(--line-2); }
    .section h4 { margin: 0 0 12px; font-size: 14px; }
    .hint-line { margin-top: -6px; font-size: 12px; }
    .hint-line .hint { color: var(--muted); }
    .hint-line .err { color: var(--danger); }
    .check { display: flex; align-items: center; gap: 8px; font-size: 13px; color: var(--ink-2); cursor: pointer; }
    .check input { width: 16px; height: 16px; }
  `,
})
export class BankFormDialog implements OnInit {
  private readonly api = inject(BanksApi);

  readonly bank = input<Bank | null>(null);
  /** Short codes already in use, so the quick pick only offers banks not yet added. */
  readonly existingCodes = input<string[]>([]);
  readonly saved = output<Bank>();
  readonly closed = output<void>();

  protected readonly rateBases = RATE_BASES;
  protected readonly depreciations = DEPRECIATION_METHODS;
  protected readonly presets = computed(() => COMMON_BANKS.filter((b) => !this.existingCodes().includes(b.code)));
  private readonly formatsApi = inject(ReportFormatsApi);
  protected readonly formats = signal<ReportFormatItem[]>([]);
  protected readonly defaultFormat = computed(() => this.formats().find((f) => f.isDefault)?.name ?? null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly server = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    shortCode: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9]{2,10}$/)]],
    brandColor: ['#2B50C4', Validators.pattern(/^#[0-9A-Fa-f]{6}$/)],
    ifscPrefix: ['', Validators.pattern(/^[A-Za-z]{4}$/)],
    reportFormatId: [''],
    panelCode: ['', Validators.maxLength(60)],
    isActive: [true],
    rateBasis: ['MarketRates' as RateBasis],
    depreciation: ['StraightLine' as DepreciationMethod],
    distressPercent: [80, [Validators.required, Validators.min(1), Validators.max(100)]],
    realizablePercent: [90, [Validators.required, Validators.min(1), Validators.max(100)]],
  });

  protected readonly v = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() }) as () => ReturnType<typeof this.form.getRawValue>;

  protected readonly rulesError = computed(() => {
    const { distressPercent: d, realizablePercent: r } = this.v();
    return d != null && r != null && Number(d) > Number(r) ? 'Distress value should not be higher than realizable value.' : null;
  });

  ngOnInit(): void {
    const b = this.bank();
    if (b) {
      this.form.setValue({
        name: b.name, shortCode: b.shortCode, brandColor: b.brandColor, ifscPrefix: b.ifscPrefix ?? '', reportFormatId: b.reportFormatId ?? '', panelCode: b.panelCode ?? '',
        isActive: b.isActive, rateBasis: b.rules.rateBasis, depreciation: b.rules.depreciation,
        distressPercent: b.rules.distressPercent, realizablePercent: b.rules.realizablePercent,
      });
    }
    this.form.valueChanges.subscribe(() => this.server.set({}));
    this.formatsApi.list().subscribe({ next: (f) => this.formats.set(f), error: () => this.formats.set([]) });
  }

  protected pick(code: string): void {
    const b = COMMON_BANKS.find((x) => x.code === code);
    if (b) this.form.patchValue({ name: b.name, shortCode: b.code, ifscPrefix: b.ifsc, brandColor: b.color });
  }

  protected sample(percent: number): string {
    return Math.round((10_000_000 * (Number(percent) || 0)) / 100).toLocaleString('en-IN');
  }

  protected bad(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return (c.touched && c.invalid) || !!this.server()[name];
  }

  protected async save(): Promise<void> {
    if (this.form.invalid || this.rulesError()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body: SaveBank = {
      name: v.name.trim(),
      shortCode: v.shortCode.trim().toUpperCase(),
      brandColor: v.brandColor,
      ifscPrefix: v.ifscPrefix.trim().toUpperCase() || null,
      reportFormatId: v.reportFormatId || null,
      panelCode: v.panelCode.trim() || null,
      isActive: v.isActive,
      rules: {
        rateBasis: v.rateBasis,
        depreciation: v.depreciation,
        distressPercent: Number(v.distressPercent),
        realizablePercent: Number(v.realizablePercent),
      },
    };
    this.busy.set(true);
    this.error.set(null);
    try {
      const b = this.bank();
      this.saved.emit(await firstValueFrom(b ? this.api.update(b.id, body) : this.api.create(body)));
    } catch (e) {
      const p = toProblem(e);
      this.server.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
