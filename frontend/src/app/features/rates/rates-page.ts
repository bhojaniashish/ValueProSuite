import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { LocalTable } from '../../shared/local-table';
import { Pager } from '../../shared/pager';
import { SortHeader } from '../../shared/sort-header';
import { TabItem, Tabs } from '../../shared/tabs';
import { depreciationLabel } from '../banks/banks.api';
import { RateDialogKind, RateFormDialog } from './rate-form-dialog';
import { RateImportDialog } from './rate-import-dialog';
import {
  CircleRate, ConstructionRate, day, DepreciationRule, downloadCsv, inr, LocationRate, RateFilters, RateKind, RatesApi,
  schemeLabel, unitLabel, zoneMeta,
} from './rates.api';

type TabKey = RateDialogKind;
const TAB_KEYS: TabKey[] = ['location', 'construction', 'circle', 'depreciation'];

@Component({
  selector: 'vp-rates-page',
  imports: [Tabs, SortHeader, Pager, RateFormDialog, RateImportDialog, Dialog],
  templateUrl: './rates-page.html',
  styleUrl: './rates-page.scss',
})
export class RatesPage implements OnInit {
  private readonly api = inject(RatesApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** ?tab=… keeps the open tab in the URL. */
  readonly tabParam = input<string>(undefined, { alias: 'tab' });

  protected readonly inr = inr;
  protected readonly day = day;
  protected readonly zone = zoneMeta;
  protected readonly scheme = schemeLabel;
  protected readonly unit = unitLabel;
  protected readonly method = depreciationLabel;
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.ratesManage));

  protected readonly tab = signal<TabKey>('location');
  protected readonly currentOnly = signal(true);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly filters = signal<RateFilters | null>(null);
  protected readonly location = signal<LocationRate[]>([]);
  protected readonly circle = signal<CircleRate[]>([]);
  protected readonly construction = signal<ConstructionRate[]>([]);
  protected readonly depreciation = signal<DepreciationRule[]>([]);

  protected readonly tL = new LocalTable(this.location, { searchIn: ['locality', 'city', 'state', 'zone', 'source'], sort: 'locality' });
  protected readonly tK = new LocalTable(this.construction, { searchIn: ['constructionType', 'remarks'], sort: 'constructionType' });
  protected readonly tC = new LocalTable(this.circle, { searchIn: ['locality', 'city', 'state', 'scheme', 'reference'], sort: 'locality' });

  /** The table on the open tab (null on Depreciation, which shows cards). */
  protected readonly active = computed<LocalTable<never> | null>(() =>
    ({ location: this.tL, construction: this.tK, circle: this.tC, depreciation: null })[this.tab()] as LocalTable<never> | null);

  protected readonly tabs = computed<TabItem[]>(() => {
    const f = this.filters();
    return [
      { value: 'location', label: 'Location Rates', icon: 'fa-location-dot', count: f?.location },
      { value: 'construction', label: 'Construction', icon: 'fa-trowel-bricks', count: f?.construction },
      { value: 'circle', label: 'Circle / Jantri', icon: 'fa-landmark', count: f?.circle },
      { value: 'depreciation', label: 'Depreciation', icon: 'fa-chart-line', count: f?.depreciation },
    ];
  });

  protected readonly editing = signal<{ kind: RateDialogKind; rate: unknown } | null>(null);
  protected readonly importing = signal<RateKind | null>(null);
  protected readonly deleting = signal<{ kind: RateDialogKind; id: string; label: string } | null>(null);
  protected readonly busy = signal(false);

  protected readonly importKind = computed<RateKind | null>(() =>
    ({ location: 'Location', circle: 'Circle', construction: 'Construction', depreciation: null } as const)[this.tab()]);

  async ngOnInit(): Promise<void> {
    const t = this.tabParam() as TabKey | undefined;
    if (t && TAB_KEYS.includes(t)) this.tab.set(t);
    await this.load();
  }

  protected onTab(t: string): void {
    this.tab.set(t as TabKey);
    void this.router.navigate([], { queryParams: { tab: t === 'location' ? null : t }, replaceUrl: true });
  }

  protected async setCurrentOnly(v: boolean): Promise<void> {
    this.currentOnly.set(v);
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    try {
      const c = this.currentOnly();
      const [filters, location, circle, construction, depreciation] = await Promise.all([
        firstValueFrom(this.api.filters()),
        firstValueFrom(this.api.location(c)),
        firstValueFrom(this.api.circle(c)),
        firstValueFrom(this.api.construction(c)),
        firstValueFrom(this.api.depreciation()),
      ]);
      this.filters.set(filters);
      this.location.set(location.items);
      this.circle.set(circle.items);
      this.construction.set(construction.items);
      this.depreciation.set(depreciation);
      this.loadError.set(null);
    } catch (e) {
      this.loadError.set(toProblem(e).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected add(): void {
    this.editing.set({ kind: this.tab(), rate: null });
  }

  protected onSaved(): void {
    const isNew = !this.editing()?.rate;
    this.editing.set(null);
    this.toast.ok(isNew ? 'Rate added' : 'Rate updated');
    void this.load();
  }

  protected onImported(n: number): void {
    if (n) this.toast.ok('Import complete', `${n} rate${n === 1 ? '' : 's'} added.`);
    void this.load();
  }

  protected async addStandard(): Promise<void> {
    this.busy.set(true);
    try {
      this.depreciation.set(await firstValueFrom(this.api.addStandardDepreciation()));
      this.toast.ok('Standard rules added', 'Adjust life and salvage to your banks’ norms.');
      void this.load();
    } catch (e) {
      this.toast.error('Could not add rules', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const d = this.deleting();
    if (!d) return;
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(d.kind, d.id));
      this.deleting.set(null);
      this.toast.ok('Removed', d.label);
      void this.load();
    } catch (e) {
      this.toast.error('Could not remove', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** Export what the table shows (after search and sorting, all pages) to a CSV for Excel. */
  protected export(): void {
    const stamp = new Date().toISOString().slice(0, 10);
    switch (this.tab()) {
      case 'location':
        return downloadCsv(`location-rates-${stamp}.csv`, ['Locality', 'City', 'State', 'Zone', 'Min Rate (sq.ft)', 'Max Rate (sq.ft)', 'Circle Rate (sq.m)', 'Effective From', 'Source'],
          this.tL.rows().map((r) => [r.locality, r.city, r.state, zoneMeta(r.zone).label, r.minRate, r.maxRate, r.circleRatePerSqM, r.effectiveFrom, r.source]));
      case 'circle':
        return downloadCsv(`circle-rates-${stamp}.csv`, ['State', 'Scheme', 'City', 'Locality', 'Residential (sq.m)', 'Commercial (sq.m)', 'Open Land (sq.m)', 'Effective From', 'Reference'],
          this.tC.rows().map((r) => [r.state, schemeLabel(r.scheme), r.city, r.locality, r.residential, r.commercial, r.openLand, r.effectiveFrom, r.reference]));
      case 'construction':
        return downloadCsv(`construction-rates-${stamp}.csv`, ['Construction Type', 'Unit', 'CPWD', 'PWD', 'Bank', 'Effective From', 'Remarks'],
          this.tK.rows().map((r) => [r.constructionType, unitLabel(r.unit), r.cpwdRate, r.pwdRate, r.bankRate, r.effectiveFrom, r.remarks]));
    }
  }
}
