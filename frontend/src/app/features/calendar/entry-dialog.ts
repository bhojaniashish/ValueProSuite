import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { Choice } from '../../shared/chips';
import { Typeahead, TypeaheadOption } from '../../shared/typeahead';
import { AssignmentsApi } from '../assignments/assignments.api';
import { CalendarApi, CalendarItem, ENTRY_KINDS, EntryKind, SaveEntry } from './calendar.api';

/** Add or change a follow-up, reminder or meeting; optionally for a case and for someone else. */
@Component({
  selector: 'vp-entry-dialog',
  imports: [FormsModule, Dialog, Choice, Typeahead],
  template: `
    <vp-dialog [title]="item() ? 'Edit entry' : 'Add to calendar'" [subtitle]="item()?.personName ? 'For ' + item()?.personName : 'A follow-up, reminder or meeting'"
      icon="fa-thumbtack" tone="b-warn" (closed)="closed.emit()">
      <div class="grid">
        <div class="fld col-2" [class.invalid]="!!errors()['title']">
          <label for="etitle">What <span class="req">*</span></label>
          <input id="etitle" maxlength="160" [(ngModel)]="f.title" placeholder="e.g. Chase HDFC Vesu for the sale deed copy" />
          @if (errors()['title']) { <span class="err">{{ errors()['title'] }}</span> }
        </div>
        <div class="fld col-2">
          <span class="lbl">Kind</span>
          <vp-choice label="Kind" [options]="kinds" [value]="f.kind" (valueChange)="f.kind = $any($event) ?? f.kind" />
        </div>
        <div class="fld">
          <label for="edate">Date <span class="req">*</span></label>
          <input id="edate" type="date" [(ngModel)]="f.date" />
        </div>
        <div class="fld">
          <label for="etime">Time</label>
          <div class="time">
            <input id="etime" type="time" step="900" [(ngModel)]="f.time" [disabled]="allDay" />
            <label class="inline"><span class="switch"><input type="checkbox" [(ngModel)]="allDay" (ngModelChange)="allDayChanged()" /><span class="track"></span></span> All day</label>
          </div>
        </div>
        <div class="fld" [class.invalid]="!!errors()['assigneeId']">
          <label for="ewho">For</label>
          <select id="ewho" [(ngModel)]="f.assigneeId">
            <option [ngValue]="null">Me</option>
            @for (p of others(); track p.id) { <option [ngValue]="p.id">{{ p.name }}</option> }
          </select>
          @if (errors()['assigneeId']) { <span class="err">{{ errors()['assigneeId'] }}</span> }
        </div>
        <div class="fld" [class.invalid]="!!errors()['assignmentId']">
          <label for="ecase">Case</label>
          <vp-typeahead inputId="ecase" placeholder="Case no., applicant or property" [search]="searchCases" hint="No matching case."
            [(text)]="caseText" [(pickedId)]="f.assignmentId" />
          @if (errors()['assignmentId']) { <span class="err">{{ errors()['assignmentId'] }}</span> }
          @else { <span class="hint">Optional. Links the entry to the case.</span> }
        </div>
        <div class="fld col-2">
          <label for="enotes">Notes</label>
          <textarea id="enotes" rows="3" maxlength="1000" [(ngModel)]="f.notes"></textarea>
        </div>
        @if (item()) {
          <label class="inline col-2"><span class="switch"><input type="checkbox" [(ngModel)]="f.done" /><span class="track"></span></span> Done</label>
        }
      </div>
      @if (error(); as e) { <p class="alert-inline">{{ e }}</p> }
      <ng-container footer>
        @if (item()) {
          <button class="btn btn-ghost danger" type="button" [disabled]="busy()" (click)="remove()"><i class="fa-solid fa-trash"></i> Delete</button>
        }
        <span class="sp"></span>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="button" [disabled]="busy() || !f.title.trim() || !f.date" (click)="save()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> Save
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 14px; }
    .col-2 { grid-column: 1 / -1; }
    .lbl { font-size: 13px; font-weight: 600; color: var(--ink-2); }
    .time { display: flex; align-items: center; gap: 10px; input[type='time'] { flex: 1; min-width: 0; } }
    .inline { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; font-weight: 600; color: var(--ink-2); white-space: nowrap; }
    .sp { flex: 1; }
    .danger { color: var(--danger); }
    .alert-inline { margin: 12px 0 0; }
    @media (max-width: 599px) { .grid { grid-template-columns: minmax(0, 1fr); } }
  `,
})
export class EntryDialog implements OnInit {
  private readonly api = inject(CalendarApi);
  private readonly cases = inject(AssignmentsApi);

  readonly item = input<CalendarItem | null>(null);
  readonly day = input.required<string>();
  readonly people = input<{ id: string; name: string }[]>([]);
  readonly meId = input<string | null>(null);
  readonly saved = output<CalendarItem>();
  readonly removed = output<string>();
  readonly closed = output<void>();

  protected readonly kinds = ENTRY_KINDS.map((k) => ({ value: k.value, label: k.label }));
  protected readonly others = computed(() => this.people().filter((p) => p.id !== this.meId()));

  protected f: SaveEntry = { title: '', kind: 'FollowUp', date: '', time: null, assignmentId: null, assigneeId: null, notes: null, done: false };
  protected allDay = true;
  protected caseText = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly errors = signal<Record<string, string>>({});

  protected readonly searchCases = async (q: string): Promise<TypeaheadOption[]> =>
    (await firstValueFrom(this.cases.list({ search: q, page: 1, pageSize: 8 }))).items.map((a) => ({
      id: a.id, label: a.number, badge: a.bankShortCode, sub: `${a.applicantName} · ${a.propertySummary}`,
    }));

  ngOnInit(): void {
    const i = this.item();
    if (i) {
      this.f = {
        title: i.title, kind: (i.entryKind ?? 'Other') as EntryKind, date: i.date, time: i.time?.slice(0, 5) ?? null,
        assignmentId: i.assignmentId, assigneeId: i.assigneeId, notes: i.notes, done: i.done,
      };
      this.caseText = i.assignmentNumber ?? '';
    } else {
      this.f.date = this.day();
    }
    this.allDay = !this.f.time;
  }

  protected allDayChanged(): void {
    this.f.time = this.allDay ? null : (this.f.time ?? '10:00');
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.errors.set({});
    const body: SaveEntry = { ...this.f, title: this.f.title.trim(), time: this.allDay ? null : this.f.time, notes: this.f.notes?.trim() || null };
    try {
      const i = this.item();
      this.saved.emit(await firstValueFrom(i?.entryId ? this.api.updateEntry(i.entryId, body) : this.api.createEntry(body)));
    } catch (e) {
      const p = toProblem(e);
      this.errors.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const id = this.item()?.entryId;
    if (!id) return;
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.deleteEntry(id));
      this.removed.emit(id);
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
