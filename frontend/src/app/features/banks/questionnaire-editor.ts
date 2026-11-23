import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';
import { ANSWER_TYPES, AnswerType, Bank, BanksApi, Question } from './banks.api';

/** The bank's dynamic questionnaire: questions the inspector answers for this bank's cases. */
@Component({
  selector: 'vp-questionnaire-editor',
  imports: [FormsModule],
  template: `
    <div class="card" id="questionnaire">
      <div class="card-head">
        <h3>Dynamic Questionnaire</h3>
        <span class="sub">Format: {{ bank().reportFormat || 'Standard Format' }} · {{ items().length }} question{{ items().length === 1 ? '' : 's' }}</span>
        @if (canEdit()) {
          <button class="btn btn-soft btn-sm ml" type="button" (click)="add()"><i class="fa-solid fa-plus"></i> Add question</button>
        }
      </div>

      @if (!items().length) {
        <div class="empty">
          <i class="fa-solid fa-clipboard-question"></i>
          <span>No questions yet. Add the bank-specific checks inspectors must answer, e.g. "Is the property SARFAESI compliant?".</span>
          @if (canEdit()) {
            <button class="btn btn-ghost btn-sm" type="button" (click)="addSamples()">Add common questions</button>
          }
        </div>
      } @else {
        <ol class="list">
          @for (q of items(); track $index; let i = $index; let first = $first; let last = $last) {
            <li [class.bad]="problem(q)">
              <span class="num">Q{{ i + 1 }}</span>
              <div class="body">
                <input class="text" [ngModel]="q.text" (ngModelChange)="patch(i, { text: $event })" [disabled]="!canEdit()"
                  placeholder="Question text" [attr.aria-label]="'Question ' + (i + 1)" maxlength="300" />
                <div class="row">
                  <select [ngModel]="q.answerType" (ngModelChange)="patch(i, { answerType: $event })" [disabled]="!canEdit()" [attr.aria-label]="'Answer type for question ' + (i + 1)">
                    @for (t of types; track t.value) { <option [value]="t.value">{{ t.label }}</option> }
                  </select>
                  <label class="req"><input type="checkbox" [ngModel]="q.required" (ngModelChange)="patch(i, { required: $event })" [disabled]="!canEdit()" /> Required</label>
                  @if (q.answerType === 'Choice') {
                    <input class="opts" [ngModel]="q.options.join(', ')" (ngModelChange)="patch(i, { options: split($event) })" [disabled]="!canEdit()"
                      placeholder="Options, comma separated: Good, Average, Poor" [attr.aria-label]="'Options for question ' + (i + 1)" />
                  }
                </div>
                @if (problem(q)) { <span class="err">{{ problem(q) }}</span> }
              </div>
              @if (canEdit()) {
                <div class="tools">
                  <button class="btn-icon" type="button" [disabled]="first" (click)="move(i, -1)" [attr.aria-label]="'Move question ' + (i + 1) + ' up'"><i class="fa-solid fa-arrow-up"></i></button>
                  <button class="btn-icon" type="button" [disabled]="last" (click)="move(i, 1)" [attr.aria-label]="'Move question ' + (i + 1) + ' down'"><i class="fa-solid fa-arrow-down"></i></button>
                  <button class="btn-icon danger" type="button" (click)="remove(i)" [attr.aria-label]="'Delete question ' + (i + 1)"><i class="fa-solid fa-trash-can"></i></button>
                </div>
              }
            </li>
          }
        </ol>
      }

      @if (canEdit()) {
        <div class="section-foot">
          @if (dirty()) { <span class="status">Unsaved changes</span> }
          <button class="btn btn-ghost" type="button" [disabled]="!dirty() || busy()" (click)="items.set(clone(value()))">Discard</button>
          <button class="btn btn-pri" type="button" [disabled]="!dirty() || invalid() || busy()" (click)="save()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-floppy-disk'"></i> Save Format
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    .ml { margin-left: auto; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px 20px; text-align: center; color: var(--muted); }
    .empty i { font-size: 24px; color: var(--muted-2); }
    .list { margin: 0; padding: 8px 20px; list-style: none; }
    li { display: flex; gap: 12px; align-items: flex-start; padding: 12px 0; border-bottom: 1px solid var(--line-2); }
    li:last-child { border-bottom: none; }
    .num { display: grid; place-items: center; flex: none; width: 34px; height: 34px; border-radius: 9px; background: var(--brand-soft);
      color: var(--brand); font-weight: 700; font-size: 12.5px; }
    .body { display: flex; flex: 1; flex-direction: column; gap: 8px; min-width: 0; }
    input, select { height: 36px; padding: 0 10px; border: 1px solid var(--line); border-radius: 8px; font: inherit; font-size: 13.5px; background: #fff; }
    input:focus, select:focus { outline: none; border-color: var(--brand); box-shadow: 0 0 0 3px var(--brand-soft); }
    .text { width: 100%; font-weight: 500; }
    li.bad .text { border-color: var(--danger); }
    .row { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
    .req { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--ink-2); cursor: pointer; }
    .req input { width: 16px; height: 16px; }
    .opts { flex: 1 1 220px; }
    .err { font-size: 12px; color: var(--danger); }
    .tools { display: flex; flex: none; gap: 2px; }
    .btn-icon.danger:hover:not(:disabled) { background: var(--danger-soft); color: var(--danger); }
    @media (max-width: 599px) { li { flex-wrap: wrap; } .tools { margin-left: 46px; } }
  `,
})
export class QuestionnaireEditor {
  private readonly api = inject(BanksApi);
  private readonly toast = inject(ToastService);

