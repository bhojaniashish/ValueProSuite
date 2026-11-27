import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/ui/toast';
import { Choice } from '../../shared/chips';
import { Placeholder, ReportFormatCatalog, ReportFormatDetail, ReportFormatsApi, ReportLayout, SaveReportFormat, SectionInfo } from './report-formats.api';

type ValueFlag = 'showWorking' | 'showRealizable' | 'showDistress' | 'showGuideline' | 'showInsurance';
type TextField = 'opinionWording' | 'declaration';

/** Edit one report format, with a live preview of a sample report in it. */
@Component({
  selector: 'vp-report-format-editor',
  imports: [FormsModule, RouterLink, Choice],
  templateUrl: './report-format-editor.html',
  styleUrl: './report-format-editor.scss',
})
export class ReportFormatEditor implements OnInit {
  private readonly api = inject(ReportFormatsApi);
  private readonly toast = inject(ToastService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly auth = inject(AuthService);

  readonly id = input.required<string>();

  protected readonly canEdit = computed(() => this.auth.hasPermission(Permission.settingsManage));
  protected readonly catalog = signal<ReportFormatCatalog | null>(null);
  protected readonly detail = signal<ReportFormatDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected f!: SaveReportFormat;
  protected readonly dirty = signal(false);
  protected readonly saving = signal(false);
  protected readonly fieldErrors = signal<Record<string, string>>({});

  protected readonly kinds = [{ value: 'valuation', label: 'Valuation' }, { value: 'stage', label: 'Stage inspection' }];
  protected readonly kind = signal<'valuation' | 'stage'>('valuation');
  protected readonly previewUrl = signal<SafeResourceUrl | null>(null);
  protected readonly previewing = signal(false);
  protected readonly previewError = signal<string | null>(null);
  private rawUrl: string | null = null;
  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  protected readonly values: { key: ValueFlag; label: string }[] = [
    { key: 'showWorking', label: 'Working table (item, working, amount)' },
    { key: 'showRealizable', label: 'Realizable value' },
    { key: 'showDistress', label: 'Distress / forced sale value' },
    { key: 'showGuideline', label: 'Government (guideline) value' },
    { key: 'showInsurance', label: 'Insurance value' },
  ];

  constructor() {
    const warn = (e: BeforeUnloadEvent) => {
      if (this.dirty()) e.preventDefault();
    };
    window.addEventListener('beforeunload', warn);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('beforeunload', warn);
      clearTimeout(this.timer);
      if (this.rawUrl) URL.revokeObjectURL(this.rawUrl);
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      const [catalog, detail] = await Promise.all([firstValueFrom(this.api.catalog()), firstValueFrom(this.api.get(this.id()))]);
      this.catalog.set(catalog);
      this.apply(detail);
      void this.preview();
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  private apply(d: ReportFormatDetail): void {
    this.detail.set(d);
    this.f = {
      name: d.name, description: d.description, title: d.title, subtitle: d.subtitle, addressTo: d.addressTo,
      layout: structuredClone(d.layout), declaration: d.declaration, limitingConditions: d.limitingConditions, opinionWording: d.opinionWording,
    };
    this.dirty.set(false);
    this.fieldErrors.set({});
  }

  protected info(key: string): SectionInfo | undefined {
    return this.catalog()?.sections.find((s) => s.key === key);
  }

  protected changed(): void {
    this.dirty.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.preview(), 700);
  }

  protected move(i: number, by: -1 | 1): void {
    const list = this.f.layout.sections;
    const j = i + by;
    if (j < 0 || j >= list.length) return;
    [list[i], list[j]] = [list[j], list[i]];
    this.changed();
  }

  protected flag(key: keyof ReportLayout, on: boolean): void {
    (this.f.layout as unknown as Record<string, unknown>)[key] = on;
    this.changed();
  }

  protected setKind(k: string | null): void {
    this.kind.set(k === 'stage' ? 'stage' : 'valuation');
    void this.preview();
  }

  /** Puts a placeholder where the cursor is in the text box. */
  protected insert(field: TextField, box: HTMLTextAreaElement, p: Placeholder): void {
    const text = this.f[field] ?? '';
    const at = box.selectionStart ?? text.length;
    const end = box.selectionEnd ?? at;
    this.f[field] = text.slice(0, at) + p.token + text.slice(end);
    this.changed();
    setTimeout(() => {
      box.focus();
      box.setSelectionRange(at + p.token.length, at + p.token.length);
    });
  }

  protected startFromStandard(field: TextField): void {
    const c = this.catalog();
    if (!c) return;
    this.f[field] = field === 'declaration' ? c.standardDeclaration : c.standardOpinion;
    this.changed();
  }

  private body(): SaveReportFormat {
    const t = (s: string | null) => (s?.trim() ? s.trim() : null);
    return {
      ...this.f, name: this.f.name.trim(), description: t(this.f.description), title: t(this.f.title), subtitle: t(this.f.subtitle),
      addressTo: t(this.f.addressTo), declaration: t(this.f.declaration), limitingConditions: t(this.f.limitingConditions), opinionWording: t(this.f.opinionWording),
      layout: { ...this.f.layout, sections: this.f.layout.sections.map((s) => ({ ...s, title: t(s.title) })) },
    };
  }

  protected async preview(): Promise<void> {
    if (!this.f) return;
    const seq = ++this.seq;
    this.previewing.set(true);
    try {
      const blob = await firstValueFrom(this.api.preview(this.body(), this.kind() === 'stage'));
      if (seq !== this.seq) return;
      if (this.rawUrl) URL.revokeObjectURL(this.rawUrl);
      this.rawUrl = URL.createObjectURL(blob);
      this.previewUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.rawUrl + '#view=FitH&toolbar=0'));
      this.previewError.set(null);
    } catch (e) {
      if (seq !== this.seq) return;
      const p = toProblem(e);
      this.previewError.set(Object.values(p.fields)[0] ?? p.message);
    } finally {
      if (seq === this.seq) this.previewing.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (!this.f.name.trim()) {
      this.fieldErrors.set({ name: 'Name the format.' });
      return;
    }
    this.saving.set(true);
    try {
      this.apply(await firstValueFrom(this.api.update(this.id(), this.body())));
      this.toast.ok('Format saved', this.f.name);
    } catch (e) {
      const p = toProblem(e);
      this.fieldErrors.set(p.fields);
      this.toast.error('Could not save', Object.values(p.fields)[0] ?? p.message);
    } finally {
      this.saving.set(false);
    }
  }

  protected reset(): void {
    const d = this.detail();
    if (!d) return;
    this.apply(d);
    void this.preview();
  }

  protected err(key: string): string | null {
    const e = this.fieldErrors();
    return e[key] ?? e[key.charAt(0).toUpperCase() + key.slice(1)] ?? null;
  }
}
