import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/ui/toast';
import { shortDate, statusMeta } from '../assignments/assignments.api';
import {
  addDays, addMonths, CalendarApi, CalendarFeed, CalendarItem, CaseToSchedule, clock, dayNumber, entryAt, ItemKind, itemBadge, itemIcon,
  label, LAYERS, monthStart, weekday, weekStart,
} from './calendar.api';
import { EntryDialog } from './entry-dialog';
import { VisitDialog, VisitTarget } from './visit-dialog';

type View = 'month' | 'week' | 'list';
type Scope = 'all' | 'mine';

const VIEW_KEY = 'vp.calendar.view';
const SCOPE_KEY = 'vp.calendar.scope';
const LAYER_KEY = 'vp.calendar.hidden';
const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

function load<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  try {
    const v = localStorage.getItem(key) as T | null;
    return v && allowed.includes(v) ? v : fallback;
  } catch {
    return fallback;
  }
}
function save(key: string, value: string): void {
  try { localStorage.setItem(key, value); } catch { /* ignore */ }
}

/** Today in India as yyyy-mm-dd. */
function indiaToday(): string {
  return new Date(Date.now() + 5.5 * 3600_000).toISOString().slice(0, 10);
}

/**
 * Site visits, report due dates, payments due and follow-ups in one place. Month, week and list views;
 * a day panel with what is on and quick actions; new cases waiting for a visit; drag a visit or entry to another day.
 */