  readonly bank = input.required<Bank>();
  readonly value = input.required<Question[]>();
  readonly canEdit = input(false);
  readonly saved = output<Question[]>();

  protected readonly types = ANSWER_TYPES;
  protected readonly items = signal<Question[]>([]);
  protected readonly busy = signal(false);
  protected readonly dirty = computed(() => JSON.stringify(this.items()) !== JSON.stringify(this.value()));
  protected readonly invalid = computed(() => this.items().some((q) => this.problem(q)));

  constructor() {
    effect(() => this.items.set(this.clone(this.value())));
  }

  protected clone(q: Question[]): Question[] {
    return q.map((x) => ({ ...x, options: [...x.options] }));
  }

  protected problem(q: Question): string | null {
    if (!q.text.trim()) return 'Enter the question text.';
    if (q.answerType === 'Choice' && q.options.filter((o) => o.trim()).length < 2) return 'Add at least two options.';
    return null;
  }

  protected split(value: string): string[] {
    return value.split(',').map((s) => s.trimStart());
  }

  protected patch(i: number, change: Partial<Question>): void {
    this.items.update((list) => list.map((q, j) => (j === i ? { ...q, ...change } : q)));
  }

  protected add(): void {
    this.items.update((list) => [...list, { text: '', answerType: 'YesNo' as AnswerType, options: [], required: false }]);
    setTimeout(() => (document.querySelectorAll<HTMLInputElement>('#questionnaire .text').item(this.items().length - 1))?.focus());
  }

  protected addSamples(): void {
    const yes = (text: string, required = true): Question => ({ text, answerType: 'YesNo', options: [], required });
    this.items.set([
      yes('Whether the property is clearly demarcated?'),
      yes('Is the property SARFAESI compliant?'),
      { text: 'Approach road width (in feet)?', answerType: 'Number', options: [], required: true },
      yes('Is construction as per the approved plan?'),
      { text: 'Marketability of the property?', answerType: 'Choice', options: ['Good', 'Average', 'Poor'], required: true },
      yes('Any encroachment observed?'),
      { text: 'Distance from main road (km)?', answerType: 'Number', options: [], required: false },
      { text: 'Occupied by?', answerType: 'Choice', options: ['Owner', 'Tenant', 'Vacant'], required: true },
    ]);
  }

  protected move(i: number, by: number): void {
    this.items.update((list) => {
      const next = [...list];
      [next[i], next[i + by]] = [next[i + by], next[i]];
      return next;
    });
  }

  protected remove(i: number): void {
    this.items.update((list) => list.filter((_, j) => j !== i));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      const body = this.items().map((q) => ({ ...q, text: q.text.trim(), options: q.options.map((o) => o.trim()).filter(Boolean) }));
      this.saved.emit(await firstValueFrom(this.api.updateQuestionnaire(this.bank().id, body)));
      this.toast.ok('Questionnaire saved', `${body.length} question${body.length === 1 ? '' : 's'} for ${this.bank().shortCode}.`);
    } catch (e) {
      this.toast.error('Could not save questionnaire', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
