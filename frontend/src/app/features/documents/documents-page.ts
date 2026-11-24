import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Pager } from '../../shared/pager';
import {
  availabilityMeta, day, DOC_TYPES, docMeta, docName, DocumentListItem, DocumentPage, DocumentsApi, DocumentType, EXPIRY_BADGE, DocumentFile,
} from './documents.api';
import { FileOpener } from './file-opener';

const PAGE = 25;

/** Every document recorded on any case: search by number or case, and catch expiring or expired ones. */
@Component({
  selector: 'vp-documents-page',
  imports: [RouterLink, Pager],
  templateUrl: './documents-page.html',
  styleUrl: './documents-page.scss',
})
export class DocumentsPage implements OnInit {
  private readonly api = inject(DocumentsApi);
  private readonly opener = inject(FileOpener);

  protected readonly types = DOC_TYPES;
  protected readonly meta = docMeta;
  protected readonly avail = availabilityMeta;
  protected readonly expiryBadge = EXPIRY_BADGE;
  protected readonly name = docName;
  protected readonly day = day;

  protected readonly data = signal<DocumentPage | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly type = signal<DocumentType | ''>('');
  protected readonly expiry = signal<'' | 'Expiring' | 'Expired'>('');
  protected readonly mine = signal(false);
  protected readonly page = signal(1);
  protected search = '';
  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  protected readonly items = computed(() => this.data()?.page.items ?? []);
  protected readonly pages = computed(() => Math.max(1, Math.ceil((this.data()?.page.total ?? 0) / PAGE)));
  protected readonly range = computed(() => {
    const p = this.data()?.page;
    if (!p || !p.total) return '0 documents';
    const from = (p.page - 1) * p.pageSize + 1;
    return `${from}–${Math.min(p.total, from + p.items.length - 1)} of ${p.total}`;
  });
  protected readonly filtered = computed(() => !!(this.type() || this.expiry() || this.mine() || this.search));

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const seq = ++this.seq;
    this.loading.set(true);
    try {
      const r = await firstValueFrom(this.api.list({
        search: this.search, type: this.type(), expiry: this.expiry(), mine: this.mine(), page: this.page(), pageSize: PAGE,
      }));
      if (seq !== this.seq) return;
      this.data.set(r);
      this.error.set(null);
    } catch (e) {
      if (seq === this.seq) this.error.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  private reset(): void {
    this.page.set(1);
    void this.load();
  }

  protected onSearch(v: string): void {
    this.search = v.trim();
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.reset(), 300);
  }

  protected setType(v: string): void {
    this.type.set(v as DocumentType | '');
    this.reset();
  }

  protected setExpiry(v: '' | 'Expiring' | 'Expired'): void {
    this.expiry.set(this.expiry() === v ? '' : v);
    this.reset();
  }

  protected setMine(v: boolean): void {
    this.mine.set(v);
    this.reset();
  }

  protected clear(): void {
    this.search = '';
    this.type.set('');
    this.expiry.set('');
    this.mine.set(false);
    this.reset();
  }

  protected goTo(p: number): void {
    this.page.set(p);
    void this.load();
  }

  protected open(f: DocumentFile): void {
    void this.opener.open(f);
  }

  protected facts(i: DocumentListItem): string {
    const d = i.document;
    if (d.availability === 'NotProvided') return d.notes ?? '';
    return [d.number ? `No. ${d.number}` : null, d.issuedOn ? day(d.issuedOn) : null, d.issuedBy].filter(Boolean).join(' · ');
  }
}
