import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { timeAgo } from '../../core/ui/format';
import { TabItem, Tabs } from '../../shared/tabs';
import { shortDate, statusMeta } from '../assignments/assignments.api';
import { Drafts } from './field-tools';
import { InspectionListItem, InspectionsApi } from './inspections.api';
import { PhotoQueue } from '../photos/photo-queue';

/** Site visits: the valuer's list of properties to inspect, ordered by visit time. */
@Component({
  selector: 'vp-inspections-page',
  imports: [RouterLink, Tabs],
  template: `
    <div class="page-head">
      <div>
        <h2>Site Inspection</h2>
        <p>Properties to visit, ordered by visit time. Works on a phone, even with weak signal.</p>
      </div>
    </div>

    @if (waiting(); as n) {
      <div class="alert-inline info"><i class="fa-solid fa-cloud-arrow-up"></i> {{ n }} photo{{ n === 1 ? ' is' : 's are' }} waiting on this phone to upload.</div>
    }

    <div class="card">
      <vp-tabs [tabs]="tabs()" [value]="done() ? 'done' : 'todo'" (valueChange)="setDone($event === 'done')" />
      <div class="toolbar">
        <div class="search">
          <i class="fa-solid fa-magnifying-glass"></i>
          <input type="search" placeholder="Search case number, applicant, property" aria-label="Search site visits" (input)="onSearch($any($event.target).value)" />
        </div>
        @if (canSeeAll()) {
          <label class="mine">
            <span class="switch"><input type="checkbox" [checked]="mine()" (change)="setMine($any($event.target).checked)" /><span class="track"></span></span>
            <span class="small">Only mine</span>
          </label>
        }
        @if (loading()) { <i class="fa-solid fa-spinner fa-spin muted"></i> }
      </div>

      @if (error()) {
        <div class="empty"><i class="fa-solid fa-triangle-exclamation"></i><b>Could not load site visits</b><span>{{ error() }}</span>
          <button class="btn btn-soft btn-sm" type="button" (click)="load()">Try again</button></div>
      } @else if (!items().length && !loading()) {
        <div class="empty">
          <i class="fa-solid" [class]="done() ? 'fa-clipboard-check' : 'fa-mug-hot'"></i>
          <b>{{ done() ? 'No inspections submitted yet' : 'No site visits pending' }}</b>
          <span>{{ done() ? 'Submitted inspections show here.' : mine() ? 'Cases assigned to you that need a visit show here.' : 'New and scheduled cases show here.' }}</span>
        </div>
      } @else {
        <div class="list">
          @for (i of items(); track i.assignmentId) {
            <a class="row" [routerLink]="['/inspection', i.assignmentId]">
              <div class="when" [class.today]="isToday(i.siteVisitAt)">
                @if (i.siteVisitAt) {
                  <b>{{ dayLabel(i.siteVisitAt) }}</b><span>{{ time(i.siteVisitAt) }}</span>
                } @else {
                  <b>Not</b><span>scheduled</span>
                }
              </div>
              <div class="main">
                <div class="l1">
                  <span class="bank" [style.background]="i.bankColor">{{ i.bankShortCode }}</span>
                  <span class="num">{{ i.number }}</span>
                  @if (!mine()) { <span class="small muted">· {{ i.valuerName }}</span> }
                </div>
                <b>{{ i.applicantName }}</b>
                <span class="small muted">{{ i.propertySummary }}</span>
              </div>
              <div class="state">
                @if (i.status === 'Submitted') {
                  <span class="badge b-ok"><i class="fa-solid fa-circle-check"></i> Submitted</span>
                } @else if (i.status === 'Draft' || hasLocal(i.assignmentId)) {
                  <span class="badge b-warn"><i class="fa-solid fa-pen"></i> In progress</span>
                } @else {
                  <span class="badge" [class]="statusMeta(i.assignmentStatus).badge">{{ statusMeta(i.assignmentStatus).label }}</span>
                }
                <span class="small muted">
                  @if (i.photoCount) { <i class="fa-solid fa-camera"></i> {{ i.photoCount }} · }
                  @if (i.updatedAt) { {{ timeAgo(i.updatedAt) }} } @else { Due {{ shortDate(i.dueDate) }} }
                </span>
              </div>
              <i class="fa-solid fa-chevron-right go"></i>
            </a>
          }
        </div>
      }
    </div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 16px; }
    .page-head { margin-bottom: 0; }
    .card { padding: 0; overflow: hidden; }
    .toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; padding: 14px 18px; border-bottom: 1px solid var(--line-2); }
    .search { max-width: none; }
    .mine { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; }
    .list { display: flex; flex-direction: column; }
    .row { display: grid; grid-template-columns: 64px minmax(0, 1fr) auto 16px; align-items: center; gap: 14px; padding: 14px 18px;
      border-bottom: 1px solid var(--line-2); color: inherit; text-decoration: none; }
    .row:last-child { border-bottom: none; }
    .row:hover { background: #fafbfe; }
    .when { display: flex; flex-direction: column; align-items: center; padding: 8px 4px; border-radius: 10px; background: var(--bg-2); text-align: center; }
    .when b { font-size: 13px; }
    .when span { font-size: 11.5px; color: var(--muted); }
    .when.today { background: var(--brand-soft); color: var(--brand); }
    .when.today span { color: var(--brand); }
    .main { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .main > * { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .l1 { display: flex; align-items: center; gap: 8px; }
    .bank { padding: 2px 7px; border-radius: 6px; color: #fff; font-size: 11px; font-weight: 700; }
    .num { font-size: 12.5px; font-weight: 600; color: var(--brand); }
    .state { display: flex; flex-direction: column; align-items: flex-end; gap: 4px; }
    .go { color: var(--muted-2); font-size: 12px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 48px 16px; text-align: center; color: var(--muted); }
    .empty i { font-size: 26px; color: var(--muted-2); margin-bottom: 6px; }
    .empty b { color: var(--ink); }
    @media (max-width: 599px) {
      .row { grid-template-columns: 56px minmax(0, 1fr) 12px; gap: 10px; padding: 12px 14px; }
      .state { grid-column: 2; grid-row: 2; flex-direction: row; align-items: center; justify-content: flex-start; flex-wrap: wrap; gap: 8px; }
      .go { grid-column: 3; grid-row: 1; }
      .toolbar { padding: 12px 14px; }
    }
  `,
})
export class InspectionsPage implements OnInit {
  private readonly api = inject(InspectionsApi);
  private readonly auth = inject(AuthService);
  private readonly queue = inject(PhotoQueue);

