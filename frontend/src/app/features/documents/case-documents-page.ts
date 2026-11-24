import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { saveBlob } from '../photos/photos.api';
import { DocumentFormDialog } from './document-form-dialog';
import {
  availabilityMeta, CaseDocument, CaseDocuments, day, docMeta, docName, DocumentFile, DocumentsApi, DocumentType, EXPIRY_BADGE, size,
} from './documents.api';
import { FileOpener } from './file-opener';

/** One case's documents: what the bank needs (checklist), what was seen, the files, and earlier documents to reuse. */
@Component({
  selector: 'vp-case-documents-page',
  imports: [RouterLink, Dialog, DocumentFormDialog],
  template: `
    @if (error()) {
      <div class="card empty"><i class="fa-solid fa-triangle-exclamation"></i><b>Could not load the documents</b><span>{{ error() }}</span>
        <a class="btn btn-soft btn-sm" routerLink="/documents">Back to documents</a></div>
    } @else if (c(); as c) {
      <div class="head card">
        <a class="back" routerLink="/documents" aria-label="Back to documents"><i class="fa-solid fa-arrow-left"></i></a>
        <div class="who">
          <div class="l1">
            <span class="bank" [style.background]="c.bankColor">{{ c.bankShortCode }}</span>
            <a class="num" [routerLink]="['/assignments', c.assignmentId]">{{ c.number }}</a>
          </div>
          <b>{{ c.applicantName }}</b>
          <span class="small muted">{{ c.propertySummary }}</span>
        </div>
        <div class="acts">
          @if (c.canEdit) { <button class="btn btn-pri" type="button" (click)="add(null)"><i class="fa-solid fa-plus"></i> Add document</button> }
          <button class="btn btn-ghost" type="button" (click)="zip()" [disabled]="!withFiles() || downloading()">
            <i class="fa-solid" [class]="downloading() ? 'fa-spinner fa-spin' : 'fa-file-zipper'"></i> Download files
          </button>
        </div>
      </div>

      @if (!c.canEdit && c.readOnlyReason) { <div class="alert-inline info"><i class="fa-solid fa-lock"></i> {{ c.readOnlyReason }}</div> }

      <!-- Checklist -->
      <div class="card sec">
        <div class="sec-head">
          <div>
            <h3>Checklist <span class="badge" [class]="checkDone() === c.checklist.length ? 'b-ok' : 'b-warn'">{{ checkDone() }} / {{ c.checklist.length }}</span></h3>
            <span class="small muted">The usual documents for this type of property. Record each one, or mark it as not provided.</span>
          </div>
        </div>
        <div class="checks">
          @for (k of c.checklist; track k.type) {
            <div class="check" [class.done]="k.recorded" [class.no]="k.notProvided">
              <i class="fa-solid st" [class]="k.recorded ? 'fa-circle-check' : k.notProvided ? 'fa-circle-xmark' : 'fa-circle-dot'"></i>
              <span class="nm"><i class="fa-solid" [class]="meta(k.type).icon"></i> {{ k.label }}</span>
              <span class="small muted state">{{ k.recorded ? 'Recorded' : k.notProvided ? 'Not provided' : 'Pending' }}</span>
              @if (c.canEdit && !k.recorded && !k.notProvided) {
                <button class="btn btn-soft btn-sm" type="button" (click)="add(k.type)">Record</button>
              }
            </div>
          }
        </div>
      </div>

      <!-- Documents -->
      <div class="card sec">
        <div class="sec-head"><div><h3>Documents perused <span class="badge b-brand">{{ c.documents.length }}</span></h3>
          <span class="small muted">These go into the report's "documents perused" table.</span></div></div>
        @if (c.documents.length) {
          <div class="docs">
            @for (d of c.documents; track d.id) {
              <div class="doc">
                <span class="ic"><i class="fa-solid" [class]="meta(d.type).icon"></i></span>
                <div class="main">
                  <div class="t1">
                    <b>{{ name(d) }}</b>
                    <span class="badge" [class]="avail(d.availability).badge">{{ avail(d.availability).label }}</span>
                    @if (d.expiryState && d.expiryState !== 'Valid') { <span class="badge" [class]="expiryBadge[d.expiryState]">{{ d.expiryState }} {{ day(d.validUntil) }}</span> }
                    @if (d.copiedFromCase) { <span class="badge b-gray" title="Reused from an earlier case on this property"><i class="fa-solid fa-clone"></i> From {{ d.copiedFromCase }}</span> }
                  </div>
                  <span class="small muted">
                    @if (d.availability === 'NotProvided') { {{ d.notes }} }
                    @else { {{ facts(d) }} }
                  </span>
                  @if (d.files.length; as n) {
                    <span class="file small">
                      <button class="link" type="button" (click)="open(d.files[0])"><i class="fa-solid" [class]="fileIcon(d.files[0])"></i> {{ d.files[0].fileName }}</button>
                      <span class="muted">· {{ size(d.files[0].sizeBytes) }}</span>
                      @if (n > 1) { <button class="link muted" type="button" (click)="versions.set(d)">· v{{ d.files[0].version }}, {{ n - 1 }} earlier</button> }
                    </span>
                  }
                </div>
                @if (c.canEdit) {
                  <div class="tools">
                    <button class="btn-icon" type="button" title="Edit" [attr.aria-label]="'Edit ' + name(d)" (click)="edit(d)"><i class="fa-solid fa-pen"></i></button>
                    <button class="btn-icon danger" type="button" title="Remove" [attr.aria-label]="'Remove ' + name(d)" (click)="removing.set(d)"><i class="fa-solid fa-trash-can"></i></button>
                  </div>
                }
              </div>
            }
          </div>
        } @else {
          <div class="empty small-empty"><i class="fa-solid fa-folder-open"></i><span>No documents recorded yet.</span></div>
        }
      </div>

      @if (c.earlier.length && c.canEdit) {
        <div class="card sec">
          <div class="sec-head"><div><h3>From earlier cases on this property</h3>
            <span class="small muted">Recorded before for the same property. Add one to this case without entering it again.</span></div></div>
          <div class="docs">
            @for (e of c.earlier; track e.document.id) {
              <div class="doc">
                <span class="ic muted-ic"><i class="fa-solid" [class]="meta(e.document.type).icon"></i></span>
                <div class="main">
                  <div class="t1"><b>{{ name(e.document) }}</b><span class="badge b-gray">{{ e.caseNumber }}</span></div>
                  <span class="small muted">{{ facts(e.document) }}{{ e.document.files.length ? ' · file attached' : '' }}</span>
                </div>
                <div class="tools"><button class="btn btn-soft btn-sm" type="button" [disabled]="busy()" (click)="reuse(e.document)"><i class="fa-solid fa-plus"></i> Use for this case</button></div>
              </div>
            }
          </div>
        </div>
      }
    } @else {
      <div class="card empty"><i class="fa-solid fa-spinner fa-spin"></i><span>Loading documents…</span></div>
    }

    @if (editing(); as e) {
      <vp-document-form-dialog [assignmentId]="id()" [caseNumber]="c()?.number ?? ''" [doc]="e.doc" [presetType]="e.type"
        (saved)="onSaved()" (closed)="editing.set(null)" />
    }

    @if (versions(); as d) {
      <vp-dialog [title]="name(d) + ': versions'" icon="fa-clock-rotate-left" size="sm" (closed)="versions.set(null)">
        <ul class="vers">
          @for (f of d.files; track f.id) {
            <li>
              <span class="badge" [class]="$first ? 'b-brand' : 'b-gray'">v{{ f.version }}</span>
              <button class="link" type="button" (click)="open(f)">{{ f.fileName }}</button>
              <span class="small muted">{{ size(f.sizeBytes) }} · {{ when(f.uploadedAt) }} · {{ f.uploadedByName }}</span>
            </li>
          }
        </ul>
        <ng-container footer><button class="btn btn-pri" type="button" (click)="versions.set(null)">Close</button></ng-container>
      </vp-dialog>
    }

    @if (removing(); as d) {
      <vp-dialog title="Remove this document?" [subtitle]="name(d)" icon="fa-trash-can" tone="b-danger" size="sm" (closed)="removing.set(null)">
        <p class="confirm">It will no longer be listed for this case. Its files are kept in the case history.</p>
        <ng-container footer>
          <button class="btn btn-ghost" type="button" (click)="removing.set(null)">Cancel</button>
          <button class="btn btn-danger" type="button" [disabled]="busy()" (click)="remove(d)"><i class="fa-solid fa-trash-can"></i> Remove</button>
        </ng-container>
      </vp-dialog>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 14px; }
    .head { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 14px; padding: 14px 18px; }
    .back { display: grid; place-items: center; width: 36px; height: 36px; border-radius: 10px; color: var(--muted); }
    .back:hover { background: var(--bg-2); color: var(--ink); }
    .who { display: flex; flex: 1 1 220px; flex-direction: column; gap: 2px; min-width: 0; }
    .who b { font-size: 16px; }
    .l1 { display: flex; align-items: center; gap: 8px; }
    .bank { padding: 2px 7px; border-radius: 6px; color: #fff; font-size: 11px; font-weight: 700; }
    .num { font-weight: 600; font-size: 13px; color: var(--brand); }
    .acts { display: flex; flex-wrap: wrap; gap: 8px; }
    .sec { padding: 0; }
    .sec-head { padding: 14px 18px; border-bottom: 1px solid var(--line-2); }
    .sec-head h3 { display: flex; align-items: center; gap: 8px; margin: 0 0 2px; font-size: 15px; }
    .checks { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(300px, 100%), 1fr)); gap: 8px; padding: 14px 18px; }
    .check { display: flex; align-items: center; gap: 10px; min-height: 48px; padding: 6px 12px; border: 1px solid var(--line); border-radius: 10px; }
    .check .st { color: var(--warn); }
    .check.done .st { color: var(--ok); }
    .check.no .st { color: var(--danger); }
    .check .nm { flex: 1; min-width: 0; font-size: 13.5px; font-weight: 600; }
    .check .nm i { width: 16px; color: var(--muted); }
    .check .state { white-space: nowrap; }
    .docs { display: flex; flex-direction: column; }
    .doc { display: flex; align-items: flex-start; gap: 12px; padding: 14px 18px; border-bottom: 1px solid var(--line-2); }
    .doc:last-child { border-bottom: none; }
    .ic { display: grid; place-items: center; flex: none; width: 40px; height: 40px; border-radius: 10px; background: var(--brand-soft); color: var(--brand); }
    .muted-ic { background: var(--bg-2); color: var(--muted); }
    .main { display: flex; flex: 1; flex-direction: column; gap: 3px; min-width: 0; }
    .t1 { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 8px; }
    .file { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; }
    .link { padding: 0; color: var(--brand); font: inherit; font-weight: 600; text-align: left; }
    .link.muted { color: var(--muted); font-weight: 400; }
    .tools { display: flex; flex: none; gap: 2px; }
    .btn-icon.danger:hover:not(:disabled) { background: var(--danger-soft); color: var(--danger); }
    .vers { margin: 0; padding: 0; list-style: none; display: flex; flex-direction: column; gap: 10px; }
    .vers li { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 10px; }
    .confirm { margin: 0; line-height: 1.55; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 48px 16px; text-align: center; color: var(--muted); }
    .empty i { font-size: 26px; color: var(--muted-2); }
    .empty b { color: var(--ink); }
    .small-empty { padding: 28px 16px; }
    @media (max-width: 599px) {
      .head { padding: 12px 14px; }
      .acts { width: 100%; }
      .acts .btn { flex: 1; justify-content: center; }
      .doc { padding: 12px 14px; flex-wrap: wrap; }
      .tools { margin-left: 52px; }
      .checks { padding: 12px 14px; }
    }
  `,
})
export class CaseDocumentsPage implements OnInit {
  private readonly api = inject(DocumentsApi);
  private readonly toast = inject(ToastService);
  private readonly opener = inject(FileOpener);

