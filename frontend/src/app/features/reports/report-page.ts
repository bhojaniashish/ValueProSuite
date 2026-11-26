import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { Choice } from '../../shared/chips';
import { statusMeta } from '../assignments/assignments.api';
import { saveBlob } from '../photos/photos.api';
import { inr } from '../valuations/valuations.api';
import { ReportEmailDialog } from './report-email-dialog';
import { CHECK_LINKS, ReportCheck, ReportDetail, ReportFileItem, ReportsApi, SaveReport } from './reports.api';

/** Prepare a case's report: check it's ready, adjust the wording, generate drafts, and approve the signed final. */
@Component({
  selector: 'vp-report-page',
  imports: [FormsModule, RouterLink, Dialog, Choice, ReportEmailDialog],
  templateUrl: './report-page.html',
  styleUrl: './report-page.scss',
})
export class ReportPage implements OnInit {
  private readonly api = inject(ReportsApi);
  private readonly toast = inject(ToastService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly auth = inject(AuthService);

  readonly id = input.required<string>();

  protected readonly inr = inr;
  protected readonly statusMeta = statusMeta;
  protected readonly perPage = [{ value: '4', label: '4 per page' }, { value: '2', label: '2 per page (larger)' }];

  protected readonly d = signal<ReportDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected f!: SaveReport;
  protected readonly dirty = signal(false);
  protected readonly busy = signal<'save' | 'draft' | 'approve' | null>(null);
  protected readonly confirmApprove = signal(false);
  protected readonly emailing = signal(false);
  protected readonly previewFor = signal<string | null>(null);
  protected readonly previewUrl = signal<SafeResourceUrl | null>(null);
  protected readonly previewLoading = signal(false);
  private rawUrl: string | null = null;

  protected readonly latest = computed(() => this.d()?.files[0] ?? null);
  protected readonly final = computed(() => this.d()?.files.find((f) => f.kind === 'Final') ?? null);
  /** The signature link goes to the signed-in user's own signature, so only when they are the case's valuer. */
  protected readonly isValuer = computed(() => this.auth.user()?.fullName === this.d()?.valuerName);
  protected readonly blocking = computed(() => (this.d()?.checks ?? []).filter((c) => c.blocking && !c.ok));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.dropPreview());
  }

  async ngOnInit(): Promise<void> {
    try {
      this.apply(await firstValueFrom(this.api.get(this.id())));
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  private apply(d: ReportDetail): void {
    this.d.set(d);
    this.f = {
      reportDate: d.fields.reportDate, valuationDate: d.fields.valuationDate, opinion: d.fields.opinion,
      declaration: d.fields.declaration, place: d.fields.place, photosPerPage: d.fields.photosPerPage,
    };
    this.dirty.set(false);
    const latest = d.files[0];
    if (latest && latest.id !== this.previewFor()) void this.preview(latest);
  }

  protected link(c: ReportCheck): string {
    return CHECK_LINKS[c.key](this.id());
  }

  protected changed(): void {
    this.dirty.set(true);
  }

  protected resetDeclaration(): void {
    this.f.declaration = null;
    this.changed();
    void this.save(true);
  }

  protected async save(quiet = false): Promise<boolean> {
    this.busy.set('save');
    try {
      this.apply(await firstValueFrom(this.api.save(this.id(), this.f)));
      if (!quiet) this.toast.ok('Report details saved');
      return true;
    } catch (e) {
      const p = toProblem(e);
      this.toast.error('Could not save', Object.values(p.fields)[0] ?? p.message);
      return false;
    } finally {
      this.busy.set(null);
    }
  }

  /** Saves any changes first, so the PDF has them. */
  protected async generate(): Promise<void> {
    if (this.dirty() && !(await this.save(true))) return;
    this.busy.set('draft');
    try {
      const d = await firstValueFrom(this.api.draft(this.id()));
      this.apply(d);
      this.toast.ok(`Draft version ${d.files[0]?.number} ready`, d.status === 'DraftPrepared' ? 'The case is now at "Draft Prepared".' : undefined);
    } catch (e) {
      this.toast.error('Could not generate the draft', toProblem(e).message);
    } finally {
      this.busy.set(null);
    }
  }

  protected async approve(): Promise<void> {
    if (this.dirty() && !(await this.save(true))) return;
    this.busy.set('approve');
    try {
      this.apply(await firstValueFrom(this.api.approve(this.id())));
      this.confirmApprove.set(false);
      this.toast.ok('Report approved and signed', 'The case is now "Submitted".');
    } catch (e) {
      this.toast.error('Could not approve', toProblem(e).message);
      this.confirmApprove.set(false);
    } finally {
      this.busy.set(null);
    }
  }

  // ---------------- PDFs ----------------

  protected async preview(f: ReportFileItem): Promise<void> {
    this.previewLoading.set(true);
    try {
      const blob = await firstValueFrom(this.api.pdf(f.id));
      this.dropPreview();
      this.rawUrl = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }));
      this.previewUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.rawUrl));
      this.previewFor.set(f.id);
    } catch (e) {
      this.toast.error('Could not load the PDF', toProblem(e).message);
    } finally {
      this.previewLoading.set(false);
    }
  }

  private dropPreview(): void {
    if (this.rawUrl) URL.revokeObjectURL(this.rawUrl);
    this.rawUrl = null;
  }

  protected async download(f: ReportFileItem): Promise<void> {
    try {
      const blob = await firstValueFrom(this.api.pdf(f.id));
      saveBlob(blob, `${this.d()!.number.replace(/\//g, '-')} report v${f.number}${f.kind === 'Final' ? ' (signed)' : ' (draft)'}.pdf`);
    } catch (e) {
      this.toast.error('Could not download', toProblem(e).message);
    }
  }

  protected emailed(d: ReportDetail): void {
    this.emailing.set(false);
    this.apply(d);
    const last = d.emails[0];
    this.toast.ok('Report emailed', last ? `To ${last.to}` : d.number);
  }

  protected size(bytes: number): string {
    return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`;
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-IN', { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' });
  }
}
