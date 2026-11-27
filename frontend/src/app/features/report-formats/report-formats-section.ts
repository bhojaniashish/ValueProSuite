import { Component, inject, input, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { blankFormat, ReportFormatItem, ReportFormatsApi } from './report-formats.api';

/** Settings → Templates: the firm's report formats, which banks use each, and the default. */
@Component({
  selector: 'vp-report-formats-section',
  imports: [RouterLink, Dialog],
  template: `
    <div class="card">
      <div class="card-head">
        <div>
          <h3>Report formats</h3>
          <span class="sub">Each bank's report: headings, sections and their order, values shown, declaration. Banks pick one on the bank form; banks without one use the default.</span>
        </div>
        @if (canEdit()) {
          <button class="btn btn-soft btn-sm ml" type="button" [disabled]="busy()" (click)="create()"><i class="fa-solid fa-plus"></i> New format</button>
        }
      </div>
      <div class="card-pad list">
        @if (items(); as list) {
          @for (f of list; track f.id) {
            <div class="row">
              <span class="ic"><i class="fa-solid fa-file-lines"></i></span>
              <a class="main" [routerLink]="['/settings/report-formats', f.id]">
                <b>{{ f.name }}</b>
                <span class="small muted">{{ f.description || 'No description' }}</span>
                <span class="small muted">
                  @if (f.banks.length) { Used by {{ f.banks.join(', ') }} } @else if (f.isDefault) { Banks without their own format } @else { Not used by any bank yet }
                  · edited {{ when(f.updatedAt) }}
                </span>
              </a>
              @if (f.isDefault) { <span class="badge b-ok">Default</span> }
              @if (canEdit()) {
                <div class="acts">
                  @if (!f.isDefault) {
                    <button class="btn-icon" type="button" [attr.aria-label]="'Make ' + f.name + ' the default'" title="Make default" (click)="makeDefault(f)"><i class="fa-solid fa-star"></i></button>
                  }
                  <button class="btn-icon" type="button" [attr.aria-label]="'Copy ' + f.name" title="Copy" (click)="duplicate(f)"><i class="fa-solid fa-copy"></i></button>
                  <button class="btn-icon danger" type="button" [attr.aria-label]="'Delete ' + f.name" title="Delete" (click)="confirmDelete.set(f)"><i class="fa-solid fa-trash"></i></button>
                </div>
              }
            </div>
          } @empty {
            <div class="empty">
              <i class="fa-solid fa-file-lines"></i>
              <b>No formats yet</b>
              <span class="small muted">Reports use the standard layout. Add a format to change headings, sections or wording for a bank.</span>
            </div>
          }
        } @else if (error()) {
          <div class="alert-inline danger"><i class="fa-solid fa-triangle-exclamation"></i> {{ error() }}</div>
        } @else {
          <span class="muted"><i class="fa-solid fa-spinner fa-spin"></i> Loading formats…</span>
        }
      </div>
    </div>

    @if (confirmDelete(); as f) {
      <vp-dialog title="Delete this format?" [subtitle]="f.name" icon="fa-trash" tone="b-danger" size="sm" (closed)="confirmDelete.set(null)">
        <p class="confirm">Reports already generated keep their PDFs. Banks must not be using it.</p>
        <ng-container footer>
          <button class="btn btn-ghost" type="button" (click)="confirmDelete.set(null)">Keep it</button>
          <button class="btn btn-danger" type="button" [disabled]="busy()" (click)="remove(f)"><i class="fa-solid fa-trash"></i> Delete</button>
        </ng-container>
      </vp-dialog>
    }
  `,
  styles: `
    .card-head { display: flex; align-items: flex-start; gap: 12px; }
    .card-head .sub { display: block; margin-top: 2px; max-width: 620px; }
    .list { display: flex; flex-direction: column; gap: 8px; padding-top: 10px; }
    .row { display: flex; align-items: center; gap: 12px; padding: 12px; border: 1px solid var(--line); border-radius: 10px; }
    .ic { display: grid; place-items: center; flex: none; width: 36px; height: 36px; border-radius: 10px; background: var(--brand-soft); color: var(--brand); }
    .main { display: flex; flex-direction: column; gap: 2px; flex: 1; min-width: 0; color: inherit; text-decoration: none; }
    .main:hover b { color: var(--brand); }
    .acts { display: flex; gap: 2px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 26px 10px; text-align: center; }
    .empty > i { font-size: 24px; color: var(--muted-2); }
    .confirm { margin: 0; line-height: 1.55; }
    @media (max-width: 599px) {
      .row { flex-wrap: wrap; }
      .acts { width: 100%; justify-content: flex-end; }
    }
  `,
})
export class ReportFormatsSection implements OnInit {
  private readonly api = inject(ReportFormatsApi);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly canEdit = input(false);

  protected readonly items = signal<ReportFormatItem[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmDelete = signal<ReportFormatItem | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  private async load(): Promise<void> {
    try {
      this.items.set(await firstValueFrom(this.api.list()));
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  protected async create(): Promise<void> {
    await this.run(async () => {
      const catalog = await firstValueFrom(this.api.catalog());
      const taken = new Set((this.items() ?? []).map((f) => f.name.toLowerCase()));
      let name = 'New format';
      for (let n = 2; taken.has(name.toLowerCase()); n++) name = `New format ${n}`;
      const f = await firstValueFrom(this.api.create(blankFormat(catalog, name)));
      void this.router.navigate(['/settings/report-formats', f.id]);
    }, 'Could not add the format');
  }

  protected async duplicate(f: ReportFormatItem): Promise<void> {
    await this.run(async () => {
      const copy = await firstValueFrom(this.api.duplicate(f.id));
      this.toast.ok('Format copied', copy.name);
      await this.load();
    }, 'Could not copy');
  }

  protected async makeDefault(f: ReportFormatItem): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.makeDefault(f.id));
      this.toast.ok('Default format', `${f.name} is used by banks without their own format.`);
      await this.load();
    }, 'Could not change the default');
  }

  protected async remove(f: ReportFormatItem): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.remove(f.id));
      this.confirmDelete.set(null);
      this.toast.ok('Format deleted', f.name);
      await this.load();
    }, 'Could not delete');
  }

  private async run(work: () => Promise<void>, failTitle: string): Promise<void> {
    this.busy.set(true);
    try {
      await work();
    } catch (e) {
      this.confirmDelete.set(null);
      this.toast.error(failTitle, toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
  }
}
