import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/ui/toast';
import { BankBadge } from './bank-badge';
import { BankFormDialog } from './bank-form-dialog';
import { BankRulesDialog } from './bank-rules-dialog';
import { Bank, BanksApi, rateBasisLabel } from './banks.api';

@Component({
  selector: 'vp-banks-page',
  imports: [RouterLink, BankBadge, BankFormDialog, BankRulesDialog],
  template: `
    <div class="page-head">
      <div>
        <h2>Bank Management</h2>
        <p>Manage empanelled banks, branches, formats &amp; valuation rules</p>
      </div>
      <div class="actions">
        @if (banks().length) {
          <button class="btn btn-ghost" type="button" (click)="rules.set(true)"><i class="fa-solid fa-sliders"></i> Valuation Rules</button>
        }
        @if (canManage()) {
          <button class="btn btn-pri" type="button" (click)="editing.set(null)"><i class="fa-solid fa-plus"></i> Add Bank</button>
        }
      </div>
    </div>

    <div class="toolbar card">
      <div class="search">
        <i class="fa-solid fa-magnifying-glass"></i>
        <input type="search" placeholder="Search bank, branch, IFSC or city" aria-label="Search banks" (input)="onSearch($any($event.target).value)" />
      </div>
      <span class="small muted">{{ banks().length }} bank{{ banks().length === 1 ? '' : 's' }} · {{ totalBranches() }} branch{{ totalBranches() === 1 ? '' : 'es' }}</span>
      @if (loading()) { <i class="fa-solid fa-spinner fa-spin muted"></i> }
    </div>

    @if (error()) {
      <div class="card empty"><i class="fa-solid fa-triangle-exclamation"></i><b>Could not load banks</b><span>{{ error() }}</span>
        <button class="btn btn-soft btn-sm" type="button" (click)="load()">Try again</button></div>
    } @else if (!loading() && !banks().length) {
      <div class="card empty">
        <i class="fa-solid fa-building-columns"></i>
        <b>{{ search ? 'No banks match' : 'No banks yet' }}</b>
        <span>{{ search ? 'Try another name, branch, IFSC or city.' : 'Add the banks your firm is empanelled with. Common banks can be picked from a list.' }}</span>
        @if (!search && canManage()) { <button class="btn btn-pri btn-sm" type="button" (click)="editing.set(null)"><i class="fa-solid fa-plus"></i> Add Bank</button> }
      </div>
    } @else {
      <div class="grid">
        @for (b of banks(); track b.id) {
          <div class="card bank" [class.inactive]="!b.isActive">
            <a class="top" [routerLink]="['/banks', b.id]">
              <vp-bank-badge [code]="b.shortCode" [color]="b.brandColor" />
              <div class="name">
                <b>{{ b.name }}</b>
                <span class="small muted">{{ b.reportFormat || 'Standard Format' }}</span>
              </div>
              @if (!b.isActive) { <span class="badge b-gray">Inactive</span> }
            </a>
            <div class="stats">
              <div><b class="tnum">{{ b.branches }}</b><span>Branches</span></div>
              <div><b class="tnum">{{ b.activeBranches }}</b><span>Active</span></div>
              <div><b class="tnum">{{ b.rules.distressPercent }}%</b><span>Distress</span></div>
            </div>
            <div class="meta small muted">
              <span><i class="fa-solid fa-scale-balanced"></i> {{ basis(b) }}</span>
              <span><i class="fa-solid fa-clipboard-question"></i> {{ b.questions }} question{{ b.questions === 1 ? '' : 's' }}</span>
            </div>
            <div class="foot">
              <a class="btn btn-soft btn-sm" [routerLink]="['/banks', b.id]"><i class="fa-solid fa-code-branch"></i> Branches</a>
              <a class="btn btn-ghost btn-sm" [routerLink]="['/banks', b.id]" fragment="questionnaire"><i class="fa-solid fa-clipboard-question"></i> Format</a>
              @if (canManage()) {
                <button class="btn-icon" type="button" title="Edit" [attr.aria-label]="'Edit ' + b.name" (click)="editing.set(b)"><i class="fa-solid fa-pen"></i></button>
              }
            </div>
          </div>
        }
      </div>
    }

    @if (editing() !== undefined) {
      <vp-bank-form-dialog [bank]="editing() ?? null" [existingCodes]="codes()" (saved)="onSaved($event)" (closed)="editing.set(undefined)" />
    }
    @if (rules()) {
      <vp-bank-rules-dialog [banks]="banks()" [canEdit]="canManage()" (saved)="onRulesSaved($event)" (closed)="rules.set(false)" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 18px; }
    .page-head { margin-bottom: 0; }
    .toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; padding: 12px 16px; }
    .search { position: relative; flex: 1 1 260px; max-width: 420px; }
    .search i { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); color: var(--muted-2); font-size: 13px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; }
    .bank { display: flex; flex-direction: column; }
    .bank.inactive { opacity: .7; }
    .top { display: flex; align-items: center; gap: 14px; padding: 18px 18px 14px; color: inherit; text-decoration: none; }
    .top:hover b { color: var(--brand); }
    .name { display: flex; flex: 1; flex-direction: column; gap: 2px; min-width: 0; }
    .name b { font-size: 15px; }
    .stats { display: grid; grid-template-columns: repeat(3, 1fr); margin: 0 18px; padding: 12px 0; border-top: 1px solid var(--line-2); border-bottom: 1px solid var(--line-2); text-align: center; }
    .stats b { display: block; font-size: 18px; }
    .stats span { font-size: 11.5px; color: var(--muted); }
    .meta { display: flex; flex-wrap: wrap; gap: 6px 14px; padding: 10px 18px 0; }
    .meta i { margin-right: 4px; }
    .foot { display: flex; align-items: center; gap: 8px; margin-top: auto; padding: 14px 18px 16px; }
    .foot .btn-icon { margin-left: auto; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 48px 16px; text-align: center; color: var(--muted); }
    .empty i { margin-bottom: 6px; font-size: 26px; color: var(--muted-2); }
    .empty b { color: var(--ink); }
    .empty .btn { margin-top: 8px; }
  `,
})
export class BanksPage implements OnInit, OnDestroy {
  private readonly api = inject(BanksApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);

  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.banksManage));
  protected readonly banks = signal<Bank[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly editing = signal<Bank | null | undefined>(undefined);
  protected readonly rules = signal(false);
  protected readonly totalBranches = computed(() => this.banks().reduce((n, b) => n + b.branches, 0));
  protected readonly codes = computed(() => this.banks().map((b) => b.shortCode));
  protected search = '';

  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }

  protected basis(b: Bank): string {
    return rateBasisLabel(b.rules.rateBasis);
  }

  protected onSearch(value: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      this.search = value.trim();
      void this.load();
    }, 300);
  }

  protected async load(): Promise<void> {
    const seq = ++this.seq;
    this.loading.set(true);
    try {
      const banks = await firstValueFrom(this.api.list(this.search));
      if (seq === this.seq) {
        this.banks.set(banks);
        this.error.set(null);
      }
    } catch (e) {
      if (seq === this.seq) this.error.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  protected onSaved(bank: Bank): void {
    const isNew = !this.editing();
    this.editing.set(undefined);
    this.toast.ok(isNew ? 'Bank added' : 'Bank updated', isNew ? `Now add ${bank.shortCode}'s branches.` : bank.name);
    void this.load();
  }

  protected onRulesSaved(banks: Bank[]): void {
    this.rules.set(false);
    this.banks.set(banks);
    this.toast.ok('Valuation rules saved');
  }
}
