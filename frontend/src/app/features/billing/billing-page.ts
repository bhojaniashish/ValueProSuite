import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/ui/toast';
import { Pager } from '../../shared/pager';
import { TabItem, Tabs } from '../../shared/tabs';
import {
  BillingApi, BillingSummary, day, InvoiceFilter, InvoiceListItem, money, STATE_META, UnbilledCase,
} from './billing.api';

type View = 'invoices' | 'unbilled';

/** Billing: what is owed and overdue, cases waiting to be billed, and every invoice. */
@Component({
  selector: 'vp-billing-page',
  imports: [RouterLink, Tabs, Pager],
  templateUrl: './billing-page.html',
  styleUrl: './billing-page.scss',
})
export class BillingPage implements OnInit {
  private readonly api = inject(BillingApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  /** ?view=unbilled from the URL. */
  readonly view = input<string>();

  protected readonly money = money;
  protected readonly day = day;
  protected readonly stateMeta = STATE_META;
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.billingManage));

  protected readonly summary = signal<BillingSummary | null>(null);
  protected readonly tab = signal<View>('invoices');
  protected readonly tabs = computed<TabItem[]>(() => [
    { value: 'invoices', label: 'Invoices', icon: 'fa-file-invoice' },
    { value: 'unbilled', label: `To bill${this.summary()?.unbilledCount ? ` (${this.summary()!.unbilledCount})` : ''}`, icon: 'fa-hourglass-half' },
  ]);

  // invoices
  protected readonly filters: { value: InvoiceFilter; label: string }[] = [
    { value: '', label: 'All' }, { value: 'unpaid', label: 'Unpaid' }, { value: 'overdue', label: 'Overdue' },
    { value: 'paid', label: 'Paid' }, { value: 'draft', label: 'Drafts' }, { value: 'cancelled', label: 'Cancelled' },
  ];
  protected readonly filter = signal<InvoiceFilter>('');
  protected readonly bankId = signal<string | null>(null);
  protected readonly items = signal<InvoiceListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 20;
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));
  protected readonly range = computed(() => {
    const t = this.total();
    if (!t) return '0 invoices';
    const from = (this.page() - 1) * this.pageSize + 1;
    return `${from}–${Math.min(t, from + this.pageSize - 1)} of ${t}`;
  });
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  private search = '';
  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  // unbilled
  protected readonly unbilled = signal<UnbilledCase[] | null>(null);
  protected readonly picked = signal<Set<string>>(new Set());
  protected readonly creating = signal(false);
  protected readonly groups = computed(() => {
    const map = new Map<string, { bankId: string; code: string; color: string; cases: UnbilledCase[] }>();
    for (const c of this.unbilled() ?? []) {
      const g = map.get(c.bankId) ?? { bankId: c.bankId, code: c.bankShortCode, color: c.bankColor, cases: [] };
      g.cases.push(c);
      map.set(c.bankId, g);
    }
    return [...map.values()];
  });
  /** Cases on one invoice must be one bank's. */
  protected readonly pickedBank = computed(() => {
    const first = [...this.picked()][0];
    return (this.unbilled() ?? []).find((c) => c.assignmentId === first)?.bankId ?? null;
  });
  protected readonly pickedTotal = computed(() => (this.unbilled() ?? []).filter((c) => this.picked().has(c.assignmentId)).reduce((s, c) => s + c.fee, 0));

  protected readonly maxMonth = computed(() => Math.max(1, ...(this.summary()?.months ?? []).map((m) => Math.max(m.invoiced, m.collected))));

  async ngOnInit(): Promise<void> {
    if (this.view() === 'unbilled') this.tab.set('unbilled');
    void this.loadSummary();
    void this.load();
    void this.loadUnbilled();
  }

  private async loadSummary(): Promise<void> {
    try {
      this.summary.set(await firstValueFrom(this.api.summary()));
    } catch {
      /* the lists still work */
    }
  }

  protected setTab(v: string): void {
    this.tab.set(v as View);
    void this.router.navigate([], { queryParams: { view: v === 'unbilled' ? 'unbilled' : null }, replaceUrl: true });
  }

  protected async load(): Promise<void> {
    const seq = ++this.seq;
    this.loading.set(true);
    try {
      const r = await firstValueFrom(this.api.list({ search: this.search, state: this.filter(), bankId: this.bankId(), page: this.page(), pageSize: this.pageSize }));
      if (seq !== this.seq) return;
      this.items.set(r.items);
      this.total.set(r.total);
      this.error.set(null);
    } catch (e) {
      if (seq === this.seq) this.error.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  protected onSearch(v: string): void {
    this.search = v.trim();
    clearTimeout(this.timer);
    this.timer = setTimeout(() => { this.page.set(1); void this.load(); }, 300);
  }

  protected setFilter(f: InvoiceFilter): void {
    this.filter.set(f);
    this.page.set(1);
    void this.load();
  }

  protected setBank(id: string | null): void {
    this.bankId.set(id);
    this.filter.set('');
    this.page.set(1);
    this.setTab('invoices');
    void this.load();
  }

  protected goTo(p: number): void {
    this.page.set(p);
    void this.load();
  }

  protected async loadUnbilled(): Promise<void> {
    try {
      this.unbilled.set(await firstValueFrom(this.api.unbilled()));
    } catch {
      this.unbilled.set([]);
    }
  }

  protected toggle(c: UnbilledCase): void {
    const next = new Set(this.picked());
    if (next.has(c.assignmentId)) next.delete(c.assignmentId);
    else {
      // Picking another bank's case starts a new selection.
      if (this.pickedBank() && this.pickedBank() !== c.bankId) next.clear();
      next.add(c.assignmentId);
    }
    this.picked.set(next);
  }

  protected pickAll(bankId: string): void {
    const cases = this.groups().find((g) => g.bankId === bankId)?.cases.filter((c) => !c.draftInvoiceId) ?? [];
    this.picked.set(new Set(cases.map((c) => c.assignmentId)));
  }

  protected async createInvoice(ids: string[]): Promise<void> {
    if (!ids.length) return;
    this.creating.set(true);
    try {
      const inv = await firstValueFrom(this.api.create(ids));
      void this.router.navigate(['/billing', inv.id]);
    } catch (e) {
      this.toast.error('Could not create the invoice', toProblem(e).message);
    } finally {
      this.creating.set(false);
    }
  }

  protected readonly bankName = computed(() => this.summary()?.byBank.find((b) => b.bankId === this.bankId())?.name ?? 'One bank');

  protected clearPicked(): void {
    this.picked.set(new Set());
  }

  protected selectedIds(): string[] {
    return [...this.picked()];
  }

  protected barHeight(v: number): number {
    return Math.round((v / this.maxMonth()) * 100);
  }

  protected short(v: number): string {
    if (v >= 1_00_00_000) return `₹${(v / 1_00_00_000).toFixed(2).replace(/\.?0+$/, '')} Cr`;
    if (v >= 1_00_000) return `₹${(v / 1_00_000).toFixed(2).replace(/\.?0+$/, '')} L`;
    return money(Math.round(v));
  }
}
