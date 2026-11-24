import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { AmountInput } from '../../shared/amount-input';
import { DEPRECIATION_METHODS, DepreciationMethod } from '../banks/banks.api';
import { INDIAN_STATES } from '../settings/settings.api';
import {
  CircleRate, ConstructionRate, DepreciationRule, isoDate, LocationRate, RatesApi, SCHEME_BY_STATE, SCHEMES, UNITS, ZONES,
} from './rates.api';

export type RateDialogKind = 'location' | 'circle' | 'construction' | 'depreciation';
type AnyRate = LocationRate | CircleRate | ConstructionRate | DepreciationRule;

const TITLES: Record<RateDialogKind, string> = {
  location: 'Location Rate',
  circle: 'Circle / Jantri Rate',
  construction: 'Construction Rate',
  depreciation: 'Depreciation Rule',
};

/** Start of the current Indian financial year (1 April), the usual date rates are revised. */
function financialYearStart(): string {
  const now = new Date();
  return isoDate(new Date(now.getMonth() >= 3 ? now.getFullYear() : now.getFullYear() - 1, 3, 1));
}

@Component({
  selector: 'vp-rate-form-dialog',
  imports: [Dialog, FormsModule, AmountInput],
  templateUrl: './rate-form-dialog.html',
  styles: `
    .note { margin: 0; padding: 10px 12px; border-radius: 9px; background: var(--info-soft); color: var(--info); font-size: 12.5px; line-height: 1.5; }
  `,
})
export class RateFormDialog implements OnInit {
  private readonly api = inject(RatesApi);

  readonly kind = input.required<RateDialogKind>();
  readonly rate = input<AnyRate | null>(null);
  readonly cities = input<string[]>([]);
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly zones = ZONES;
  protected readonly schemes = SCHEMES;
  protected readonly units = UNITS;
  protected readonly methods = DEPRECIATION_METHODS;
  protected readonly states = INDIAN_STATES.map((s) => s.name);
  protected readonly title = computed(() => (this.rate() ? 'Edit ' : 'Add ') + TITLES[this.kind()]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fields = signal<Record<string, string>>({});
  protected touched = false;

  // One model covers all four kinds; only the fields of the current kind are used.
  protected m = {
    locality: '', city: '', state: null as string | null, zone: 'Residential', minRate: null as number | null, maxRate: null as number | null,
    scheme: 'Jantri', residential: null as number | null, commercial: null as number | null, openLand: null as number | null,
    constructionType: '', unit: 'SqFt', cpwdRate: null as number | null, pwdRate: null as number | null, bankRate: null as number | null,
    structureType: '', totalLifeYears: 60 as number | null, salvagePercent: 10 as number | null, method: 'StraightLine' as DepreciationMethod,
    effectiveFrom: financialYearStart() as string | null, note: '', // yyyy-mm-dd from <input type=date>
  };

  ngOnInit(): void {
    const r = this.rate() as Record<string, unknown> | null;
    if (!r) return;
    Object.assign(this.m, Object.fromEntries(Object.entries(r).filter(([k]) => k in this.m)));
    this.m.note = String(r['source'] ?? r['reference'] ?? r['remarks'] ?? '');
  }

  protected onState(state: string | null): void {
    if (this.kind() === 'circle' && state && SCHEME_BY_STATE[state]) this.m.scheme = SCHEME_BY_STATE[state];
  }

  /** Client-side checks that mirror the server's, so most mistakes show before saving. */
  protected problem(field: string): string | null {
    const server = this.fields()[field];
    if (server) return server;
    if (!this.touched) return null;
    const m = this.m;
    switch (this.kind()) {
      case 'location':
        if (field === 'locality' && !m.locality.trim()) return 'Enter the locality.';
        if (field === 'city' && !m.city.trim()) return 'Enter the city.';
        if (field === 'minRate' && !(m.minRate! > 0)) return 'Enter the lowest market rate per sq.ft.';
        if (field === 'maxRate' && m.maxRate != null && m.minRate != null && m.maxRate < m.minRate) return 'The highest rate cannot be below the lowest.';
        break;
      case 'circle':
        if (field === 'state' && !m.state) return 'Choose the state.';
        if (field === 'city' && !m.city.trim()) return 'Enter the city or district.';
        if (field === 'locality' && !m.locality.trim()) return 'Enter the locality / village.';
        if (field === 'residential' && m.residential == null && m.commercial == null && m.openLand == null) return 'Enter at least one rate.';
        break;
      case 'construction':
        if (field === 'constructionType' && !m.constructionType.trim()) return 'Enter the construction type.';
        if (field === 'cpwdRate' && m.cpwdRate == null && m.pwdRate == null && m.bankRate == null) return 'Enter at least one rate.';
        break;
      case 'depreciation':
        if (field === 'structureType' && !m.structureType.trim()) return 'Enter the structure type.';
        if (field === 'totalLifeYears' && !(m.totalLifeYears! >= 5 && m.totalLifeYears! <= 150)) return 'Total life should be 5–150 years.';
        if (field === 'salvagePercent' && !(m.salvagePercent! >= 0 && m.salvagePercent! <= 50)) return 'Salvage value should be 0–50%.';
        if (field === 'salvagePercent' && m.method === 'WrittenDownValue' && !(m.salvagePercent! > 0)) return 'Written-down value needs a salvage value above 0%.';
        break;
    }
    if (field === 'effectiveFrom' && this.kind() !== 'depreciation' && !m.effectiveFrom) return 'Enter the date these rates apply from.';
    return null;
  }

  private body(): unknown {
    const m = this.m;
    const note = m.note.trim() || null;
    const date = m.effectiveFrom || null;
    switch (this.kind()) {
      case 'location':
        return { locality: m.locality.trim(), city: m.city.trim(), state: m.state || null, zone: m.zone, minRate: m.minRate, maxRate: m.maxRate ?? m.minRate, effectiveFrom: date, source: note };
      case 'circle':
        return { state: m.state, scheme: m.scheme, city: m.city.trim(), locality: m.locality.trim(), residential: m.residential, commercial: m.commercial, openLand: m.openLand, effectiveFrom: date, reference: note };
      case 'construction':
        return { constructionType: m.constructionType.trim(), unit: m.unit, cpwdRate: m.cpwdRate, pwdRate: m.pwdRate, bankRate: m.bankRate, effectiveFrom: date, remarks: note };
      default:
        return { structureType: m.structureType.trim(), totalLifeYears: m.totalLifeYears, salvagePercent: m.salvagePercent, method: m.method, remarks: note };
    }
  }

  private readonly checked: Record<RateDialogKind, string[]> = {
    location: ['locality', 'city', 'minRate', 'maxRate', 'effectiveFrom'],
    circle: ['state', 'city', 'locality', 'residential', 'effectiveFrom'],
    construction: ['constructionType', 'cpwdRate', 'effectiveFrom'],
    depreciation: ['structureType', 'totalLifeYears', 'salvagePercent'],
  };

  protected async save(): Promise<void> {
    this.touched = true;
    this.fields.set({});
    if (this.checked[this.kind()].some((f) => this.problem(f))) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.save(this.kind(), this.rate()?.id ?? null, this.body()));
      this.saved.emit();
    } catch (e) {
      const p = toProblem(e);
      this.fields.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