@Component({
  selector: 'vp-calendar-page',
  imports: [NgTemplateOutlet, RouterLink, VisitDialog, EntryDialog],
  templateUrl: './calendar-page.html',
  styleUrl: './calendar-page.scss',
})
export class CalendarPage implements OnInit {
  private readonly api = inject(CalendarApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  /** ?date=yyyy-mm-dd opens that day. */
  readonly date = input<string>();

  protected readonly weekdays = WEEKDAYS;
  protected readonly layers = LAYERS;
  protected readonly badge = itemBadge;
  protected readonly icon = itemIcon;
  protected readonly clock = clock;
  protected readonly short = shortDate;
  protected readonly meta = statusMeta;
  protected readonly dayNumber = dayNumber;
  protected readonly label = label;

  protected readonly meId = computed(() => this.auth.user()?.id ?? null);
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.assignmentsManage));

  protected readonly view = signal<View>(load(VIEW_KEY, ['month', 'week', 'list'] as const, window.innerWidth < 768 ? 'list' : 'month'));
  protected readonly scope = signal<Scope>(load(SCOPE_KEY, ['all', 'mine'] as const, this.auth.hasPermission(Permission.assignmentsManage) ? 'all' : 'mine'));
  protected readonly hidden = signal<Set<ItemKind>>(this.loadHidden());
  protected readonly today = signal(indiaToday());
  /** The day in focus: drives which month/week is shown and the day panel. */
  protected readonly selected = signal(indiaToday());

  protected readonly feed = signal<CalendarFeed | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly visitTarget = signal<VisitTarget | null>(null);
  protected readonly entry = signal<{ item: CalendarItem | null; day: string } | null>(null);
  protected readonly dragging = signal<CalendarItem | null>(null);
  protected readonly dropDay = signal<string | null>(null);

  private seq = 0;

  /** First and last day shown (and fetched). */
  protected readonly range = computed(() => {
    const s = this.selected();
    if (this.view() === 'week') {
      const from = weekStart(s);
      return { from, to: addDays(from, 6) };
    }
    const first = monthStart(s);
    const last = addDays(addMonths(first, 1), -1);
    if (this.view() === 'list') return { from: first, to: last };
    return { from: weekStart(first), to: addDays(weekStart(last), 6) };
  });

  protected readonly title = computed(() => {
    const { from, to } = this.range();
    if (this.view() !== 'week') return label(monthStart(this.selected()), { month: 'long', year: 'numeric' });
    const sameMonth = from.slice(0, 7) === to.slice(0, 7);
    return sameMonth
      ? `${dayNumber(from)}–${label(to, { day: 'numeric', month: 'short', year: 'numeric' })}`
      : `${label(from, { day: 'numeric', month: 'short' })} – ${label(to, { day: 'numeric', month: 'short', year: 'numeric' })}`;
  });

  protected readonly visible = computed(() => {
    const hide = this.hidden();
    return (this.feed()?.items ?? []).filter((i) => !hide.has(i.kind));
  });

  private readonly byDay = computed(() => {
    const map = new Map<string, CalendarItem[]>();
    for (const i of this.visible()) {
      const list = map.get(i.date) ?? [];
      list.push(i);
      map.set(i.date, list);
    }
    return map;
  });

  protected readonly days = computed(() => {
    const { from, to } = this.range();
    const month = this.selected().slice(0, 7);
    const out: { date: string; items: CalendarItem[]; inMonth: boolean; weekend: boolean }[] = [];
    for (let d = from; d <= to; d = addDays(d, 1)) {
      out.push({ date: d, items: this.byDay().get(d) ?? [], inMonth: d.slice(0, 7) === month, weekend: weekday(d) === 6 });
    }
    return out;
  });

  protected readonly listDays = computed(() => this.days().filter((d) => d.items.length));
  protected readonly dayItems = computed(() => this.byDay().get(this.selected()) ?? []);
  protected readonly selectedLabel = computed(() => label(this.selected(), { weekday: 'long', day: 'numeric', month: 'long' }));

  protected readonly counts = computed(() => {
    const c: Record<ItemKind, number> = { visit: 0, due: 0, invoice: 0, entry: 0 };
    for (const i of this.feed()?.items ?? []) c[i.kind]++;
    return c;
  });
  protected readonly layerList = computed(() => this.layers.filter((l) => l.kind !== 'invoice' || this.feed()?.showsInvoices));

  ngOnInit(): void {
    const d = this.date();
    if (d && /^\d{4}-\d{2}-\d{2}$/.test(d)) this.selected.set(d);
    void this.reload();
  }

  // ---------------- navigation ----------------

  protected step(dir: -1 | 1): void {
    const s = this.selected();
    this.selected.set(this.view() === 'week' ? addDays(s, 7 * dir) : addMonths(s, dir));
    void this.reload();
  }

  protected goToday(): void {
    this.selected.set(this.today());
    void this.reload();
  }

  protected setView(v: View): void {
    this.view.set(v);
    save(VIEW_KEY, v);
    void this.reload();
  }

  protected setScope(s: Scope): void {
    if (s === this.scope()) return;
    this.scope.set(s);
    save(SCOPE_KEY, s);
    void this.reload();
  }

  protected toggleLayer(k: ItemKind): void {
    const next = new Set(this.hidden());
    if (next.has(k)) next.delete(k);
    else next.add(k);
    this.hidden.set(next);
    save(LAYER_KEY, [...next].join(','));
  }

  private loadHidden(): Set<ItemKind> {
    try {
      return new Set((localStorage.getItem(LAYER_KEY) ?? '').split(',').filter((x): x is ItemKind => ['visit', 'due', 'invoice', 'entry'].includes(x)));
    } catch {
      return new Set();
    }
  }

  protected pick(day: string): void {
    const before = this.range();
    this.selected.set(day);
    const after = this.range();
    if (before.from !== after.from || before.to !== after.to) void this.reload();
  }

  protected async reload(): Promise<void> {
    const { from, to } = this.range();
    const seq = ++this.seq;
    this.loading.set(true);
    this.error.set(null);
    try {
      const f = await firstValueFrom(this.api.feed(from, to, this.scope() === 'mine'));
      if (seq !== this.seq) return;
      this.feed.set(f);
      this.today.set(f.today);
    } catch (e) {
      if (seq === this.seq) this.error.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  // ---------------- items ----------------

  protected kindLabel(i: CalendarItem): string {
    switch (i.kind) {
      case 'visit': return i.done ? 'Site visit (done)' : 'Site visit';
      case 'due': return 'Report due';
      case 'invoice': return 'Payment due';
      default: return { FollowUp: 'Follow-up', Reminder: 'Reminder', Meeting: 'Meeting', Other: 'Entry' }[i.entryKind ?? 'Other'];
    }
  }

  /** Short text for a chip in the month grid. */
  protected chip(i: CalendarItem): string {
    const t = i.time ? clock(i.time).replace(':00', '') + ' ' : '';
    if (i.kind === 'visit' || i.kind === 'due') return t + (i.assignmentNumber?.replace(/^.*\//, '#') ?? '') + ' ' + i.title;
    return t + i.title;
  }

  protected open(i: CalendarItem): void {
    if (i.kind === 'entry' && i.canMove) {
      this.entry.set({ item: i, day: i.date });
      return;
    }
    this.pick(i.date);
  }

  protected newEntry(day = this.selected()): void {
    this.entry.set({ item: null, day });
  }

  protected moveVisit(i: CalendarItem): void {
    if (!i.assignmentId) return;
    this.visitTarget.set({ assignmentId: i.assignmentId, number: i.assignmentNumber ?? '', property: i.title, date: i.date, time: i.time });
  }

  protected book(c: CaseToSchedule): void {
    this.visitTarget.set({ assignmentId: c.id, number: c.number, property: c.propertySummary, date: null, time: null });
  }

  protected visitSaved(i: CalendarItem): void {
    this.visitTarget.set(null);
    this.toast.ok('Site visit booked', `${i.assignmentNumber}: ${label(i.date, { weekday: 'short', day: 'numeric', month: 'short' })}, ${clock(i.time)}`);
    this.selected.set(i.date);
    void this.reload();
  }

  protected entrySaved(i: CalendarItem): void {
    this.entry.set(null);
    this.toast.ok('Saved', i.title);
    void this.reload();
  }

  protected entryRemoved(): void {
    this.entry.set(null);
    this.toast.ok('Entry deleted');
    void this.reload();
  }

  protected async toggleDone(i: CalendarItem): Promise<void> {
    if (!i.entryId) return;
    try {
      await firstValueFrom(this.api.updateEntry(i.entryId, { ...entryAt(i, i.date), done: !i.done }));
      void this.reload();
    } catch (e) {
      this.toast.error('Could not update', toProblem(e).message);
    }
  }

  // ---------------- drag to another day (desktop) ----------------

  protected dragStart(e: DragEvent, i: CalendarItem): void {
    if (!i.canMove) return;
    this.dragging.set(i);
    e.dataTransfer?.setData('text/plain', i.key);
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }

  protected dragOver(e: DragEvent, day: string): void {
    const i = this.dragging();
    if (!i || (i.kind === 'visit' && day < this.today())) return;
    e.preventDefault();
    this.dropDay.set(day);
  }

  protected dragEnd(): void {
    this.dragging.set(null);
    this.dropDay.set(null);
  }

  protected async drop(e: DragEvent, day: string): Promise<void> {
    e.preventDefault();
    const i = this.dragging();
    this.dragEnd();
    if (!i || i.date === day) return;
    try {
      if (i.kind === 'visit' && i.assignmentId) {
        await firstValueFrom(this.api.scheduleVisit(i.assignmentId, day, i.time ?? '10:00'));
        this.toast.ok('Site visit moved', `${i.assignmentNumber} → ${label(day, { weekday: 'short', day: 'numeric', month: 'short' })}`);
      } else if (i.entryId) {
        await firstValueFrom(this.api.updateEntry(i.entryId, entryAt(i, day)));
        this.toast.ok('Moved', `${i.title} → ${label(day, { weekday: 'short', day: 'numeric', month: 'short' })}`);
      }
    } catch (err) {
      this.toast.error('Could not move', toProblem(err).message);
    }
    void this.reload();
  }
}
