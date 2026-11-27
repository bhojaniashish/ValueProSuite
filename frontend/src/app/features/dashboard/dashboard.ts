import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { avatarColor, initials, timeAgo } from '../../core/ui/format';
import { NAVIGATION } from '../../layout/navigation';
import { AssignmentStatus, caseTypeLabel, rupees, shortDate, statusMeta } from '../assignments/assignments.api';
import { AttentionReason, DashboardApi, DashboardSummary } from './dashboard.api';

type Scope = 'all' | 'mine';
const SCOPE_KEY = 'vp.dashboard.scope';

/**
 * Home page. People with the dashboard permission get the day's picture: what is late or due, site visits, the pipeline,
 * banks, team workload, the six-month trend, recent activity and, for billing users, money owed.
 * Everyone else (e.g. bank users) gets their module tiles.
 */
@Component({
  selector: 'vp-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(DashboardApi);

  protected readonly meta = statusMeta;
  protected readonly caseType = caseTypeLabel;
  protected readonly date = shortDate;
  protected readonly ago = timeAgo;
  protected readonly initials = initials;
  protected readonly avatarColor = avatarColor;

  protected readonly canView = computed(() => this.auth.hasPermission(Permission.dashboardView));
  protected readonly canCreate = computed(() => this.auth.hasPermission(Permission.assignmentsManage));
  protected readonly canAssignments = computed(() => this.auth.hasPermission(Permission.assignmentsView));
  protected readonly firstName = computed(() => this.auth.user()?.fullName.split(' ')[0] ?? '');

  protected readonly scope = signal<Scope>(this.storedScope());
  protected readonly data = signal<DashboardSummary | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly modules = computed(() =>
    NAVIGATION.flatMap((g) => g.items).filter((i) => i.path !== 'dashboard' && this.auth.hasPermission(i.permission)),
  );

  protected readonly greeting = computed(() => {
    const h = new Date(Date.now() + 5.5 * 3600_000).getUTCHours();
    return h < 12 ? 'Good morning' : h < 17 ? 'Good afternoon' : 'Good evening';
  });

  protected readonly todayLabel = computed(() => {
    const d = this.data()?.today;
    return d ? new Date(d + 'T00:00:00').toLocaleDateString('en-IN', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }) : '';
  });

  /** Stages shown on the pipeline bar: open work plus Submitted (waiting to be billed). */
  protected readonly pipeline = computed(() => {
    const p = this.data()?.pipeline ?? [];
    const shown = p.filter((s) => s.status !== 'InvoiceGenerated');
    const total = shown.reduce((n, s) => n + s.count, 0);
    return { total, stages: shown.map((s) => ({ ...s, pct: total ? (s.count / total) * 100 : 0 })) };
  });

  protected readonly trendMax = computed(() => Math.max(1, ...(this.data()?.trend ?? []).flatMap((m) => [m.received, m.completed])));
  protected readonly trendFees = computed(() => (this.data()?.trend ?? []).reduce((n, m) => n + m.fees, 0));
  protected readonly bankMax = computed(() => Math.max(1, ...(this.data()?.byBank ?? []).map((b) => b.open)));
  protected readonly workMax = computed(() => Math.max(1, ...(this.data()?.workload ?? []).map((w) => w.open)));

  /** This month's completions against last month's. */
  protected readonly completedDelta = computed(() => {
    const k = this.data()?.kpis;
    return k ? k.completedThisMonth - k.completedLastMonth : 0;
  });

  ngOnInit(): void {
    if (this.canView()) void this.load();
  }

  protected setScope(s: Scope): void {
    if (s === this.scope()) return;
    this.scope.set(s);
    try { localStorage.setItem(SCOPE_KEY, s); } catch { /* ignore */ }
    void this.load();
  }

  /** People who manage cases see the whole firm first; valuers, architects and reviewers see their own. */
  private storedScope(): Scope {
    try {
      const v = localStorage.getItem(SCOPE_KEY);
      if (v === 'all' || v === 'mine') return v;
    } catch { /* ignore */ }
    return this.auth.hasPermission(Permission.assignmentsManage) ? 'all' : 'mine';
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.data.set(await firstValueFrom(this.api.get(this.scope() === 'mine')));
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected reasonText(r: AttentionReason, days: number): string {
    switch (r) {
      case 'overdue': return `${days} day${days === 1 ? '' : 's'} late`;
      case 'today': return 'Due today';
      case 'review': return 'Your review';
      default: return days === 1 ? 'Due tomorrow' : `Due in ${days} days`;
    }
  }

  protected reasonBadge(r: AttentionReason): string {
    return r === 'overdue' ? 'b-danger' : r === 'today' ? 'b-warn' : r === 'review' ? 'b-purple' : 'b-info';
  }

  protected statusColor(s: AssignmentStatus): string {
    const tone = this.meta(s).badge.replace('b-', '');
    return tone === 'gray' ? 'var(--muted-2)' : `var(--${tone})`;
  }

  protected visitDay(iso: string): string {
    const d = new Date(iso);
    const key = (x: Date) => x.toLocaleDateString('en-CA', { timeZone: 'Asia/Kolkata' });
    const today = this.data()?.today ?? key(new Date());
    const tomorrow = key(new Date(new Date(today + 'T12:00:00+05:30').getTime() + 86_400_000));
    return key(d) === today ? 'Today' : key(d) === tomorrow ? 'Tomorrow' : d.toLocaleDateString('en-IN', { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'Asia/Kolkata' });
  }

  protected visitTime(iso: string): string {
    return new Date(iso).toLocaleTimeString('en-IN', { hour: 'numeric', minute: '2-digit', timeZone: 'Asia/Kolkata' });
  }

  protected short(v: number): string {
    if (v >= 1_00_00_000) return `₹${(v / 1_00_00_000).toFixed(2).replace(/\.?0+$/, '')} Cr`;
    if (v >= 1_00_000) return `₹${(v / 1_00_000).toFixed(2).replace(/\.?0+$/, '')} L`;
    return rupees(v);
  }

  protected pct(v: number, max: number): number {
    return Math.round((v / max) * 100);
  }

  /** Assignment list links that keep the dashboard's scope. */
  protected listParams(extra: Record<string, string>): Record<string, string> {
    return this.scope() === 'mine' ? { ...extra, mine: '1' } : extra;
  }
}
