import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';
import { AmountInput } from '../../shared/amount-input';
import { Choice } from '../../shared/chips';
import { DEPRECIATION_METHODS } from '../banks/banks.api';
import { propertyType } from '../properties/properties.api';
import {
  ADJUSTMENT_PRESETS, AREA_BASES, BuildingLine, ConstructionOption, EXTRA_PRESETS, inr, LocationOption, short, ValuationDetail,
  ValuationInput, ValuationResult, ValuationsApi,
} from './valuations.api';

type RateColumn = 'cpwd' | 'pwd' | 'bank';

/** Work out a case's value: inputs on the left (pre-filled), live result on the right, saved when the valuer is happy. */
@Component({
  selector: 'vp-valuation-page',
  imports: [FormsModule, RouterLink, AmountInput, Choice],
  templateUrl: './valuation-page.html',
  styleUrl: './valuation-page.scss',
})
export class ValuationPage implements OnInit {
  private readonly api = inject(ValuationsApi);
  private readonly toast = inject(ToastService);

  readonly id = input.required<string>();

  protected readonly inr = inr;
  protected readonly short = short;
  protected readonly propertyType = propertyType;
  protected readonly bases = AREA_BASES;
  protected readonly methods = DEPRECIATION_METHODS;
  protected readonly adjustmentPresets = ADJUSTMENT_PRESETS;
  protected readonly extraPresets = EXTRA_PRESETS;
  protected readonly methodOptions = [
    { value: 'Composite', label: 'Composite rate (flats, offices, shops)' },
    { value: 'LandAndBuilding', label: 'Land + building (houses, plots, sheds)' },
  ];
  protected readonly columns: { value: RateColumn; label: string }[] = [
    { value: 'cpwd', label: 'CPWD' },
    { value: 'pwd', label: 'PWD' },
    { value: 'bank', label: 'Bank' },
  ];

  protected readonly detail = signal<ValuationDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  /** The form model (plain object, bound with ngModel). */
  protected i!: ValuationInput;
  protected readonly result = signal<ValuationResult | null>(null);
  protected readonly calculating = signal(false);
  protected readonly calcError = signal<string | null>(null);
  protected readonly dirty = signal(false);
  protected readonly saving = signal(false);
  protected readonly showWorking = signal(false);

  protected readonly locationId = signal<string | null>(null);
  protected readonly constructionId = signal<string | null>(null);
  protected readonly column = signal<RateColumn>('pwd');

  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  protected readonly c = computed(() => this.detail()?.case ?? null);
  protected readonly canEdit = computed(() => this.detail()?.canEdit ?? false);
  protected readonly location = computed(() => this.detail()?.options.locations.find((l) => l.id === this.locationId()) ?? null);
  protected readonly construction = computed(() => this.detail()?.options.construction.find((c) => c.id === this.constructionId()) ?? null);

  constructor() {
    const warn = (e: BeforeUnloadEvent) => {
      if (this.dirty()) e.preventDefault();
    };
    window.addEventListener('beforeunload', warn);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('beforeunload', warn);
      clearTimeout(this.timer);
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      this.apply(await firstValueFrom(this.api.get(this.id())));
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  private apply(d: ValuationDetail): void {
    this.detail.set(d);
    this.i = structuredClone(d.input);
    this.result.set(d.result);
    this.dirty.set(false);
    // The locality whose band the valuation used (a saved one keeps its band), else the suggested match.
    const used = d.options.locations.find((l) => l.minRate === d.input.marketRateMin && l.maxRate === d.input.marketRateMax);
    this.locationId.set(used?.id ?? d.options.locations.find((l) => l.suggested)?.id ?? null);
    this.constructionId.set(d.options.construction.find((c) => c.suggested)?.id ?? null);
    this.column.set(d.bank.rateBasis === 'BankRates' ? 'bank' : 'pwd');
  }

  // ---------------- live calculation ----------------

  /** Any change: recalculate shortly (on the server, so the figures always match what is saved). */
  protected changed(): void {
    if (!this.canEdit()) return;
    this.dirty.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.calculate(), 350);
  }

  private async calculate(): Promise<void> {
    const seq = ++this.seq;
    this.calculating.set(true);
    try {
      const r = await firstValueFrom(this.api.calculate(this.i));
      if (seq !== this.seq) return;
      this.result.set(r);
      this.calcError.set(null);
    } catch (e) {
      if (seq !== this.seq) return;
      const p = toProblem(e);
      this.calcError.set(Object.values(p.fields)[0] ?? p.message);
    } finally {
      if (seq === this.seq) this.calculating.set(false);
    }
  }

  protected setMethod(m: string | null): void {
    if (!m || m === this.i.method) return;
    this.i.method = m as ValuationInput['method'];
    // Sensible areas when switching: the land for land + building, the built-up area for composite.
    if (this.i.method === 'LandAndBuilding') {
      if (!this.i.landAreaSqFt) this.i.landAreaSqFt = this.detail()?.inspection.landMeasured ?? this.detail()?.inspection.landDocs ?? 0;
      if (!this.i.landRatePerSqFt) this.i.landRatePerSqFt = this.i.compositeRatePerSqFt;
      if (!this.i.building.length && this.i.builtUpSqFt) this.addLine(this.i.builtUpSqFt);
      this.i.guidelineAreaSqFt = this.i.landAreaSqFt || this.i.guidelineAreaSqFt;
    } else if (!this.i.areaSqFt) {
      this.i.areaSqFt = this.i.builtUpSqFt || this.i.building.reduce((s, b) => s + b.areaSqFt, 0);
      this.i.areaBasis = 'BuiltUp';
    }
    this.changed();
  }

