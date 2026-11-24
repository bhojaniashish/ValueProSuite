import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Choice } from '../../shared/chips';
import { AuthImage } from './auth-image';
import { PhotoViewer, ViewerPhoto } from './photo-viewer';
import { GalleryItem, GalleryPage, needsAttention, PHOTO_TAGS, PhotosApi, PhotoTag, tagMeta } from './photos.api';

const PAGE = 60;

/** Every photo the firm has, newest first: find a case's photos, check locations, open them full size. */
@Component({
  selector: 'vp-photos-page',
  imports: [RouterLink, Choice, AuthImage, PhotoViewer],
  template: `
    <div class="page-head">
      <div>
        <h2>Photos</h2>
        <p>{{ summary()?.total ?? '–' }} site and uploaded photos across all cases. Open a case to choose and order its report photos.</p>
      </div>
    </div>

    <div class="card">
      <div class="toolbar">
        <div class="search">
          <i class="fa-solid fa-magnifying-glass"></i>
          <input type="search" placeholder="Search case number, applicant, property, caption" aria-label="Search photos" (input)="onSearch($any($event.target).value)" />
        </div>
        <div class="dates">
          <label class="fld">From<input type="date" [value]="from()" (change)="setDate('from', $any($event.target).value)" /></label>
          <label class="fld">To<input type="date" [value]="to()" (change)="setDate('to', $any($event.target).value)" /></label>
        </div>
        <label class="mine">
          <span class="switch"><input type="checkbox" [checked]="mine()" (change)="mine.set($any($event.target).checked); reload()" /><span class="track"></span></span>
          <span class="small">My cases</span>
        </label>
        @if (loading()) { <i class="fa-solid fa-spinner fa-spin muted"></i> }
      </div>

      <div class="filters">
        <button type="button" class="att" [class.on]="attention()" (click)="attention.set(!attention()); reload()" [attr.aria-pressed]="attention()">
          <i class="fa-solid fa-triangle-exclamation"></i> Needs a look <b>{{ summary()?.attention ?? 0 }}</b>
        </button>
        <vp-choice label="Photo type" [options]="tagOptions()" [value]="tag()" (valueChange)="tag.set($any($event)); reload()" />
      </div>

      @if (error()) {
        <div class="empty"><i class="fa-solid fa-triangle-exclamation"></i><b>Could not load photos</b><span>{{ error() }}</span>
          <button class="btn btn-soft btn-sm" type="button" (click)="reload()">Try again</button></div>
      } @else if (!items().length && !loading()) {
        <div class="empty">
          <i class="fa-solid fa-images"></i>
          <b>{{ filtered() ? 'No photos match' : 'No photos yet' }}</b>
          <span>{{ filtered() ? 'Try a different search or filter.' : 'Photos taken during site inspections show here.' }}</span>
        </div>
      } @else {
        <div class="grid">
          @for (g of items(); track g.photo.id; let i = $index) {
            <div class="ph">
              <button type="button" class="img" (click)="open(i)" [attr.aria-label]="'Open ' + meta(g.photo.tag).label + ' photo of ' + g.number">
                <vp-auth-img [photoId]="g.photo.id" [alt]="meta(g.photo.tag).label" />
                @if (needsAttention(g.photo); as warn) { <span class="warn" [title]="warn"><i class="fa-solid fa-triangle-exclamation"></i></span> }
                @if (!g.photo.includeInReport) { <span class="out" title="Not in the report"><i class="fa-solid fa-eye-slash"></i></span> }
              </button>
              <div class="cap">
                <b>{{ meta(g.photo.tag).label }}</b>
                <a class="case" [routerLink]="['/photos', g.photo.assignmentId]">
                  <span class="bank" [style.background]="g.bankColor">{{ g.bankShortCode }}</span>{{ g.number }}
                </a>
                <span class="small muted">{{ when(g.photo.takenAt) }}</span>
              </div>
            </div>
          }
        </div>
        @if (items().length < total()) {
          <div class="more">
            <button class="btn btn-ghost" type="button" (click)="more()" [disabled]="loading()">
              <i class="fa-solid" [class]="loading() ? 'fa-spinner fa-spin' : 'fa-angles-down'"></i> Show more ({{ total() - items().length }} left)
            </button>
          </div>
        }
      }
    </div>

    @if (viewing() !== null) {
      <vp-photo-viewer [photos]="viewerPhotos()" [start]="viewing()!" (closed)="viewing.set(null)" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 16px; }
    .page-head { margin-bottom: 0; }
    .card { padding: 0; overflow: hidden; }
    .toolbar { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 12px; padding: 14px 18px; border-bottom: 1px solid var(--line-2); }
    .dates { display: flex; flex-wrap: wrap; gap: 10px; min-width: 0; }
    .dates .fld { width: 160px; }
    .mine { display: inline-flex; align-items: center; gap: 8px; height: var(--control-h); cursor: pointer; }
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; padding: 12px 18px; border-bottom: 1px solid var(--line-2); }
    .att { display: inline-flex; align-items: center; gap: 6px; min-height: var(--control-h); padding: 7px 14px; border: 1px solid var(--line); border-radius: 99px;
      background: #fff; font-size: 13.5px; color: var(--warn); }
    .att b { padding: 0 7px; border-radius: 99px; background: var(--warn-soft); font-size: 12px; }
    .att.on { border-color: var(--warn); background: var(--warn-soft); font-weight: 600; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 14px; padding: 18px; }
    .ph { display: flex; flex-direction: column; gap: 8px; min-width: 0; }
    .img { position: relative; aspect-ratio: 4 / 3; padding: 0; border-radius: 10px; overflow: hidden; background: var(--bg-2); }
    .img vp-auth-img { width: 100%; height: 100%; }
    .img:hover { box-shadow: 0 0 0 3px var(--brand-soft); }
    .warn, .out { position: absolute; top: 6px; display: grid; place-items: center; width: 24px; height: 24px; border-radius: 50%; font-size: 11px; }
    .warn { right: 6px; background: var(--warn); color: #fff; }
    .out { left: 6px; background: rgba(0, 0, 0, .6); color: #fff; }
    .cap { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .cap b { font-size: 13px; }
    .case { display: inline-flex; align-items: center; gap: 6px; font-size: 12.5px; font-weight: 600; color: var(--brand); text-decoration: none; }
    .bank { padding: 1px 5px; border-radius: 5px; color: #fff; font-size: 10px; font-weight: 700; }
    .more { display: flex; justify-content: center; padding: 0 18px 18px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 48px 16px; text-align: center; color: var(--muted); }
    .empty i { font-size: 26px; color: var(--muted-2); margin-bottom: 6px; }
    .empty b { color: var(--ink); }
    @media (max-width: 599px) {
      .toolbar, .filters { padding: 12px 14px; }
      .search { max-width: none; flex-basis: 100%; }
      .dates { flex: 1 1 100%; }
      .dates .fld { flex: 1 1 130px; width: auto; }
      .grid { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; padding: 12px; }
    }
  `,
})
export class PhotosPage implements OnInit {
  private readonly api = inject(PhotosApi);