  readonly id = input.required<string>();

  protected readonly meta = docMeta;
  protected readonly avail = availabilityMeta;
  protected readonly expiryBadge = EXPIRY_BADGE;
  protected readonly name = docName;
  protected readonly day = day;
  protected readonly size = size;
  protected readonly c = signal<CaseDocuments | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly downloading = signal(false);
  protected readonly editing = signal<{ doc: CaseDocument | null; type: DocumentType | null } | null>(null);
  protected readonly versions = signal<CaseDocument | null>(null);
  protected readonly removing = signal<CaseDocument | null>(null);

  protected readonly checkDone = computed(() => (this.c()?.checklist ?? []).filter((k) => k.recorded || k.notProvided).length);
  protected readonly withFiles = computed(() => (this.c()?.documents ?? []).some((d) => d.files.length));

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  private async load(): Promise<void> {
    try {
      this.c.set(await firstValueFrom(this.api.case(this.id())));
      this.error.set(null);
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  protected add(type: DocumentType | null): void {
    this.editing.set({ doc: null, type });
  }

  protected edit(d: CaseDocument): void {
    this.editing.set({ doc: d, type: null });
  }

  protected onSaved(): void {
    this.toast.ok('Document saved');
    void this.load();
  }

  protected async reuse(d: CaseDocument): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.reuse(this.id(), d.id));
      this.toast.ok('Added to this case', docName(d));
      await this.load();
    } catch (e) {
      this.toast.error('Could not add the document', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(d: CaseDocument): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(d.id));
      this.removing.set(null);
      this.toast.ok('Removed', docName(d));
      await this.load();
    } catch (e) {
      this.toast.error('Could not remove', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected open(f: DocumentFile): void {
    this.opener.open(f);
  }

  protected async zip(): Promise<void> {
    const c = this.c();
    if (!c) return;
    this.downloading.set(true);
    try {
      saveBlob(await firstValueFrom(this.api.zip(c.assignmentId)), `${c.number.replace(/\//g, '-')} documents.zip`);
    } catch (e) {
      this.toast.error('Could not download', toProblem(e).message);
    } finally {
      this.downloading.set(false);
    }
  }

  protected facts(d: CaseDocument): string {
    return [d.number ? `No. ${d.number}` : null, d.issuedOn ? day(d.issuedOn) : null, d.issuedBy, d.availability !== 'NotProvided' ? d.notes : null]
      .filter(Boolean).join(' · ') || 'No details recorded';
  }

  protected fileIcon(f: DocumentFile): string {
    return f.contentType === 'application/pdf' ? 'fa-file-pdf' : 'fa-file-image';
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-IN', { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' });
  }
}
