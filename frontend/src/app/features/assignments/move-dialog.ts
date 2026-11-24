import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { AssignmentDetail, AssignmentsApi, AssignmentStatus, statusMeta } from './assignments.api';

export type MoveKind = 'forward' | 'back' | 'cancel' | 'reopen';

/** "2026-06-14T11:00" (shown in India time) from an ISO timestamp. */
function toLocalInput(iso: string): string {
  return new Date(new Date(iso).getTime() + 5.5 * 3600_000).toISOString().slice(0, 16);
}

/** Confirms a workflow move, asking for a reason (back, cancel) or the visit date (scheduling). */
@Component({
  selector: 'vp-move-dialog',
  imports: [Dialog, FormsModule],
  template: `
    <vp-dialog [title]="title()" [subtitle]="number()" [icon]="icon()" [tone]="tone()" size="sm" (closed)="closed.emit()">
      <div class="body">
        <p class="lead">{{ lead() }}</p>
        @if (needsVisit()) {
          <div class="fld">
            <label for="visit">Site visit date &amp; time <span class="req">*</span></label>
            <input id="visit" type="datetime-local" [(ngModel)]="visit" [min]="minVisit" />
            <span class="hint">India time. Shown on the calendar (Phase 4).</span>
          </div>
        }
        @if (kind() !== 'reopen') {
          <div class="fld">
            <label for="note">{{ noteRequired() ? 'Reason' : 'Note' }} @if (noteRequired()) { <span class="req">*</span> }</label>
            <textarea id="note" rows="3" [(ngModel)]="note" maxlength="500"
              [placeholder]="noteRequired() ? (kind() === 'cancel' ? 'e.g. Loan application withdrawn' : 'e.g. Carpet area does not match the plan') : 'Optional, shown on the timeline'"></textarea>
          </div>
        }
        @if (error()) { <div class="alert-inline danger"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }
      </div>
      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn" [class]="kind() === 'cancel' ? 'btn-danger' : 'btn-pri'" type="button" [disabled]="busy() || !valid()" (click)="confirm()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : icon()"></i> {{ action() }}
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .body { display: flex; flex-direction: column; gap: 14px; }
    .lead { margin: 0; line-height: 1.55; }
    textarea { height: auto !important; padding: 10px 13px !important; resize: vertical; }
  `,
})
export class MoveDialog implements OnInit {
  private readonly api = inject(AssignmentsApi);

  readonly detail = input.required<AssignmentDetail>();
  readonly kind = input.required<MoveKind>();
  readonly to = input.required<AssignmentStatus>();
  readonly moved = output<AssignmentDetail>();
  readonly closed = output<void>();

  protected note = '';
  protected visit = '';
  protected readonly minVisit = toLocalInput(new Date().toISOString()).slice(0, 10) + 'T00:00';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly number = computed(() => this.detail().assignment.number);
  protected readonly label = computed(() => statusMeta(this.to()).label);
  protected readonly needsVisit = computed(() => this.to() === 'SiteVisitScheduled' && this.kind() === 'forward');
  protected readonly noteRequired = computed(() => this.kind() === 'back' || this.kind() === 'cancel');
  protected readonly title = computed(() => ({ forward: `Move to ${this.label()}`, back: `Send back to ${this.label()}`, cancel: 'Cancel assignment', reopen: 'Reopen assignment' })[this.kind()]);
  protected readonly action = computed(() => ({ forward: this.needsVisit() ? 'Schedule visit' : 'Move', back: 'Send back', cancel: 'Cancel assignment', reopen: 'Reopen' })[this.kind()]);
  protected readonly icon = computed(() => ({ forward: statusMeta(this.to()).icon, back: 'fa-rotate-left', cancel: 'fa-ban', reopen: 'fa-rotate-right' })[this.kind()]);
  protected readonly tone = computed(() => ({ forward: 'b-brand', back: 'b-warn', cancel: 'b-danger', reopen: 'b-ok' })[this.kind()]);
  protected readonly lead = computed(() => ({
    forward: this.needsVisit() ? 'When will the site be inspected?' : `Mark ${this.number()} as "${this.label()}".`,
    back: `${this.number()} goes back to "${this.label()}". Say what needs fixing; the valuer sees it on the timeline.`,
    cancel: `${this.number()} will be cancelled. It stays on record and can be reopened later.`,
    reopen: `${this.number()} goes back to "New Assignment" so work can restart.`,
  })[this.kind()]);

  ngOnInit(): void {
    const existing = this.detail().siteVisitAt;
    const tomorrow11 = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10) + 'T11:00';
    this.visit = existing ? toLocalInput(existing) : tomorrow11;
  }

  protected valid(): boolean {
    if (this.noteRequired() && !this.note.trim()) return false;
    if (this.needsVisit() && !this.visit) return false;
    return true;
  }

  protected async confirm(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const visit = this.needsVisit() && this.visit ? `${this.visit}:00+05:30` : null;
      this.moved.emit(await firstValueFrom(this.api.move(this.detail().assignment.id, this.to(), this.note.trim() || null, visit)));
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
