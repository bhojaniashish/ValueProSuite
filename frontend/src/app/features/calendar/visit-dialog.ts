import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { CalendarApi, CalendarItem } from './calendar.api';

export interface VisitTarget {
  assignmentId: string;
  number: string;
  property: string;
  /** Already booked: moving it. */
  date: string | null;
  time: string | null;
}

/** Book a new case's site visit (moves it to "Site Visit Scheduled") or move a booked one. */
@Component({
  selector: 'vp-visit-dialog',
  imports: [FormsModule, Dialog],
  template: `
    <vp-dialog [title]="target().date ? 'Move site visit' : 'Book site visit'" [subtitle]="target().number + ' · ' + target().property"
      icon="fa-location-dot" tone="b-info" size="sm" (closed)="closed.emit()">
      <div class="grid">
        <div class="fld" [class.invalid]="!!errors()['date']">
          <label for="vdate">Date <span class="req">*</span></label>
          <input id="vdate" type="date" [min]="today()" [(ngModel)]="date" />
          @if (errors()['date']) { <span class="err">{{ errors()['date'] }}</span> }
        </div>
        <div class="fld">
          <label for="vtime">Time <span class="req">*</span></label>
          <input id="vtime" type="time" step="900" [(ngModel)]="time" />
        </div>
      </div>
      @if (!target().date) {
        <p class="hint">The case moves to "Site Visit Scheduled" and the booking goes on its timeline.</p>
      }
      @if (error(); as e) { <p class="alert-inline">{{ e }}</p> }
      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="button" [disabled]="busy() || !date || !time" (click)="save()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> {{ target().date ? 'Move visit' : 'Book visit' }}
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 14px; }
    .hint { margin: 12px 0 0; font-size: 12.5px; color: var(--muted); }
    .alert-inline { margin: 12px 0 0; }
  `,
})
export class VisitDialog implements OnInit {
  private readonly api = inject(CalendarApi);

  readonly target = input.required<VisitTarget>();
  readonly today = input.required<string>();
  /** Day picked on the calendar, for a new booking. */
  readonly day = input<string | null>(null);
  readonly saved = output<CalendarItem>();
  readonly closed = output<void>();

  protected date = '';
  protected time = '10:00';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly errors = signal<Record<string, string>>({});

  ngOnInit(): void {
    const t = this.target();
    const day = this.day();
    this.date = t.date ?? (day && day >= this.today() ? day : this.today());
    this.time = t.time?.slice(0, 5) ?? '10:00';
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.errors.set({});
    try {
      this.saved.emit(await firstValueFrom(this.api.scheduleVisit(this.target().assignmentId, this.date, this.time)));
    } catch (e) {
      const p = toProblem(e);
      this.errors.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
