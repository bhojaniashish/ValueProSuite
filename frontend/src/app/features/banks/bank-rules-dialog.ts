import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { BankBadge } from './bank-badge';
import { Bank, BankRules, BanksApi, DEPRECIATION_METHODS, RATE_BASES } from './banks.api';

/** All banks' valuation rules in one editable table; saves only rows that changed. */
@Component({
  selector: 'vp-bank-rules-dialog',
  imports: [Dialog, FormsModule, BankBadge],
  template: `
    <vp-dialog title="Bank-specific Valuation Rules" subtitle="Rate basis, distress and realizable value, and depreciation per bank" icon="fa-sliders" (closed)="closed.emit()">
      <div class="table-wrap">
        <table class="data">
          <thead><tr><th>Bank</th><th>Rate Basis</th><th>Distress %</th><th>Realizable %</th><th>Depreciation</th></tr></thead>
          <tbody>
            @for (row of rows(); track row.bank.id; let i = $index) {
              <tr [class.bad]="invalid(row.rules)">
                <td><div class="bank"><vp-bank-badge [code]="row.bank.shortCode" [color]="row.bank.brandColor" style="--size: 32px" /> {{ row.bank.shortCode }}</div></td>
                <td>
                  <select [ngModel]="row.rules.rateBasis" (ngModelChange)="set(i, 'rateBasis', $event)" [disabled]="!canEdit()" [attr.aria-label]="row.bank.shortCode + ' rate basis'">
                    @for (r of rateBases; track r.value) { <option [value]="r.value">{{ r.label }}</option> }
                  </select>
                </td>
                <td><input type="number" min="1" max="100" step="0.5" [ngModel]="row.rules.distressPercent" (ngModelChange)="set(i, 'distressPercent', +$event)" [disabled]="!canEdit()" [attr.aria-label]="row.bank.shortCode + ' distress percent'" /></td>
                <td><input type="number" min="1" max="100" step="0.5" [ngModel]="row.rules.realizablePercent" (ngModelChange)="set(i, 'realizablePercent', +$event)" [disabled]="!canEdit()" [attr.aria-label]="row.bank.shortCode + ' realizable percent'" /></td>
                <td>
                  <select [ngModel]="row.rules.depreciation" (ngModelChange)="set(i, 'depreciation', $event)" [disabled]="!canEdit()" [attr.aria-label]="row.bank.shortCode + ' depreciation'">
                    @for (d of depreciations; track d.value) { <option [value]="d.value">{{ d.label }}</option> }
                  </select>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (anyInvalid()) {
        <p class="alert-inline danger msg"><i class="fa-solid fa-circle-exclamation"></i> Percentages must be 1–100, and distress may not exceed realizable.</p>
      } @else if (error()) {
        <p class="alert-inline danger msg"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</p>
      }

      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Close</button>
        @if (canEdit()) {
          <button class="btn btn-pri" type="button" [disabled]="!changed().length || anyInvalid() || busy()" (click)="save()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-floppy-disk'"></i> Save Rules
          </button>
        }
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .bank { display: flex; align-items: center; gap: 10px; font-weight: 600; }
    /* Keep the bank visible while the table scrolls sideways on small screens. */
    th:first-child, td:first-child { position: sticky; left: 0; z-index: 1; background: #fff; box-shadow: 1px 0 0 var(--line-2); }
    th:first-child { background: #fafbfe; }
    select, input { height: 36px; padding: 0 10px; border: 1px solid var(--line); border-radius: 8px; font: inherit; font-size: 13px; background: #fff; }
    input { width: 80px; }
    tr.bad input { border-color: var(--danger); }
    .msg { margin: 12px 0 0; }
  `,
})
export class BankRulesDialog implements OnInit {
  private readonly api = inject(BanksApi);

  readonly banks = input.required<Bank[]>();
  readonly canEdit = input(false);
  readonly saved = output<Bank[]>();
  readonly closed = output<void>();

  protected readonly rateBases = RATE_BASES;
  protected readonly depreciations = DEPRECIATION_METHODS;
  protected readonly rows = signal<{ bank: Bank; rules: BankRules }[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly changed = computed(() =>
    this.rows().filter((r) => JSON.stringify(r.rules) !== JSON.stringify(r.bank.rules)),
  );
  protected readonly anyInvalid = computed(() => this.rows().some((r) => this.invalid(r.rules)));

  ngOnInit(): void {
    this.rows.set(this.banks().map((bank) => ({ bank, rules: { ...bank.rules } })));
  }

  protected invalid(r: BankRules): boolean {
    const ok = (n: number) => n >= 1 && n <= 100;
    return !ok(r.distressPercent) || !ok(r.realizablePercent) || r.distressPercent > r.realizablePercent;
  }

  protected set<K extends keyof BankRules>(i: number, key: K, value: BankRules[K]): void {
    this.rows.update((rows) => rows.map((r, j) => (j === i ? { ...r, rules: { ...r.rules, [key]: value } } : r)));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const banks = await firstValueFrom(this.api.updateRules(this.changed().map((r) => ({ bankId: r.bank.id, rules: r.rules }))));
      this.saved.emit(banks);
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
