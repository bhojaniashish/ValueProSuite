import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { avatarColor, initials } from '../../core/ui/format';
import { ToastService } from '../../core/ui/toast';
import { Paged } from '../users/users.api';
import { ClientFormDialog } from './client-form-dialog';
import { ClientImportDialog } from './client-import-dialog';
import { ClientViewDialog } from './client-view-dialog';
import { Client, ClientQuery, CLIENT_TYPES, clientType, ClientsApi, ClientSummary, ClientType } from './clients.api';

type SortKey = 'name' | 'type' | 'city' | 'code';

@Component({
  selector: 'vp-clients-page',
  imports: [ClientFormDialog, ClientViewDialog, ClientImportDialog, Dialog],
  templateUrl: './clients-page.html',
  styleUrl: './clients-page.scss',
})
export class ClientsPage implements OnInit, OnDestroy {
  private readonly api = inject(ClientsApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);

  protected readonly types = CLIENT_TYPES;
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.clientsManage));

  protected readonly query = signal<ClientQuery>({ page: 1, pageSize: 10, sort: 'name', desc: false, search: '', type: '', state: '' });
  protected readonly page = signal<Paged<Client> | null>(null);
  protected readonly summary = signal<ClientSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected readonly pages = computed(() => {
    const p = this.page();
    return p ? Math.max(1, Math.ceil(p.total / p.pageSize)) : 1;
  });
  protected readonly range = computed(() => {
    const p = this.page();
    if (!p || p.total === 0) return '0 clients';
    const from = (p.page - 1) * p.pageSize + 1;
    return `${from}–${Math.min(p.total, from + p.items.length - 1)} of ${p.total}`;
  });
  protected readonly filtered = computed(() => {
    const q = this.query();
    return !!(q.search || q.type || q.state);
  });

  // Dialogs
  protected readonly editing = signal<Client | null | undefined>(undefined); // undefined = closed, null = new
  protected readonly viewing = signal<Client | null>(null);
  protected readonly deleting = signal<Client | null>(null);
  protected readonly importing = signal(false);
  protected readonly busy = signal(false);

  private searchTimer?: ReturnType<typeof setTimeout>;
  private requestSeq = 0;

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    clearTimeout(this.searchTimer);
  }

  protected readonly initials = initials;
  protected readonly avatarColor = avatarColor;
  protected readonly type = clientType;

  protected count(t: ClientType): number {
    const s = this.summary();
    if (!s) return 0;
    return { Individual: s.individual, Builder: s.builder, BankBranch: s.bankBranch, Advocate: s.advocate }[t];
  }

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.patch({ search: value.trim(), page: 1 }), 300);
  }

  /** The type cards double as quick filters. */
  protected filterType(t: ClientType | ''): void {
    this.patch({ type: this.query().type === t ? '' : t, page: 1 });
  }

  protected onFilter(key: 'type' | 'state', value: string): void {
    this.patch({ [key]: value, page: 1 });
  }

  protected clearFilters(): void {
    this.patch({ search: '', type: '', state: '', page: 1 });
  }

  protected sortBy(key: SortKey): void {
    const q = this.query();
    this.patch({ sort: key, desc: q.sort === key ? !q.desc : false, page: 1 });
  }

  protected sortIcon(key: SortKey): string {
    const q = this.query();
    return q.sort !== key ? 'fa-sort' : q.desc ? 'fa-sort-down' : 'fa-sort-up';
  }

  protected goTo(page: number): void {
    if (page >= 1 && page <= this.pages()) this.patch({ page });
  }

  private patch(change: Partial<ClientQuery>): void {
    this.query.update((q) => ({ ...q, ...change }));
    void this.load();
  }

  protected async load(): Promise<void> {
    const seq = ++this.requestSeq;
    this.loading.set(true);
    try {
      const [page, summary] = await Promise.all([firstValueFrom(this.api.list(this.query())), firstValueFrom(this.api.summary())]);
      if (seq !== this.requestSeq) return;
      this.page.set(page);
      this.summary.set(summary);
      this.loadError.set(null);
    } catch (e) {
      if (seq === this.requestSeq) this.loadError.set(toProblem(e).message);
    } finally {
      if (seq === this.requestSeq) this.loading.set(false);
    }
  }

  protected onSaved(client: Client, wasNew: boolean): void {
    this.editing.set(undefined);
    this.toast.ok(wasNew ? 'Client added' : 'Client updated', `${client.name} · ${client.code}`);
    void this.load();
  }

  protected editFromView(): void {
    const c = this.viewing();
    this.viewing.set(null);
    this.editing.set(c);
  }

  protected async confirmDelete(): Promise<void> {
    const c = this.deleting();
    if (!c) return;
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(c.id));
      this.deleting.set(null);
      this.toast.ok('Client removed', `${c.name} has been removed.`);
      void this.load();
    } catch (e) {
      this.toast.error('Could not remove client', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected onImported(count: number): void {
    if (count) this.toast.ok('Import complete', `${count} client${count === 1 ? '' : 's'} added.`);
    void this.load();
  }
}
