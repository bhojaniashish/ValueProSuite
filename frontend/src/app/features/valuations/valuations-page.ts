import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { timeAgo } from '../../core/ui/format';
import { TabItem, Tabs } from '../../shared/tabs';
import { shortDate, statusMeta } from '../assignments/assignments.api';
import { inr, ValuationListItem, ValuationsApi } from './valuations.api';

/** Cases waiting for a valuation (site visit done), and those already valued. */
@Component({
  selector: 'vp-valuations-page',
  imports: [RouterLink, Tabs],
  template: `
    <div class="page-head">
      <div>
        <h2>Valuation Engine</h2>
        <p>Work out fair market, realizable, distress and guideline values from the inspection, the Rate Master and each bank's rules.</p>
      </div>
    </div>

    <div class="card">
      <vp-tabs [tabs]="tabs()" [value]="done() ? 'done' : 'todo'" (valueChange)="setDone($event === 'done')" />
      <div class="toolbar">
        <div class="search">
          <i class="fa-solid fa-magnifying-glass"></i>
          <input type="search" placeholder="Search case number, applicant, property" aria-label="Search valuations" (input)="onSearch($any($event.target).value)" />
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
        <div class="empty"><i class="fa-solid fa-triangle-exclamation"></i><b>Could not load valuations</b><span>{{ error() }}</span>
          <button class="btn btn-soft btn-sm" type="button" (click)="load()">Try again</button></div>
      } @else if (!items().length && !loading()) {
        <div class="empty">
          <i class="fa-solid" [class]="done() ? 'fa-calculator' : 'fa-mug-hot'"></i>
          <b>{{ done() ? 'No valuations yet' : 'Nothing waiting to be valued' }}</b>
          <span>{{ done() ? 'Saved valuations show here.' : 'Cases show here once the site visit is done.' }}</span>
        </div>
      } @else {
        <div class="list">
          @for (v of items(); track v.assignmentId) {
            <a class="row" [routerLink]="['/valuation', v.assignmentId]">
              <div class="main">
                <div class="l1">
                  <span class="bank" [style.background]="v.bankColor">{{ v.bankShortCode }}</span>
                  <span class="num">{{ v.number }}</span>
                  <span class="badge" [class]="statusMeta(v.status).badge">{{ statusMeta(v.status).label }}</span>
                </div>
                <b>{{ v.applicantName }}</b>
                <span class="small muted">{{ v.propertySummary }}@if (!mine()) { · {{ v.valuerName }} }</span>
              </div>
              <div class="val">
                @if (v.fairMarketValue != null) {
                  <b class="tnum">{{ inr(v.fairMarketValue) }}</b><span class="small muted">{{ timeAgo(v.valuedAt) }}</span>
                } @else {
                  <span class="small muted">Due {{ shortDate(v.dueDate) }}</span>
                  <span class="go-btn">Value <i class="fa-solid fa-arrow-right"></i></span>
                }
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
    .go-btn { font-size: 13px; font-weight: 600; color: var(--brand); }
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
export class ValuationsPage implements OnInit {
  private readonly api = inject(ValuationsApi);
  private readonly auth = inject(AuthService);

  protected readonly inr = inr;
  protected readonly statusMeta = statusMeta;
  protected readonly shortDate = shortDate;
  protected readonly timeAgo = timeAgo;
  protected readonly canSeeAll = computed(() => this.auth.hasPermission(Permission.assignmentsManage));
  protected readonly done = signal(false);
  protected readonly mine = signal(true);
  protected readonly items = signal<ValuationListItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  private search = '';
  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  protected readonly tabs = computed<TabItem[]>(() => [
    { value: 'todo', label: 'To value', icon: 'fa-hourglass-half', count: this.done() ? null : this.items().length },
    { value: 'done', label: 'Valued', icon: 'fa-calculator', count: this.done() ? this.items().length : null },
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

  protected onSearch(v: string): void {
    this.search = v.trim();
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.load(), 300);
  }
}
