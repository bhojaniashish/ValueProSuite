import { Component, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { inr, RateImportPreview, RateImportRow, RateKind, RatesApi } from './rates.api';

const COLUMNS: Record<RateKind, string> = {
  Location: 'Locality, City, State, Zone, Min Rate, Max Rate, Effective From, Source',
  Circle: 'State, Scheme, City, Locality, Residential, Commercial, Open Land, Effective From, Reference',
  Construction: 'Construction Type, Unit, CPWD, PWD, Bank, Effective From, Remarks',
};

/** Choose a file → check every row → import the valid ones (the same file is sent again to import). */
@Component({
  selector: 'vp-rate-import-dialog',
  imports: [Dialog],
  template: `
    <vp-dialog [title]="'Import ' + label()" subtitle="From a CSV or Excel (.xlsx) file, up to 1,000 rows" icon="fa-file-import" (closed)="closed.emit()">
      @if (done(); as d) {
        <div class="done"><span class="big"><i class="fa-solid fa-circle-check"></i></span><h3>{{ d }} rate{{ d === 1 ? '' : 's' }} imported</h3></div>
      } @else if (preview(); as p) {
        <div class="summary">
          <span class="badge b-ok"><i class="fa-solid fa-circle"></i> {{ p.valid }} ready</span>
          @if (p.invalid) { <span class="badge b-danger"><i class="fa-solid fa-circle"></i> {{ p.invalid }} with problems</span> }
          <span class="small muted">{{ file()?.name }}</span>
        </div>
        <div class="rows">
          <table class="data">
            <thead><tr><th>Row</th><th>Rate</th><th>Check</th></tr></thead>
            <tbody>
              @for (r of p.rows; track r.row) {
                <tr [class.bad]="!r.isValid">
                  <td class="tnum muted">{{ r.row }}</td>
                  <td><b>{{ title(r) }}</b><span class="small muted sub">{{ detail(r) }}</span></td>
                  <td>
                    @if (r.isValid) { <span class="ok"><i class="fa-solid fa-check"></i> Ready</span> }
                    @else { <ul class="errs">@for (e of r.errors; track e) { <li>{{ e }}</li> }</ul> }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <div class="pick">
          @if (busy()) { <i class="fa-solid fa-spinner fa-spin"></i><b>Checking {{ file()?.name }}…</b> }
          @else {
            <i class="fa-solid fa-file-arrow-up"></i>
            <label class="btn btn-soft btn-sm choose">Choose file<input type="file" accept=".csv,.xlsx" (change)="choose($event)" /></label>
            <span class="small muted">Columns: {{ columns() }}. Dates like 01/04/2026; amounts may include ₹ and commas.</span>
          }
        </div>
        @if (error()) { <div class="alert-inline danger err"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }
        <button class="link" type="button" (click)="template()"><i class="fa-solid fa-download"></i> Download the template (CSV, opens in Excel)</button>
      }
      <ng-container footer>
        @if (done() !== null) {
          <button class="btn btn-pri" type="button" (click)="closed.emit()">Done</button>
        } @else if (preview(); as p) {
          <button class="btn btn-ghost" type="button" (click)="preview.set(null)" [disabled]="busy()">Choose another file</button>
          <button class="btn btn-pri" type="button" (click)="run()" [disabled]="!p.valid || busy()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-file-import'"></i> Import {{ p.valid }} rate{{ p.valid === 1 ? '' : 's' }}
          </button>
        } @else {
          <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        }
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .pick { display: flex; flex-direction: column; align-items: center; gap: 10px; padding: 28px 20px; text-align: center; border: 2px dashed var(--line); border-radius: 12px; background: #fafbfe; }
    .pick > i { font-size: 28px; color: var(--brand); }
    .choose { position: relative; overflow: hidden; cursor: pointer; }
    .choose input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
    .err { margin-top: 14px; }
    .link { display: inline-flex; align-items: center; gap: 6px; margin-top: 14px; color: var(--brand); font-weight: 600; font-size: 13px; }
    .summary { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 12px; }
    .rows { max-height: 360px; overflow: auto; border: 1px solid var(--line); border-radius: 10px; }
    .rows th { position: sticky; top: 0; z-index: 1; }
    tr.bad td { background: #fff8f8; }
    .sub { display: block; }
    .ok { color: var(--ok); font-weight: 600; font-size: 12.5px; }
    .errs { margin: 0; padding-left: 16px; color: var(--danger); font-size: 12.5px; }
    .done { padding: 20px; text-align: center; }
    .done .big { font-size: 40px; color: var(--ok); }
  `,
})
export class RateImportDialog {
  private readonly api = inject(RatesApi);
  private readonly toast = inject(ToastService);

  readonly kind = input.required<RateKind>();
  readonly imported = output<number>();
  readonly closed = output<void>();

  protected readonly label = computed(() => ({ Location: 'location rates', Circle: 'circle / Jantri rates', Construction: 'construction rates' })[this.kind()]);
  protected readonly columns = computed(() => COLUMNS[this.kind()]);
  protected readonly file = signal<File | null>(null);
  protected readonly preview = signal<RateImportPreview | null>(null);
  protected readonly done = signal<number | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected title(r: RateImportRow): string {
    const d = (r.data ?? {}) as Record<string, string>;
    return this.kind() === 'Construction' ? d['constructionType'] || '(no type)' : `${d['locality'] || '?'}, ${d['city'] || '?'}`;
  }

  protected detail(r: RateImportRow): string {
    const d = (r.data ?? {}) as Record<string, number | string | null>;
    const n = (k: string) => inr(d[k] as number | null);
    switch (this.kind()) {
      case 'Location': return `${d['zone']} · ${n('minRate')} – ${n('maxRate')} / sq.ft · from ${d['effectiveFrom']}`;
      case 'Circle': return `${d['scheme']} · Res ${n('residential')} · Com ${n('commercial')} / sq.m · from ${d['effectiveFrom']}`;
      default: return `CPWD ${n('cpwdRate')} · PWD ${n('pwdRate')} · Bank ${n('bankRate')} · from ${d['effectiveFrom']}`;
    }
  }

  protected async choose(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const f = input.files?.[0];
    input.value = '';
    if (!f) return;
    if (!/\.(csv|xlsx)$/i.test(f.name)) return this.error.set('Choose a .csv or .xlsx file.');
    this.file.set(f);
    this.error.set(null);
    this.busy.set(true);
    try {
      this.preview.set(await firstValueFrom(this.api.previewImport(this.kind(), f)));
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async run(): Promise<void> {
    const f = this.file();
    if (!f) return;
    this.busy.set(true);
    try {
      const r = await firstValueFrom(this.api.import(this.kind(), f));
      this.done.set(r.imported);
      this.imported.emit(r.imported);
    } catch (e) {
      this.toast.error('Import failed', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async template(): Promise<void> {
    const blob = await firstValueFrom(this.api.template(this.kind()));
    const url = URL.createObjectURL(blob);
    Object.assign(document.createElement('a'), { href: url, download: `valupro-${this.kind().toLowerCase()}-rates-template.csv` }).click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
