import { Component, computed, inject, input, OnDestroy, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { avatarColor, initials } from '../../core/ui/format';
import { ToastService } from '../../core/ui/toast';
import { BankBadge } from '../banks/bank-badge';
import { Paged } from '../users/users.api';
import { AssignmentFormDialog } from './assignment-form-dialog';
import {
  AssignmentDetail, AssignmentListItem, AssignmentQuery, AssignmentsApi, AssignmentStatus, Lookups, Pipeline,
  PRIORITIES, priorityBadge, shortDate, STAGES, statusMeta,
} from './assignments.api';
import { MoveDialog, MoveKind } from './move-dialog';

type View = 'list' | 'board';
type SortKey = 'number' | 'bank' | 'applicant' | 'priority' | 'due' | 'status';
const VIEW_KEY = 'valupro.assignments.view';

@Component({
  selector: 'vp-assignments-page',
  imports: [RouterLink, BankBadge, AssignmentFormDialog, MoveDialog],
  templateUrl: './assignments-page.html',
  styleUrl: './assignments-page.scss',
})
export class AssignmentsPage implements OnInit, OnDestroy {
  private readonly api = inject(AssignmentsApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);
  protected readonly router = inject(Router);

  /** ?new=1 opens the form; ?property=<id> pre-selects a property; ?status=…, ?bank=<id>, ?mine=1, ?overdue=1 and ?open=1 filter. */
  readonly new = input<string>();
  readonly property = input<string>();
  readonly status = input<string>();
  readonly bank = input<string>();
  readonly mine = input<string>();
  readonly overdue = input<string>();
  readonly open = input<string>();

  protected readonly stages = STAGES;
  protected readonly priorities = PRIORITIES;
  protected readonly meta = statusMeta;
  protected readonly priorityBadge = priorityBadge;
  protected readonly date = shortDate;
  protected readonly initials = initials;
  protected readonly avatarColor = avatarColor;
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.assignmentsManage));

  protected readonly view = signal<View>(this.storedView());
  protected readonly query = signal<AssignmentQuery>({ page: 1, pageSize: 15, search: '', status: '', bankId: '', priority: '' });
  protected readonly page = signal<Paged<AssignmentListItem> | null>(null);
  protected readonly board = signal<AssignmentListItem[]>([]);
  protected readonly pipeline = signal<Pipeline | null>(null);
  protected readonly banks = signal<Lookups['banks']>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected readonly creating = signal(false);
  protected readonly presetProperty = signal<string | null>(null);
  protected readonly move = signal<{ detail: AssignmentDetail; kind: MoveKind; to: AssignmentStatus } | null>(null);
  protected readonly dragging = signal<AssignmentListItem | null>(null);
  protected readonly dropTarget = signal<AssignmentStatus | null>(null);

  protected readonly pages = computed(() => {
    const p = this.page();
    return p ? Math.max(1, Math.ceil(p.total / p.pageSize)) : 1;
  });
  protected readonly range = computed(() => {
    const p = this.page();
    if (!p || !p.total) return '0 assignments';
    const from = (p.page - 1) * p.pageSize + 1;
    return `${from}–${Math.min(p.total, from + p.items.length - 1)} of ${p.total}`;
  });
  protected readonly filtered = computed(() => {
    const q = this.query();
    return !!(q.search || q.status || q.bankId || q.priority || q.mine || q.overdue || q.openOnly);
  });
  protected readonly columns = computed(() =>
    STAGES.map((s) => ({ status: s, items: this.board().filter((a) => a.status === s) })),
  );

  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  async ngOnInit(): Promise<void> {
    const status = this.status();
    if (status && (STAGES as string[]).concat('Cancelled').includes(status)) this.query.update((q) => ({ ...q, status: status as AssignmentStatus }));
    const on = (v: string | undefined) => v === '1' || v === 'true';
    this.query.update((q) => ({
      ...q, bankId: this.bank() || q.bankId, mine: on(this.mine()) || q.mine, overdue: on(this.overdue()) || q.overdue, openOnly: on(this.open()) || q.openOnly,
    }));
    if (this.new()) {
      this.presetProperty.set(this.property() ?? null);
      this.creating.set(true);
      void this.router.navigate([], { queryParams: {}, replaceUrl: true }); // so refresh doesn't reopen the form
    }
    void this.load();
    try {
      this.banks.set((await firstValueFrom(this.api.lookups())).banks);
    } catch {
      /* the bank filter just stays empty */
    }
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }

  // ---------- view & filters ----------

  protected setView(v: View): void {
    this.view.set(v);
    try { localStorage.setItem(VIEW_KEY, v); } catch { /* ignore */ }
    void this.load();
  }

  private storedView(): View {
    try { return localStorage.getItem(VIEW_KEY) === 'board' ? 'board' : 'list'; } catch { return 'list'; }
  }

  protected onSearch(value: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.patch({ search: value.trim(), page: 1 }), 300);
  }

  protected stageClick(s: AssignmentStatus): void {
    if (this.view() === 'board') return;
    this.patch({ status: this.query().status === s ? '' : s, page: 1 });
  }

  protected toggle(key: 'mine' | 'overdue' | 'openOnly'): void {
    this.patch({ [key]: !this.query()[key], page: 1 });
  }

  protected clear(): void {
    this.patch({ search: '', status: '', bankId: '', priority: '', mine: false, overdue: false, openOnly: false, page: 1 });
  }

  protected sortBy(key: SortKey): void {
    const q = this.query();
    this.patch({ sort: key, desc: q.sort === key ? !q.desc : false, page: 1 });
  }

  protected sortIcon(key: SortKey): string {
    const q = this.query();
    const sort = q.sort ?? 'due';
    return sort !== key ? 'fa-sort' : q.desc ? 'fa-sort-down' : 'fa-sort-up';
  }

  protected patch(change: Partial<AssignmentQuery>): void {
    this.query.update((q) => ({ ...q, ...change }));
    void this.load();
  }

  protected async load(): Promise<void> {
    const seq = ++this.seq;
    this.loading.set(true);
    const q = this.query();
    try {
      const [pipeline, data] = await Promise.all([
        firstValueFrom(this.api.pipeline(!!q.mine)),
        this.view() === 'board'
          ? firstValueFrom(this.api.list({ ...q, status: '', page: 1, pageSize: 200 }))
          : firstValueFrom(this.api.list(q)),
      ]);
      if (seq !== this.seq) return;
      this.pipeline.set(pipeline);
      if (this.view() === 'board') this.board.set(data.items);
      else this.page.set(data);
      this.loadError.set(null);
    } catch (e) {
      if (seq === this.seq) this.loadError.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  protected count(s: AssignmentStatus): number {
    return this.pipeline()?.stages.find((x) => x.status === s)?.count ?? 0;
  }

  // ---------- create ----------

  protected onCreated(d: AssignmentDetail): void {
    this.creating.set(false);
    this.toast.ok('Assignment created', `${d.assignment.number} · assigned to ${d.assignment.valuerName}`);
    void this.router.navigate(['/assignments', d.assignment.id]);
  }

  // ---------- board drag & drop ----------

  protected onDragStart(a: AssignmentListItem, e: DragEvent): void {
    this.dragging.set(a);
    e.dataTransfer?.setData('text/plain', a.id);
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }

  protected onDragOver(s: AssignmentStatus, e: DragEvent): void {
    const a = this.dragging();
    if (!a || a.status === s) return;
    e.preventDefault();
    this.dropTarget.set(s);
  }

  protected onDragEnd(): void {
    this.dragging.set(null);
    this.dropTarget.set(null);
  }

  protected async onDrop(to: AssignmentStatus, e: DragEvent): Promise<void> {
    e.preventDefault();
    const a = this.dragging();
    this.onDragEnd();
    if (!a || a.status === to) return;

    const from = STAGES.indexOf(a.status);
    const target = STAGES.indexOf(to);
    if (Math.abs(target - from) !== 1) {
      this.toast.show('warn', 'One stage at a time', `Move ${a.number} to the next or previous stage only.`);
      return;
    }
    const kind: MoveKind = target > from ? 'forward' : 'back';
    try {
      if (kind === 'back' || to === 'SiteVisitScheduled') {
        // These need a reason or a date: confirm in the dialog.
        this.move.set({ detail: await firstValueFrom(this.api.get(a.id)), kind, to });
        return;
      }
      const d = await firstValueFrom(this.api.move(a.id, to, null));
      this.toast.ok(statusMeta(to).label, d.assignment.number);
      void this.load();
    } catch (err) {
      this.toast.error(`Could not move ${a.number}`, toProblem(err).message);
    }
  }

  protected onMoved(d: AssignmentDetail): void {
    this.move.set(null);
    this.toast.ok(statusMeta(d.assignment.status).label, d.assignment.number);
    void this.load();
  }
}
