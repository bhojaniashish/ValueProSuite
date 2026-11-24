import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { Choice } from '../../shared/chips';
import { Availability, AVAILABILITY, CaseDocument, DOC_TYPES, docMeta, DocumentsApi, DocumentType, SaveDocument, size } from './documents.api';

/** Add or edit a document: what it is, what was seen, its number and dates, and optionally its file. */
@Component({
  selector: 'vp-document-form-dialog',
  imports: [Dialog, FormsModule, Choice],
  template: `
    <vp-dialog [title]="doc() ? 'Edit document' : 'Add document'" [subtitle]="caseNumber()" [icon]="meta().icon" (closed)="closed.emit()">
      <form id="docForm" class="form-grid" (ngSubmit)="save()" novalidate>
        @if (error()) { <div class="alert-inline danger col-2"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }

        <div class="fld col-2">
          <label for="d-type">Document</label>
          <select id="d-type" name="type" [(ngModel)]="m.type" (ngModelChange)="typeChanged()">
            @for (t of types; track t.value) { <option [ngValue]="t.value">{{ t.label }}{{ t.hint ? ' · ' + t.hint : '' }}</option> }
          </select>
        </div>
        @if (m.type === 'Other' || m.title) {
          <div class="fld col-2" [class.invalid]="problem('title')">
            <label for="d-title">Name {{ m.type === 'Other' ? '' : '(optional)' }}</label>
            <input id="d-title" name="title" [(ngModel)]="m.title" placeholder="Society NOC for transfer" />
            @if (problem('title')) { <span class="err">{{ problem('title') }}</span> }
          </div>
        }

        <div class="fld col-2">
          <span class="lbl">What was seen</span>
          <vp-choice label="What was seen" [options]="availability" [value]="m.availability" (valueChange)="m.availability = $any($event) ?? 'CopyOnly'" />
        </div>

        @if (m.availability !== 'NotProvided') {
          <div class="fld">
            <label for="d-no">Document no.</label>
            <input id="d-no" name="number" [(ngModel)]="m.number" placeholder="SRT-4412/2019" />
          </div>
          <div class="fld" [class.invalid]="problem('issuedOn')">
            <label for="d-on">Dated</label>
            <input id="d-on" name="issuedOn" type="date" [max]="today" [(ngModel)]="m.issuedOn" />
            @if (problem('issuedOn')) { <span class="err">{{ problem('issuedOn') }}</span> }
          </div>
          <div class="fld">
            <label for="d-by">Issued / registered by</label>
            <input id="d-by" name="issuedBy" [(ngModel)]="m.issuedBy" placeholder="Sub-registrar, Surat-2" />
          </div>
          <div class="fld" [class.invalid]="problem('validUntil')">
            <label for="d-till">Valid until <span class="hint">(if it expires)</span></label>
            <input id="d-till" name="validUntil" type="date" [(ngModel)]="m.validUntil" />
            @if (problem('validUntil')) { <span class="err">{{ problem('validUntil') }}</span> }
          </div>
        }

        <div class="fld col-2" [class.invalid]="problem('notes')">
          <label for="d-notes">{{ m.availability === 'NotProvided' ? 'Why was it not provided?' : 'Notes' }}</label>
          <textarea id="d-notes" name="notes" rows="2" [(ngModel)]="m.notes"
            [placeholder]="m.availability === 'NotProvided' ? 'With the bank; applicant to submit later' : ''"></textarea>
          @if (problem('notes')) { <span class="err">{{ problem('notes') }}</span> }
        </div>

        <div class="fld col-2">
          <span class="lbl">{{ doc()?.files?.length ? 'Upload a newer version' : 'File' }} <span class="hint">(optional · PDF or photo, up to 20 MB)</span></span>
          <div class="file">
            <label class="btn btn-soft btn-sm pick">
              <i class="fa-solid fa-paperclip"></i> {{ file() ? 'Change file' : 'Choose file' }}
              <input type="file" accept="application/pdf,image/*" (change)="pick($event)" />
            </label>
            @if (file(); as f) { <span class="small">{{ f.name }} · {{ size(f.size) }}</span>
              <button class="btn-icon" type="button" aria-label="Remove chosen file" (click)="file.set(null)"><i class="fa-solid fa-xmark"></i></button> }
            @else if (doc()?.files?.length) { <span class="small muted">Current: v{{ doc()!.files[0].version }} · {{ doc()!.files[0].fileName }}</span> }
          </div>
          @if (fileError()) { <span class="err">{{ fileError() }}</span> }
        </div>
      </form>
      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="submit" form="docForm" [disabled]="busy()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> Save
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .file { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
    .pick { position: relative; overflow: hidden; cursor: pointer; }
    .pick input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
  `,
})
export class DocumentFormDialog implements OnInit {
  private readonly api = inject(DocumentsApi);