  protected readonly statusMeta = statusMeta;
  protected readonly shortDate = shortDate;
  protected readonly timeAgo = timeAgo;

  /** Managers can switch to everyone's visits; valuers see their own. */
  protected readonly canSeeAll = computed(() => this.auth.hasPermission(Permission.assignmentsManage));
  protected readonly done = signal(false);
  protected readonly mine = signal(true);
  protected readonly items = signal<InspectionListItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly waiting = computed(() => this.queue.items().filter((q) => !q.error).length);
  private search = '';
  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  protected readonly tabs = computed<TabItem[]>(() => [
    { value: 'todo', label: 'To visit', icon: 'fa-location-dot', count: this.done() ? null : this.items().length },
    { value: 'done', label: 'Submitted', icon: 'fa-clipboard-check', count: this.done() ? this.items().length : null },
  ]);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const seq = ++this.seq;
    this.loading.set(true);
    try {
      const items = await firstValueFrom(this.api.list(this.done(), this.mine(), this.search));
      if (seq !== this.seq) return;
      this.items.set(items);
      this.error.set(null);
    } catch (e) {
      if (seq === this.seq) this.error.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  protected setDone(v: boolean): void {
    this.done.set(v);
    void this.load();
  }

  protected setMine(v: boolean): void {
    this.mine.set(v);
    void this.load();
  }

  protected onSearch(v: string): void {
    this.search = v;
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.load(), 300);
  }

  protected hasLocal(id: string): boolean {
    return !!Drafts.get(id)?.dirty;
  }

  private ist(iso: string): Date {
    return new Date(iso);
  }

  protected isToday(iso: string | null): boolean {
    return !!iso && this.ist(iso).toDateString() === new Date().toDateString();
  }

  protected dayLabel(iso: string): string {
    const d = this.ist(iso);
    const today = new Date();
    const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
    if (d.toDateString() === today.toDateString()) return 'Today';
    if (d.toDateString() === tomorrow.toDateString()) return 'Tmrw';
    return d.toLocaleDateString('en-IN', { day: 'numeric', month: 'short' });
  }

  protected time(iso: string): string {
    return this.ist(iso).toLocaleTimeString('en-IN', { hour: 'numeric', minute: '2-digit' });
  }
}