  protected readonly meta = tagMeta;
  protected readonly needsAttention = needsAttention;
  protected readonly items = signal<GalleryItem[]>([]);
  protected readonly total = signal(0);
  protected readonly summary = signal<GalleryPage['summary'] | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly tag = signal<PhotoTag | null>(null);
  protected readonly attention = signal(false);
  protected readonly mine = signal(false);
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly viewing = signal<number | null>(null);
  private search = '';
  private page = 1;
  private seq = 0;
  private timer?: ReturnType<typeof setTimeout>;

  protected readonly filtered = computed(() => !!(this.tag() || this.attention() || this.mine() || this.from() || this.to() || this.search));
  protected readonly tagOptions = computed(() => {
    const by = this.summary()?.byTag ?? {};
    return PHOTO_TAGS.filter((t) => by[t.value]).map((t) => ({ value: t.value, label: `${t.label} · ${by[t.value]}` }));
  });
  protected readonly viewerPhotos = computed<ViewerPhoto[]>(() =>
    this.items().map((g) => ({
      key: g.photo.id, serverId: g.photo.id, tag: g.photo.tag, caption: g.photo.caption, includeInReport: g.photo.includeInReport,
      takenAt: g.photo.takenAt, takenByName: g.photo.takenByName, latitude: g.photo.latitude, longitude: g.photo.longitude,
      distance: g.photo.distanceFromPropertyMetres, source: g.photo.source, stamped: g.photo.stamped,
      caseLabel: `${g.number} · ${g.applicantName}`,
    })));

  async ngOnInit(): Promise<void> {
    await this.load(true);
  }

  protected reload(): void {
    void this.load(true);
  }

  protected more(): void {
    void this.load(false);
  }

  private async load(reset: boolean): Promise<void> {
    const seq = ++this.seq;
    this.page = reset ? 1 : this.page + 1;
    this.loading.set(true);
    try {
      const r = await firstValueFrom(this.api.gallery({
        search: this.search, tag: this.tag() ?? '', mine: this.mine(), attention: this.attention(),
        from: this.from() || undefined, to: this.to() || undefined, page: this.page, pageSize: PAGE,
      }));
      if (seq !== this.seq) return;
      this.items.set(reset ? r.page.items : [...this.items(), ...r.page.items]);
      this.total.set(r.page.total);
      this.summary.set(r.summary);
      this.error.set(null);
    } catch (e) {
      if (seq === this.seq) this.error.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  protected onSearch(v: string): void {
    this.search = v.trim();
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.reload(), 300);
  }

  protected setDate(which: 'from' | 'to', v: string): void {
    (which === 'from' ? this.from : this.to).set(v);
    this.reload();
  }

  protected open(i: number): void {
    this.viewing.set(i);
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-IN', { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' });
  }
}
