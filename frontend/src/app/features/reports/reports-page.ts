import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { timeAgo } from '../../core/ui/format';
import { TabItem, Tabs } from '../../shared/tabs';
import { shortDate, statusMeta } from '../assignments/assignments.api';
import { inr } from '../valuations/valuations.api';
import { ReportListItem, ReportsApi, ReportStage } from './reports.api';

/** Reports by stage: to prepare (valued, drafting), in review, and submitted. */
@Component({
  selector: 'vp-reports-page',
  imports: [RouterLink, Tabs],
  template: `
    <div class="page-head">
      <div>
        <h2>Reports</h2>
        <p>Prepare valuation reports, send them for review, and approve the signed PDF that goes to the bank.</p>
      </div>
    </div>

    <div class="card">
      <vp-tabs [tabs]="tabs" [value]="stage()" (valueChange)="setStage($any($event))" />
      <div class="toolbar">
        <div class="search">
          <i class="fa-solid fa-magnifying-glass"></i>
          <input type="search" placeholder="Search case number, applicant, property" aria-label="Search reports" (input)="onSearch($any($event.target).value)" />
        </div>
        @if (canSeeAll()) {
          <label class="mine">
            <span class="switch"><input type="checkbox" [checked]="mine()" (change)="mine.set($any($event.target).checked); load()" /><span class="track"></span></span>
            <span class="small">Only mine</span>
          </label>
        }
        @if (loading()) { <i class="fa-solid fa-spinner fa-spin muted"></i> }
      </div>

      @if (error()) {
        <div class="empty"><i class="fa-solid fa-triangle-exclamation"></i><b>Could not load reports</b><span>{{ error() }}</span>
          <button class="btn btn-soft btn-sm" type="button" (click)="load()">Try again</button></div>
      } @else if (!items().length && !loading()) {
        <div class="empty">
          <i class="fa-solid fa-file-circle-check"></i>
          <b>{{ emptyText().title }}</b>
          <span>{{ emptyText().body }}</span>
        </div>
      } @else {
        <div class="list">
          @for (r of items(); track r.assignmentId) {
            <a class="row" [routerLink]="['/reports', r.assignmentId]">
              <div class="main">
                <div class="l1">
                  <span class="bank" [style.background]="r.bankColor">{{ r.bankShortCode }}</span>
                  <span class="num">{{ r.number }}</span>
                  <span class="badge" [class]="statusMeta(r.status).badge">{{ statusMeta(r.status).label }}</span>
                </div>
                <b>{{ r.applicantName }}</b>
                <span class="small muted">{{ r.propertySummary }} · {{ r.valuerName }}@if (r.reviewerName) { → {{ r.reviewerName }} }</span>
              </div>
              <div class="val">
                @if (r.fairMarketValue != null) { <b class="tnum">{{ inr(r.fairMarketValue) }}</b> } @else { <span class="small warn">Not valued yet</span> }
                <span class="small muted">
                  @if (r.lastGeneratedAt) { {{ r.drafts }} draft{{ r.drafts === 1 ? '' : 's' }} · {{ timeAgo(r.lastGeneratedAt) }} } @else { Due {{ shortDate(r.dueDate) }} }
                </span>
              </div>
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
    .row { display: flex; align-items: center; justify-content: space-between; gap: 14px; padding: 14px 18px; border-bottom: 1px solid var(--line-2);
      color: inherit; text-decoration: none; }
    .row:last-child { border-bottom: none; }
    .row:hover { background: #fafbfe; }
    .main { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .main > * { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .l1 { display: flex; align-items: center; gap: 8px; }
    .bank { padding: 2px 7px; border-radius: 6px; color: #fff; font-size: 11px; font-weight: 700; }
    .num { font-size: 12.5px; font-weight: 600; color: var(--brand); }
    .val { display: flex; flex: none; flex-direction: column; align-items: flex-end; gap: 4px; }
    .val b { font-size: 15px; color: var(--brand); }
    .warn { color: var(--warn); }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 48px 16px; text-align: center; color: var(--muted); }
    .empty i { font-size: 26px; color: var(--muted-2); margin-bottom: 6px; }
    .empty b { color: var(--ink); }
    @media (max-width: 599px) {
      .row { flex-direction: column; align-items: stretch; padding: 12px 14px; }
      .val { flex-direction: row; justify-content: space-between; align-items: center; }
      .toolbar { padding: 12px 14px; }
    }
  `,
})
export class ReportsPage implements OnInit {
  private readonly api = inject(ReportsApi);
  private readonly auth = inject(AuthService);

  protected readonly inr = inr;
  protected readonly statusMeta = statusMeta;
  protected readonly shortDate = shortDate;
  protected readonly timeAgo = timeAgo;
  protected readonly canSeeAll = computed(() => this.auth.hasPermission(Permission.assignmentsManage));
  protected readonly stage = signal<ReportStage>('draft');
  protected readonly mine = signal(true);
  protected readonly items = signal<ReportListItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  private search = '';
  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  protected readonly tabs: TabItem[] = [
    { value: 'draft', label: 'To prepare', icon: 'fa-file-pen' },
    { value: 'review', label: 'In review', icon: 'fa-magnifying-glass' },
    { value: 'done', label: 'Submitted', icon: 'fa-file-circle-check' },
  ];

  protected readonly emptyText = computed(() => ({
    draft: { title: 'No reports to prepare', body: 'Cases show here once the site inspection is done.' },
    review: { title: 'Nothing in review', body: 'Cases move here from "Draft Prepared" for the reviewer.' },
    done: { title: 'No submitted reports yet', body: 'Approved, signed reports show here.' },
  })[this.stage()]);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const seq = ++this.seq;
    this.loading.set(true);
    try {
      const items = await firstValueFrom(this.api.list(this.stage(), this.mine(), this.search));
      if (seq !== this.seq) return;
      this.items.set(items);
      this.error.set(null);
    } catch (e) {
      if (seq === this.seq) this.error.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  protected setStage(s: ReportStage): void {
    this.stage.set(s);
    void this.load();
  }

  protected onSearch(v: string): void {
    this.search = v.trim();
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.load(), 300);
  }
}
