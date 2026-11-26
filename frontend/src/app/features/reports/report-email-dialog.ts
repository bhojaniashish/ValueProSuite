import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { ReportDetail, ReportEmailDraft, ReportsApi } from './reports.api';

const EMAIL = /^[^\s@,;]+@[^\s@,;]+\.[^\s@,;]+$/;

/** Sends the signed report to the bank: recipients pre-filled from the branch, standard wording the valuer can edit. */
@Component({
  selector: 'vp-report-email-dialog',
  imports: [Dialog, FormsModule],
  templateUrl: './report-email-dialog.html',
  styleUrl: './report-email-dialog.scss',
})
export class ReportEmailDialog implements OnInit {
  private readonly api = inject(ReportsApi);

  readonly assignmentId = input.required<string>();
  readonly number = input.required<string>();
  readonly sent = output<ReportDetail>();
  readonly closed = output<void>();

  protected readonly draft = signal<ReportEmailDraft | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string>>({});
  protected readonly busy = signal(false);

  protected readonly to = signal<string[]>([]);
  protected readonly cc = signal<string[]>([]);
  protected toText = '';
  protected ccText = '';
  protected subject = '';
  protected message = '';

  async ngOnInit(): Promise<void> {
    try {
      const d = await firstValueFrom(this.api.emailDraft(this.assignmentId()));
      this.draft.set(d);
      this.to.set(d.to);
      this.cc.set(d.cc);
      this.subject = d.subject;
      this.message = d.message;
    } catch (e) {
      this.loadError.set(toProblem(e).message);
    }
  }

  /** Turns typed text ("a@b.in, c@d.in") into chips; anything that isn't an address stays in the box. */
  protected take(list: 'to' | 'cc'): void {
    const text = list === 'to' ? this.toText : this.ccText;
    const parts = text.split(/[\s,;]+/).map((x) => x.trim()).filter(Boolean);
    const good = parts.filter((p) => EMAIL.test(p));
    const bad = parts.filter((p) => !EMAIL.test(p));
    const target = list === 'to' ? this.to : this.cc;
    target.update((cur) => [...cur, ...good.filter((g) => !cur.some((c) => c.toLowerCase() === g.toLowerCase()))]);
    if (list === 'to') this.toText = bad.join(', ');
    else this.ccText = bad.join(', ');
  }

  protected key(e: KeyboardEvent, list: 'to' | 'cc'): void {
    const text = list === 'to' ? this.toText : this.ccText;
    if (e.key === 'Enter' || e.key === ',' || e.key === ';') {
      e.preventDefault();
      this.take(list);
    } else if (e.key === 'Backspace' && !text) {
      (list === 'to' ? this.to : this.cc).update((cur) => cur.slice(0, -1));
    }
  }

  protected remove(list: 'to' | 'cc', idx: number): void {
    (list === 'to' ? this.to : this.cc).update((cur) => cur.filter((_, i) => i !== idx));
  }

  protected invalidText(text: string): boolean {
    return text.trim().length > 0 && text.split(/[\s,;]+/).some((p) => p && !EMAIL.test(p));
  }

  /** "Vesu" → "Vesu branch"; "Vesu Branch" stays as it is. */
  protected branch(name: string): string {
    return /branch/i.test(name) ? name : `${name} branch`;
  }

  protected size(bytes: number): string {
    return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  protected canSend(): boolean {
    return !this.busy() && this.to().length > 0 && !!this.subject.trim() && !!this.message.trim()
      && !this.invalidText(this.toText) && !this.invalidText(this.ccText);
  }

  protected async send(): Promise<void> {
    const d = this.draft();
    if (!d) return;
    this.take('to');
    this.take('cc');
    if (!this.canSend()) return;
    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    try {
      const r = await firstValueFrom(this.api.email(this.assignmentId(), {
        fileId: d.fileId, to: this.to(), cc: this.cc(), subject: this.subject.trim(), message: this.message,
      }));
      this.sent.emit(r);
    } catch (e) {
      const p = toProblem(e);
      this.fieldErrors.set(p.fields);
      this.error.set(Object.keys(p.fields).length ? null : p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
