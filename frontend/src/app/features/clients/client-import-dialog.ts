import { Component, computed, inject, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { clientType, ClientsApi, ImportPreview, ImportResult } from './clients.api';

const MAX_BYTES = 5 * 1024 * 1024;

/** Three steps: choose a file → check every row → import the valid ones. */
@Component({
  selector: 'vp-client-import-dialog',
  imports: [Dialog],
  template: `
    <vp-dialog title="Import clients" subtitle="From a CSV or Excel (.xlsx) file, up to 1,000 rows" icon="fa-file-import" (closed)="closed.emit()">
      @if (result(); as r) {
        <div class="done">
          <span class="big"><i class="fa-solid fa-circle-check"></i></span>
          <h3>{{ r.imported }} client{{ r.imported === 1 ? '' : 's' }} imported</h3>
          @if (r.rejected.length) {
            <p class="muted">{{ r.rejected.length }} row(s) were not imported because they no longer passed the checks.</p>
          }
        </div>
      } @else if (preview(); as p) {
        <div class="summary">
          <span class="badge b-ok"><i class="fa-solid fa-circle"></i> {{ p.valid }} ready</span>
          @if (p.invalid) { <span class="badge b-danger"><i class="fa-solid fa-circle"></i> {{ p.invalid }} with problems</span> }
          <span class="small muted">{{ fileName() }}</span>
          @if (p.invalid) {
            <label class="only"><input type="checkbox" [checked]="onlyProblems()" (change)="onlyProblems.set(!onlyProblems())" /> Show only problems</label>
          }
        </div>
        <div class="rows">
          <table class="data">
            <thead><tr><th>Row</th><th>Client</th><th>Type</th><th>Check</th></tr></thead>
            <tbody>
              @for (r of visibleRows(); track r.row) {
                <tr [class.bad]="!r.isValid">
                  <td class="tnum muted">{{ r.row }}</td>
                  <td>
                    <b>{{ r.client?.name || '(no name)' }}</b>
                    <span class="small muted sub">{{ r.client?.mobile }}{{ r.client?.city ? ' · ' + r.client?.city : '' }}</span>
                  </td>
                  <td>@if (r.client) { <span class="badge" [class]="type(r.client.type).badge">{{ type(r.client.type).label }}</span> }</td>
                  <td>
                    @if (r.isValid) { <span class="ok"><i class="fa-solid fa-check"></i> Ready</span> }
                    @else { <ul class="errs">@for (e of r.errors; track e) { <li>{{ e }}</li> }</ul> }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (p.invalid) {
          <p class="small muted note">Rows with problems are skipped. Fix them in the file and import it again; rows already imported will be reported as duplicates if they have a PAN or GSTIN.</p>
        }
      } @else {
        <div class="pick" [class.drag]="dragging()" (dragover)="$event.preventDefault(); dragging.set(true)" (dragleave)="dragging.set(false)" (drop)="drop($event)">
          @if (busy()) {
            <i class="fa-solid fa-spinner fa-spin"></i><b>Checking {{ fileName() }}…</b>
          } @else {
            <i class="fa-solid fa-file-arrow-up"></i>
            <b>Drop a file here, or</b>
            <label class="btn btn-soft btn-sm choose">Choose file<input type="file" accept=".csv,.xlsx" (change)="choose($event)" /></label>
            <span class="small muted">Columns: Type, Name, Mobile, Email, GSTIN, PAN, Address, City, State, Pincode, Remarks. Only Name and Mobile are required.</span>
          }
        </div>
        @if (error()) { <div class="alert-inline danger err"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }
        <button class="link" type="button" (click)="downloadTemplate()"><i class="fa-solid fa-download"></i> Download the template (CSV, opens in Excel)</button>
      }

      <ng-container footer>
        @if (result()) {
          <button class="btn btn-pri" type="button" (click)="closed.emit()">Done</button>
        } @else if (preview(); as p) {
          <button class="btn btn-ghost" type="button" (click)="reset()" [disabled]="busy()">Choose another file</button>
          <button class="btn btn-pri" type="button" (click)="import()" [disabled]="!p.valid || busy()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-file-import'"></i> Import {{ p.valid }} client{{ p.valid === 1 ? '' : 's' }}
          </button>
        } @else {
          <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        }
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .pick { display: flex; flex-direction: column; align-items: center; gap: 10px; padding: 32px 20px; text-align: center;
      border: 2px dashed var(--line); border-radius: 12px; background: #fafbfe; }
    .pick.drag { border-color: var(--brand); background: var(--brand-soft); }
    .pick > i { font-size: 30px; color: var(--brand); }
    .choose { position: relative; overflow: hidden; cursor: pointer; }
    .choose input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
    .err { margin-top: 14px; }
    .link { display: inline-flex; align-items: center; gap: 6px; margin-top: 14px; color: var(--brand); font-weight: 600; font-size: 13px; }
    .summary { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 12px; }
    .only { display: flex; align-items: center; gap: 6px; margin-left: auto; font-size: 12.5px; cursor: pointer; }
    .rows { max-height: 360px; overflow: auto; border: 1px solid var(--line); border-radius: 10px; }
    .rows th { position: sticky; top: 0; z-index: 1; }
    tr.bad td { background: #fff8f8; }
    .sub { display: block; }
    .ok { color: var(--ok); font-weight: 600; font-size: 12.5px; }
    .errs { margin: 0; padding-left: 16px; color: var(--danger); font-size: 12.5px; }
    .note { margin: 10px 0 0; }
    .done { padding: 20px; text-align: center; }
    .done .big { font-size: 40px; color: var(--ok); }
    .done h3 { margin: 10px 0 4px; }
  `,
})
export class ClientImportDialog {
  private readonly api = inject(ClientsApi);
  private readonly toast = inject(ToastService);

  readonly imported = output<number>();
  readonly closed = output<void>();

  protected readonly type = clientType;
  protected readonly busy = signal(false);
  protected readonly dragging = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fileName = signal('');
  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly onlyProblems = signal(false);

  protected readonly visibleRows = computed(() => {
    const rows = this.preview()?.rows ?? [];
    return this.onlyProblems() ? rows.filter((r) => !r.isValid) : rows;
  });

  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) void this.check(file);
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) void this.check(file);
  }

  protected reset(): void {
    this.preview.set(null);
    this.error.set(null);
    this.onlyProblems.set(false);
  }

  private async check(file: File): Promise<void> {
    this.error.set(null);
    if (!/\.(csv|xlsx)$/i.test(file.name)) return this.error.set('Choose a .csv or .xlsx file. Older .xls files: open in Excel and save as .xlsx.');
    if (file.size > MAX_BYTES) return this.error.set('The file is larger than 5 MB. Split it into smaller files.');

    this.fileName.set(file.name);
    this.busy.set(true);
    try {
      const p = await firstValueFrom(this.api.previewImport(file));
      this.preview.set(p);
      this.onlyProblems.set(p.invalid > 0 && p.valid > 20);
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async import(): Promise<void> {
    const p = this.preview();
    if (!p) return;
    this.busy.set(true);
    try {
      const r = await firstValueFrom(this.api.import(p.rows.filter((x) => x.isValid).map((x) => x.client!)));
      this.result.set(r);
      this.imported.emit(r.imported);
    } catch (e) {
      this.toast.error('Import failed', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async downloadTemplate(): Promise<void> {
    try {
      const blob = await firstValueFrom(this.api.template());
      const url = URL.createObjectURL(blob);
      const a = Object.assign(document.createElement('a'), { href: url, download: 'valupro-clients-template.csv' });
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      this.toast.error('Could not download the template', toProblem(e).message);
    }
  }
}