  // ---------------- Rate Master ----------------

  protected pickLocation(id: string): void {
    this.locationId.set(id || null);
    const l = this.location();
    if (!l) return;
    const mid = Math.round((l.minRate + l.maxRate) / 2 / 50) * 50;
    this.i.marketRateMin = l.minRate;
    this.i.marketRateMax = l.maxRate;
    if (this.i.method === 'Composite') this.i.compositeRatePerSqFt = mid;
    else this.i.landRatePerSqFt = mid;
    const circle = this.circleFor(l);
    if (circle) this.i.circleRatePerSqM = circle;
    this.changed();
  }

  private circleFor(l: LocationOption): number | null {
    const t = this.c()?.propertyType;
    if (t === 'OpenPlot' || t === 'AgriculturalLand') return l.circleOpenLand;
    if (t === 'CommercialShop' || t === 'OfficeSpace') return l.circleCommercial ?? l.circleResidential;
    return l.circleResidential;
  }

  protected rateOf(c: ConstructionOption | null, col: RateColumn): number | null {
    if (!c) return null;
    return col === 'cpwd' ? c.cpwdPerSqFt : col === 'pwd' ? c.pwdPerSqFt : c.bankPerSqFt;
  }

  /** Applies the chosen construction rate to every building line (and to the flat's rebuild cost). */
  protected applyConstruction(id: string | null, col: RateColumn): void {
    this.constructionId.set(id);
    this.column.set(col);
    const rate = this.rateOf(this.construction(), col);
    if (rate == null) return;
    for (const b of this.i.building) b.ratePerSqFt = rate;
    this.i.constructionRatePerSqFt = rate;
    this.changed();
  }

  protected useStructure(b: BuildingLine, structure: string): void {
    const r = this.detail()?.options.depreciation.find((x) => x.structureType === structure);
    if (!r) return;
    b.lifeYears = r.lifeYears;
    b.salvagePercent = r.salvagePercent;
    this.changed();
  }

  // ---------------- lines ----------------

  protected addLine(area = 0): void {
    const first = this.i.building[0];
    this.i.building.push({
      label: this.i.building.length ? `Floor ${this.i.building.length + 1}` : 'Building', areaSqFt: area,
      ratePerSqFt: first?.ratePerSqFt ?? this.rateOf(this.construction(), this.column()) ?? 0,
      ageYears: first?.ageYears ?? this.detail()?.inspection.ageYears ?? 0, lifeYears: first?.lifeYears ?? 60,
      salvagePercent: first?.salvagePercent ?? 10, method: first?.method ?? this.detail()?.bank.depreciation ?? 'StraightLine',
    });
    this.changed();
  }

  protected removeLine(idx: number): void {
    this.i.building.splice(idx, 1);
    this.changed();
  }

  protected addAdjustment(label: string | null = null, percent = 0): void {
    this.i.adjustments.push({ label, percent });
    this.changed();
  }

  protected addExtra(label: string | null = null): void {
    this.i.extras.push({ label, amount: 0 });
    this.changed();
  }

  protected remove<T>(list: T[], idx: number): void {
    list.splice(idx, 1);
    this.changed();
  }

  protected hasPreset(label: string | null): boolean {
    return this.i.adjustments.some((a) => a.label === label);
  }

  protected guidelineSqM(): number {
    return Math.round((this.i.guidelineAreaSqFt / 10.7639) * 100) / 100;
  }

  protected areaLabel(): string {
    return AREA_BASES.find((b) => b.value === this.i.areaBasis)?.label ?? 'Carpet';
  }

  protected belowGuideline(r: ValuationResult): number | null {
    return r.guidelineValue > 0 && r.fairMarketValue > 0 ? Math.round((r.fairMarketValue / r.guidelineValue - 1) * 1000) / 10 : null;
  }

  // ---------------- save ----------------

  protected async save(): Promise<void> {
    const d = this.detail();
    if (!d) return;
    clearTimeout(this.timer);
    this.saving.set(true);
    try {
      this.apply(await firstValueFrom(this.api.save(this.id(), d.version, this.i)));
      this.toast.ok('Valuation saved', `${d.case.number}: ${inr(this.result()?.fairMarketValue)}`);
    } catch (e) {
      const p = toProblem(e);
      this.toast.error(e instanceof HttpErrorResponse && e.status === 409 ? 'Saved elsewhere' : 'Could not save', Object.values(p.fields)[0] ?? p.message);
    } finally {
      this.saving.set(false);
    }
  }

  protected reset(): void {
    const d = this.detail();
    if (d) this.apply(d);
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-IN', { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' });
  }
}