  readonly assignmentId = input.required<string>();
  readonly caseNumber = input('');
  readonly doc = input<CaseDocument | null>(null);
  /** Pre-chosen type, from a checklist line. */
  readonly presetType = input<DocumentType | null>(null);
  readonly saved = output<CaseDocument>();
  readonly closed = output<void>();

  protected readonly types = DOC_TYPES;
  protected readonly availability = AVAILABILITY.map((a) => ({ value: a.value, label: a.label }));
  protected readonly size = size;
  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fields = signal<Record<string, string>>({});
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  private touched = false;
  /** Set once a new document is saved, so a retry (e.g. after a failed upload) updates it instead of adding another. */
  private created: CaseDocument | null = null;

  protected m: SaveDocument = {
    type: 'SaleDeed', title: null, availability: 'OriginalSeen', number: null, issuedOn: null, issuedBy: null, validUntil: null, notes: null,
  };
  protected readonly meta = computed(() => docMeta(this.doc()?.type ?? this.presetType() ?? 'SaleDeed'));

  ngOnInit(): void {
    const d = this.doc();
    if (d) {
      this.m = { type: d.type, title: d.title, availability: d.availability, number: d.number, issuedOn: d.issuedOn, issuedBy: d.issuedBy, validUntil: d.validUntil, notes: d.notes };
    } else if (this.presetType()) {
      this.m.type = this.presetType()!;
    }
  }

  protected typeChanged(): void {
    if (this.m.type !== 'Other' && !this.doc()) this.m.title = null;
  }

  protected pick(e: Event): void {
    const el = e.target as HTMLInputElement;
    const f = el.files?.[0] ?? null;
    el.value = '';
    this.fileError.set(null);
    if (!f) return;
    if (!(f.type === 'application/pdf' || /^image\/(jpeg|png|webp)$/.test(f.type))) return this.fileError.set('Choose a PDF or a JPEG/PNG/WebP photo.');
    if (f.size > 20 * 1024 * 1024) return this.fileError.set('Files can be up to 20 MB.');
    this.file.set(f);
    if (this.m.availability === 'NotProvided') this.m.availability = 'CopyOnly';
  }

  /** Client checks that mirror the server's. */
  protected problem(field: string): string | null {
    const server = this.fields()[field];
    if (server) return server;
    if (!this.touched) return null;
    const m = this.m;
    if (field === 'title' && m.type === 'Other' && !m.title?.trim()) return 'Name the document.';
    if (field === 'notes' && m.availability === 'NotProvided' && !m.notes?.trim()) return 'Say why it was not provided.';
    if (field === 'issuedOn' && m.issuedOn && m.issuedOn > this.today) return "The date can't be in the future.";
    if (field === 'validUntil' && m.validUntil && m.issuedOn && m.validUntil < m.issuedOn) return 'Valid-until is before the document date.';
    return null;
  }

  protected async save(): Promise<void> {
    this.touched = true;
    this.fields.set({});
    if (['title', 'notes', 'issuedOn', 'validUntil'].some((f) => this.problem(f))) return;
    const body: SaveDocument = this.m.availability === 'NotProvided'
      ? { ...this.m, number: null, issuedOn: null, issuedBy: null, validUntil: null }
      : this.m;
    this.busy.set(true);
    this.error.set(null);
    try {
      const d = this.created ?? this.doc();
      let saved = d ? await firstValueFrom(this.api.update(d.id, body)) : await firstValueFrom(this.api.create(this.assignmentId(), body));
      this.created = saved;
      const f = this.file();
      if (f) {
        try {
          saved = await firstValueFrom(this.api.upload(saved.id, f));
          this.file.set(null);
        } catch (e) {
          // The details are saved; only the file failed. Keep the dialog open on the saved document so it can be retried.
          this.fileError.set(toProblem(e).message);
          this.saved.emit(saved);
          return;
        }
      }
      this.saved.emit(saved);
      this.closed.emit();
    } catch (e) {
      const p = toProblem(e);
      this.fields.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
