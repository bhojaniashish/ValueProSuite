import { Component, computed, inject, input, OnDestroy, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { BankBadge } from './bank-badge';
import { BankFormDialog } from './bank-form-dialog';
import { Bank, BankDetail, BanksApi, Branch, depreciationLabel, Question, rateBasisLabel } from './banks.api';
import { BranchFormDialog } from './branch-form-dialog';
import { QuestionnaireEditor } from './questionnaire-editor';

@Component({
  selector: 'vp-bank-detail-page',
  imports: [RouterLink, BankBadge, BankFormDialog, BranchFormDialog, QuestionnaireEditor, Dialog],
  template: `
    <a class="back small" routerLink="/banks"><i class="fa-solid fa-arrow-left"></i> All banks</a>

    @if (error()) {
      <div class="card card-pad"><div class="alert-inline danger"><i class="fa-solid fa-triangle-exclamation"></i> {{ error() }}</div></div>
    } @else if (detail(); as d) {
      <div class="card head">
        <vp-bank-badge [code]="d.bank.shortCode" [color]="d.bank.brandColor" style="--size: 56px" />
        <div class="title">
          <h2>{{ d.bank.name }} @if (!d.bank.isActive) { <span class="badge b-gray">Inactive</span> }</h2>
          <p class="muted">{{ d.bank.reportFormatName || 'Firm default format' }}{{ d.bank.panelCode ? ' · Panel ' + d.bank.panelCode : '' }}{{ d.bank.ifscPrefix ? ' · IFSC ' + d.bank.ifscPrefix + '…' : '' }}</p>
        </div>
        @if (canManage()) {
          <div class="actions">
            <button class="btn btn-ghost btn-sm" type="button" (click)="editBank.set(true)"><i class="fa-solid fa-pen"></i> Edit</button>
            <button class="btn btn-ghost btn-sm danger" type="button" (click)="deleteBank.set(true)"><i class="fa-solid fa-trash-can"></i> Remove</button>
          </div>
        }
        <div class="rules">
          <div><span>Rate basis</span><b>{{ basis(d.bank) }}</b></div>
          <div><span>Distress value</span><b class="tnum">{{ d.bank.rules.distressPercent }}%</b></div>
          <div><span>Realizable value</span><b class="tnum">{{ d.bank.rules.realizablePercent }}%</b></div>
          <div><span>Depreciation</span><b>{{ dep(d.bank) }}</b></div>
        </div>
      </div>

      <div class="card">
        <div class="card-head">
          <h3>Branches</h3>
          <span class="sub">{{ branches().length }} branch{{ branches().length === 1 ? '' : 'es' }} · {{ activeBranches() }} active</span>
          @if (canManage()) {
            <button class="btn btn-pri btn-sm ml" type="button" (click)="editBranch.set(null)"><i class="fa-solid fa-plus"></i> Add Branch</button>
          }
        </div>
        @if (d.bank.branches > 5) {
          <div class="search">
            <i class="fa-solid fa-magnifying-glass"></i>
            <input type="search" placeholder="Search branch, IFSC, city or contact" aria-label="Search branches" (input)="onSearch($any($event.target).value)" />
          </div>
        }
        @if (!branches().length) {
          <div class="empty">
            <i class="fa-solid fa-code-branch"></i>
            <span>{{ search ? 'No branches match.' : 'No branches yet. Add the branches that send you valuation work.' }}</span>
          </div>
        } @else {
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Branch</th><th>IFSC</th><th class="hide-sm">Contact</th><th>Status</th>@if (canManage()) { <th class="right" aria-label="Actions"></th> }</tr></thead>
              <tbody>
                @for (b of branches(); track b.id) {
                  <tr>
                    <td><b>{{ b.name }}</b><span class="small muted block">{{ place(b) }}</span></td>
                    <td class="tnum small">{{ b.ifsc || '—' }}</td>
                    <td class="hide-sm">
                      <span class="small">{{ b.contactPerson || '—' }}</span>
                      <span class="small muted block tnum">
                        @if (b.phone) { <a [href]="'tel:' + digits(b.phone)">{{ b.phone }}</a> } @else { — }
                      </span>
                    </td>
                    <td><span class="badge" [class]="b.isActive ? 'b-ok' : 'b-gray'"><i class="fa-solid fa-circle"></i> {{ b.isActive ? 'Active' : 'Inactive' }}</span></td>
                    @if (canManage()) {
                      <td class="right nowrap">
                        <button class="btn-icon" type="button" [attr.aria-label]="'Edit ' + b.name" (click)="editBranch.set(b)"><i class="fa-solid fa-pen"></i></button>
                        <button class="btn-icon danger" type="button" [attr.aria-label]="'Remove ' + b.name" (click)="deleteBranch.set(b)"><i class="fa-solid fa-trash-can"></i></button>
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <vp-questionnaire-editor [bank]="d.bank" [value]="d.questions" [canEdit]="canManage()" (saved)="onQuestions($event)" />

      @if (editBank()) {
        <vp-bank-form-dialog [bank]="d.bank" (saved)="onBankSaved($event)" (closed)="editBank.set(false)" />
      }
      @if (editBranch() !== undefined) {
        <vp-branch-form-dialog [bank]="d.bank" [branch]="editBranch() ?? null" (saved)="onBranchSaved($event)" (closed)="editBranch.set(undefined)" />
      }
      @if (deleteBranch(); as b) {
        <vp-dialog title="Remove branch?" [subtitle]="d.bank.shortCode + ' · ' + b.name" icon="fa-trash-can" tone="b-danger" size="sm" (closed)="deleteBranch.set(null)">
          <p class="confirm">{{ b.name }} will be removed from {{ d.bank.shortCode }}'s branches. Past records are kept.</p>
          <ng-container footer>
            <button class="btn btn-ghost" type="button" (click)="deleteBranch.set(null)">Cancel</button>
            <button class="btn btn-danger" type="button" [disabled]="busy()" (click)="removeBranch(b)"><i class="fa-solid fa-trash-can"></i> Remove</button>
          </ng-container>
        </vp-dialog>
      }
      @if (deleteBank()) {
        <vp-dialog title="Remove bank?" [subtitle]="d.bank.name" icon="fa-trash-can" tone="b-danger" size="sm" (closed)="deleteBank.set(false)">
          <p class="confirm">{{ d.bank.name }} and its {{ d.bank.branches }} branch{{ d.bank.branches === 1 ? '' : 'es' }} will be removed. Past records are kept.</p>
          <ng-container footer>
            <button class="btn btn-ghost" type="button" (click)="deleteBank.set(false)">Cancel</button>
            <button class="btn btn-danger" type="button" [disabled]="busy()" (click)="removeBank(d.bank)"><i class="fa-solid fa-trash-can"></i> Remove bank</button>
          </ng-container>
        </vp-dialog>
      }
    } @else {
      <div class="card card-pad muted"><i class="fa-solid fa-spinner fa-spin"></i> Loading…</div>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 18px; }
    .back { align-self: flex-start; display: inline-flex; gap: 6px; align-items: center; font-weight: 600; text-decoration: none; margin-bottom: -6px; }
    .head { display: flex; flex-wrap: wrap; align-items: center; gap: 16px; padding: 20px; }
    .title { flex: 1; min-width: 200px; }
    .title h2 { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin: 0 0 4px; font-size: 21px; }
    .title p { margin: 0; }
    .actions { display: flex; gap: 8px; }
    .btn.danger:hover { border-color: var(--danger); color: var(--danger); background: var(--danger-soft); }
    .rules { display: grid; grid-template-columns: repeat(4, 1fr); flex-basis: 100%; gap: 10px; padding-top: 14px; border-top: 1px solid var(--line-2); }
    @media (max-width: 767px) { .rules { grid-template-columns: 1fr 1fr; } }
    .rules span { display: block; font-size: 11.5px; font-weight: 600; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); }
    .rules b { font-size: 15px; }
    .ml { margin-left: auto; }
    .search { position: relative; margin: 12px 16px 0; max-width: 380px; }
    .search i { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); color: var(--muted-2); font-size: 13px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px 20px; text-align: center; color: var(--muted); }
    .empty i { font-size: 24px; color: var(--muted-2); }
    .block { display: block; }
    .nowrap { white-space: nowrap; }
    .btn-icon.danger:hover:not(:disabled) { background: var(--danger-soft); color: var(--danger); }
    .confirm { margin: 0; line-height: 1.55; }
    @media (max-width: 599px) { .hide-sm { display: none; } }
  `,
})
export class BankDetailPage implements OnInit, OnDestroy {
  private readonly api = inject(BanksApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Route parameter :id. */
  readonly id = input.required<string>();

  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.banksManage));
  protected readonly detail = signal<BankDetail | null>(null);
  protected readonly branches = signal<Branch[]>([]);
  protected readonly activeBranches = computed(() => this.branches().filter((b) => b.isActive).length);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly editBank = signal(false);
  protected readonly deleteBank = signal(false);
  protected readonly editBranch = signal<Branch | null | undefined>(undefined);
  protected readonly deleteBranch = signal<Branch | null>(null);
  protected search = '';
  private timer?: ReturnType<typeof setTimeout>;

  async ngOnInit(): Promise<void> {
    try {
      const [detail, branches] = await Promise.all([firstValueFrom(this.api.get(this.id())), firstValueFrom(this.api.branches(this.id()))]);
      this.detail.set(detail);
      this.branches.set(branches);
      if (location.hash === '#questionnaire') setTimeout(() => document.getElementById('questionnaire')?.scrollIntoView({ behavior: 'smooth' }));
    } catch (e) {
      this.error.set(toProblem(e).status === 404 ? 'This bank no longer exists.' : toProblem(e).message);
    }
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }

  protected basis(b: Bank): string { return rateBasisLabel(b.rules.rateBasis); }
  protected dep(b: Bank): string { return depreciationLabel(b.rules.depreciation); }
  protected digits(phone: string): string { return phone.replace(/[^\d+]/g, ''); }
  protected place(b: Branch): string { return [b.city, b.state].filter((x) => !!x).join(', ') || '—'; }

  protected onSearch(value: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(async () => {
      this.search = value.trim();
      this.branches.set(await firstValueFrom(this.api.branches(this.id(), this.search)));
    }, 300);
  }

  private async reload(): Promise<void> {
    const [detail, branches] = await Promise.all([firstValueFrom(this.api.get(this.id())), firstValueFrom(this.api.branches(this.id(), this.search))]);
    this.detail.set(detail);
    this.branches.set(branches);
  }

  protected onBankSaved(bank: Bank): void {
    this.editBank.set(false);
    this.toast.ok('Bank updated', bank.name);
    void this.reload();
  }

  protected onBranchSaved(branch: Branch): void {
    const isNew = !this.editBranch();
    this.editBranch.set(undefined);
    this.toast.ok(isNew ? 'Branch added' : 'Branch updated', branch.name);
    void this.reload();
  }

  protected onQuestions(questions: Question[]): void {
    this.detail.update((d) => (d ? { ...d, questions, bank: { ...d.bank, questions: questions.length } } : d));
  }

  protected async removeBranch(b: Branch): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.deleteBranch(this.id(), b.id));
      this.deleteBranch.set(null);
      this.toast.ok('Branch removed', b.name);
      void this.reload();
    } catch (e) {
      this.toast.error('Could not remove branch', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeBank(b: Bank): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(b.id));
      this.toast.ok('Bank removed', b.name);
      await this.router.navigateByUrl('/banks');
    } catch (e) {
      this.toast.error('Could not remove bank', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
