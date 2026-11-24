import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';
import { AuthImage } from './auth-image';
import { PhotoQueue, QueuedPhoto } from './photo-queue';
import { PhotoTagDialog } from './photo-tag-dialog';
import { newKey, preparePhoto } from './photo-tools';
import { PhotoChange, PhotoViewer, ViewerPhoto } from './photo-viewer';
import { CasePhotos, needsAttention, PhotoItem, PhotosApi, PhotoTag, saveBlob, tagMeta } from './photos.api';

/** One case's photos: pick and order the report photos, add photos received later, download them all. */
@Component({
  selector: 'vp-case-photos-page',
  imports: [RouterLink, AuthImage, PhotoViewer, PhotoTagDialog],
  host: { '(dragover)': 'onDragOver($event)', '(drop)': 'onDropFiles($event)' },
  template: `
    @if (error()) {
      <div class="card empty"><i class="fa-solid fa-triangle-exclamation"></i><b>Could not load the photos</b><span>{{ error() }}</span>
        <a class="btn btn-soft btn-sm" routerLink="/photos">Back to all photos</a></div>
    } @else if (c(); as c) {
      <div class="head card">
        <a class="back" routerLink="/photos" aria-label="Back to all photos"><i class="fa-solid fa-arrow-left"></i></a>
        <div class="who">
          <div class="l1">
            <span class="bank" [style.background]="c.bankColor">{{ c.bankShortCode }}</span>
            <a class="num" [routerLink]="['/assignments', c.assignmentId]">{{ c.number }}</a>
          </div>
          <b>{{ c.applicantName }}</b>
          <span class="small muted">{{ c.propertySummary }}</span>
        </div>
        <div class="acts">
          @if (c.canEdit) {
            <button class="btn btn-ghost" type="button" (click)="fileIn.click()" [disabled]="preparing()">
              <i class="fa-solid" [class]="preparing() ? 'fa-spinner fa-spin' : 'fa-upload'"></i> Add photos
            </button>
          }
          <button class="btn btn-pri" type="button" (click)="download(true)" [disabled]="!report().length || downloading()">
            <i class="fa-solid" [class]="downloading() ? 'fa-spinner fa-spin' : 'fa-file-zipper'"></i> Download report photos
          </button>
          <button class="btn-icon" type="button" title="Download all photos" aria-label="Download all photos (ZIP)" (click)="download(false)" [disabled]="!c.photos.length || downloading()">
            <i class="fa-solid fa-download"></i>
          </button>
        </div>
      </div>

      @if (!c.canEdit && c.readOnlyReason) { <div class="alert-inline info"><i class="fa-solid fa-lock"></i> {{ c.readOnlyReason }}</div> }
      @if (c.canEdit && !hasFront()) {
        <div class="alert-inline info"><i class="fa-solid fa-circle-info"></i> Banks expect a front elevation photo first in the report.</div>
      }

      <div class="card sec">
        <div class="sec-head">
          <div>
            <h3>In the report <span class="badge b-brand">{{ report().length }}</span></h3>
            <span class="small muted">{{ c.canEdit ? 'In this order. Drag to reorder, or use the arrows.' : 'In this order.' }}</span>
          </div>
        </div>
        @if (report().length) {
          <div class="grid">
            @for (p of report(); track p.id; let i = $index) {
              <div class="ph" [class.dragging]="dragId() === p.id" [class.over]="overId() === p.id"
                [attr.draggable]="c.canEdit" (dragstart)="dragStart($event, p)" (dragend)="dragEnd()"
                (dragover)="dragOver($event, p)" (drop)="dropOn($event, p)">
                <button type="button" class="img" (click)="openServer(p)" [attr.aria-label]="'Open photo ' + (i + 1) + ': ' + meta(p.tag).label">
                  <vp-auth-img [photoId]="p.id" [alt]="meta(p.tag).label" />
                  <span class="no">{{ i + 1 }}</span>
                  @if (needsAttention(p); as w) { <span class="warn" [title]="w"><i class="fa-solid fa-triangle-exclamation"></i></span> }
                </button>
                <div class="row">
                  <div class="cap"><b>{{ meta(p.tag).label }}</b>@if (p.caption) { <span class="small muted">{{ p.caption }}</span> }</div>
                  @if (c.canEdit) {
                    <div class="tools">
                      <button class="btn-icon" type="button" aria-label="Move earlier" [disabled]="i === 0 || busy()" (click)="move(p, -1)"><i class="fa-solid fa-arrow-left"></i></button>
                      <button class="btn-icon" type="button" aria-label="Move later" [disabled]="i === report().length - 1 || busy()" (click)="move(p, 1)"><i class="fa-solid fa-arrow-right"></i></button>
                      <button class="btn-icon" type="button" title="Leave out of the report" aria-label="Leave out of the report" [disabled]="busy()" (click)="setIncluded(p, false)"><i class="fa-solid fa-eye-slash"></i></button>
                    </div>
                  }
                </div>
              </div>
            }
          </div>
        } @else {
          <div class="empty small-empty"><i class="fa-solid fa-images"></i><span>No photos chosen for the report yet.</span></div>
        }
      </div>

      @if (others().length || waiting().length) {
        <div class="card sec">
          <div class="sec-head">
            <div>
              <h3>Not in the report <span class="badge b-gray">{{ others().length }}</span></h3>
              <span class="small muted">Kept on file with the case.</span>
            </div>
          </div>
          <div class="grid">
            @for (q of waiting(); track q.clientKey) {
              <div class="ph">
                <div class="img"><img [src]="localUrl(q)" [alt]="meta(q.tag).label" />
                  <span class="up" [class.bad]="q.error" [title]="q.error ?? 'Uploading'"><i class="fa-solid" [class]="q.error ? 'fa-circle-exclamation' : 'fa-spinner fa-spin'"></i></span>
                </div>
                <div class="cap"><b>{{ meta(q.tag).label }}</b><span class="small" [class.err]="q.error">{{ q.error ?? 'Uploading…' }}</span></div>
              </div>
            }
            @for (p of others(); track p.id) {
              <div class="ph out">
                <button type="button" class="img" (click)="openServer(p)" [attr.aria-label]="'Open ' + meta(p.tag).label + ' photo'">
                  <vp-auth-img [photoId]="p.id" [alt]="meta(p.tag).label" />
                  @if (needsAttention(p); as w) { <span class="warn" [title]="w"><i class="fa-solid fa-triangle-exclamation"></i></span> }
                </button>
                <div class="row">
                  <div class="cap"><b>{{ meta(p.tag).label }}</b>@if (p.caption) { <span class="small muted">{{ p.caption }}</span> }</div>
                  @if (c.canEdit) {
                    <div class="tools"><button class="btn btn-soft btn-sm" type="button" [disabled]="busy()" (click)="setIncluded(p, true)"><i class="fa-solid fa-plus"></i> Report</button></div>
                  }
                </div>
              </div>
            }
          </div>
        </div>
      }

      @if (dropping()) { <div class="dropzone"><i class="fa-solid fa-upload"></i> Drop photos to add them</div> }
      <input #fileIn class="hidden-file" type="file" accept="image/*" multiple (change)="picked($event)" />
    } @else {
      <div class="card empty"><i class="fa-solid fa-spinner fa-spin"></i><span>Loading photos…</span></div>
    }

    @if (tagging(); as files) {
      <vp-photo-tag-dialog [count]="files.length" initialTag="Other"
        note="Added photos keep no location and are not stamped, since the app can't tell where they were taken."
        (done)="addFiles(files, $event.tag, $event.caption)" (closed)="tagging.set(null)" />
    }
    @if (viewing(); as v) {
      <vp-photo-viewer [photos]="v.list" [start]="v.start" [canEdit]="c()?.canEdit ?? false" [canDelete]="c()?.canDelete ?? false"
        (saved)="onSaved($event)" (removed)="onRemoved($event)" (closed)="viewing.set(null)" />
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
    .acts { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .sec { padding: 0; }
    .sec-head { padding: 14px 18px; border-bottom: 1px solid var(--line-2); }
    .sec-head h3 { display: flex; align-items: center; gap: 8px; margin: 0 0 2px; font-size: 15px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 16px; padding: 18px; }
    .ph { display: flex; flex-direction: column; gap: 8px; min-width: 0; border-radius: 12px; transition: opacity .15s; }
    .ph[draggable='true'] { cursor: grab; }
    .ph.dragging { opacity: .4; }
    .ph.over .img { box-shadow: 0 0 0 3px var(--brand); }
    .ph.out .img { opacity: .75; }
    .img { position: relative; display: block; aspect-ratio: 4 / 3; padding: 0; border-radius: 10px; overflow: hidden; background: var(--bg-2); }
    .img vp-auth-img, .img img { width: 100%; height: 100%; object-fit: cover; display: block; }
    .no { position: absolute; top: 6px; left: 6px; display: grid; place-items: center; min-width: 24px; height: 24px; padding: 0 6px; border-radius: 99px;
      background: var(--brand); color: #fff; font-size: 12px; font-weight: 700; box-shadow: 0 0 0 2px #fff; }
    .warn { position: absolute; top: 6px; right: 6px; display: grid; place-items: center; width: 24px; height: 24px; border-radius: 50%; background: var(--warn); color: #fff; font-size: 11px; }
    .up { position: absolute; right: 6px; bottom: 6px; display: grid; place-items: center; width: 24px; height: 24px; border-radius: 50%; background: rgba(0,0,0,.6); color: #fff; font-size: 11px; }
    .up.bad { background: var(--danger); }
    .row { display: flex; align-items: flex-start; justify-content: space-between; gap: 8px; }
    .cap { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .cap b { font-size: 13px; }
    .cap span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .err { color: var(--danger); }
    .tools { display: flex; flex: none; gap: 2px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 48px 16px; text-align: center; color: var(--muted); }
    .empty i { font-size: 26px; color: var(--muted-2); }
    .empty b { color: var(--ink); }
    .small-empty { padding: 28px 16px; }
    .hidden-file { display: none; }
    .dropzone { position: fixed; inset: 16px; z-index: 50; display: grid; place-items: center; border: 3px dashed var(--brand); border-radius: 18px;
      background: color-mix(in srgb, var(--brand-soft) 85%, transparent); color: var(--brand); font-size: 18px; font-weight: 700; pointer-events: none; }
    @media (max-width: 599px) {
      .head { padding: 12px 14px; }
      .acts { width: 100%; }
      .acts .btn { flex: 1; justify-content: center; }
      .grid { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; padding: 12px; }
      .row { flex-direction: column; }
    }
  `,
})
export class CasePhotosPage implements OnInit {
  private readonly api = inject(PhotosApi);
  private readonly queue = inject(PhotoQueue);
  private readonly toast = inject(ToastService);

  readonly id = input.required<string>();

  protected readonly meta = tagMeta;
  protected readonly needsAttention = needsAttention;
  protected readonly c = signal<CasePhotos | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly preparing = signal(false);
  protected readonly downloading = signal(false);
  protected readonly tagging = signal<File[] | null>(null);
  protected readonly viewing = signal<{ list: ViewerPhoto[]; start: number } | null>(null);
  protected readonly dragId = signal<string | null>(null);
  protected readonly overId = signal<string | null>(null);
  protected readonly dropping = signal(false);
  private dropTimer?: ReturnType<typeof setTimeout>;
  private readonly urls = new Map<string, string>();

  protected readonly report = computed(() => (this.c()?.photos ?? []).filter((p) => p.includeInReport));
  protected readonly others = computed(() => (this.c()?.photos ?? []).filter((p) => !p.includeInReport));
  protected readonly waiting = computed(() => this.queue.items().filter((q) => q.assignmentId === this.id()));
  protected readonly hasFront = computed(() => this.report().some((p) => p.tag === 'Front'));

  constructor() {
    const sub = this.queue.uploaded.subscribe(({ assignmentId, photo }) => {
      if (assignmentId !== this.id()) return;
      this.c.update((c) => (c && !c.photos.some((p) => p.id === photo.id) ? { ...c, photos: [...c.photos, photo] } : c));
    });
    inject(DestroyRef).onDestroy(() => {
      sub.unsubscribe();
      for (const u of this.urls.values()) URL.revokeObjectURL(u);
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      this.c.set(await firstValueFrom(this.api.case(this.id())));
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  private setPhotos(photos: PhotoItem[]): void {
    this.c.update((c) => (c ? { ...c, photos } : c));
  }

  // ---------------- report selection and order ----------------

  /** Saves the order: report photos first (as shown), then the rest. */
  private async saveOrder(report: PhotoItem[], others: PhotoItem[]): Promise<void> {
    const before = this.c()!.photos;
    const next = [...report, ...others];
    this.setPhotos(next);
    this.busy.set(true);
    try {
      this.setPhotos(await firstValueFrom(this.api.reorder(this.id(), next.map((p) => p.id))));
    } catch (e) {
      this.setPhotos(before);
      this.toast.error('Could not change the order', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected move(p: PhotoItem, delta: number): void {
    const list = [...this.report()];
    const i = list.indexOf(p);
    const j = i + delta;
    if (j < 0 || j >= list.length) return;
    [list[i], list[j]] = [list[j], list[i]];
    void this.saveOrder(list, this.others());
  }

  protected async setIncluded(p: PhotoItem, include: boolean): Promise<void> {
    this.busy.set(true);
    try {
      const updated = await firstValueFrom(this.api.update(p.id, p.tag, p.caption, include));
      const report = this.report().filter((x) => x.id !== p.id);
      const others = this.others().filter((x) => x.id !== p.id);
      // Added photos go to the end of the report; removed ones to the start of the rest.
      if (include) report.push(updated);
      else others.unshift(updated);
      this.busy.set(false);
      await this.saveOrder(report, others);
    } catch (e) {
      this.toast.error('Could not change the photo', toProblem(e).message);
      this.busy.set(false);
    }
  }

  // drag to reorder (desktop)
  protected dragStart(e: DragEvent, p: PhotoItem): void {
    if (!this.c()?.canEdit) return;
    this.dragId.set(p.id);
    e.dataTransfer?.setData('text/plain', p.id);
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }

  protected dragEnd(): void {
    this.dragId.set(null);
    this.overId.set(null);
  }

  protected dragOver(e: DragEvent, p: PhotoItem): void {
    if (!this.dragId()) return;
    e.preventDefault();
    e.stopPropagation();
    this.overId.set(p.id);
  }

  protected dropOn(e: DragEvent, target: PhotoItem): void {
    const id = this.dragId();
    this.dragEnd();
    if (!id) return;
    e.preventDefault();
    e.stopPropagation();
    if (id === target.id) return;
    const list = this.report().filter((x) => x.id !== id);
    const moved = this.report().find((x) => x.id === id);
    if (!moved) return;
    list.splice(list.indexOf(target) + (this.report().indexOf(moved) < this.report().indexOf(target) ? 1 : 0), 0, moved);
    void this.saveOrder(list, this.others());
  }

  // ---------------- adding photos ----------------

  protected picked(event: Event): void {
    const el = event.target as HTMLInputElement;
    const files = Array.from(el.files ?? []).filter((f) => f.type.startsWith('image/'));
    el.value = '';
    if (files.length) this.tagging.set(files);
  }

  /** Photos dropped from the computer (e.g. sent by the field team on WhatsApp). */
  protected onDragOver(e: DragEvent): void {
    if (this.dragId() || !this.c()?.canEdit || !e.dataTransfer?.types.includes('Files')) return;
    e.preventDefault();
    this.dropping.set(true);
    clearTimeout(this.dropTimer);
    this.dropTimer = setTimeout(() => this.dropping.set(false), 150);
  }

  protected onDropFiles(e: DragEvent): void {
    if (this.dragId() || !e.dataTransfer?.files.length) return;
    e.preventDefault();
    this.dropping.set(false);
    const files = Array.from(e.dataTransfer.files).filter((f) => f.type.startsWith('image/'));
    if (files.length) this.tagging.set(files);
  }

  protected async addFiles(files: File[], tag: PhotoTag, caption: string | null): Promise<void> {
    this.tagging.set(null);
    this.preparing.set(true);
    try {
      for (const f of files) {
        const p = await preparePhoto(f);
        await this.queue.add({
          assignmentId: this.id(), clientKey: newKey(), tag, caption, latitude: null, longitude: null,
          takenAt: new Date(f.lastModified || Date.now()).toISOString(), width: p.width, height: p.height,
          image: p.image, thumb: p.thumb, source: 'Upload', stamped: false,
        });
      }
    } catch (e) {
      this.toast.error('Could not add the photos', (e as Error).message);
    } finally {
      this.preparing.set(false);
    }
  }

  protected localUrl(q: QueuedPhoto): string {
    let u = this.urls.get(q.clientKey);
    if (!u) {
      u = URL.createObjectURL(q.thumb);
      this.urls.set(q.clientKey, u);
    }
    return u;
  }

  // ---------------- viewer ----------------

  protected openServer(p: PhotoItem): void {
    const list = [...this.report(), ...this.others()];
    this.viewing.set({
      list: list.map((x) => ({
        key: x.id, serverId: x.id, tag: x.tag, caption: x.caption, includeInReport: x.includeInReport, takenAt: x.takenAt,
        takenByName: x.takenByName, latitude: x.latitude, longitude: x.longitude, distance: x.distanceFromPropertyMetres,
        source: x.source, stamped: x.stamped,
      })),
      start: list.indexOf(p),
    });
  }

  protected async onSaved(ch: PhotoChange): Promise<void> {
    const p = this.c()!.photos.find((x) => x.id === ch.photo.serverId);
    if (!p) return;
    try {
      const updated = await firstValueFrom(this.api.update(p.id, ch.tag, ch.caption, ch.includeInReport));
      this.viewing.set(null);
      if (updated.includeInReport !== p.includeInReport) {
        const report = this.report().filter((x) => x.id !== p.id);
        const others = this.others().filter((x) => x.id !== p.id);
        if (updated.includeInReport) report.push(updated);
        else others.unshift(updated);
        await this.saveOrder(report, others);
      } else {
        this.setPhotos(this.c()!.photos.map((x) => (x.id === updated.id ? updated : x)));
      }
    } catch (e) {
      this.toast.error('Could not change the photo', toProblem(e).message);
    }
  }

  protected async onRemoved(v: ViewerPhoto): Promise<void> {
    try {
      await firstValueFrom(this.api.delete(v.serverId!));
      this.setPhotos(this.c()!.photos.filter((x) => x.id !== v.serverId));
      this.viewing.set(null);
      this.toast.ok('Photo removed');
    } catch (e) {
      this.toast.error('Could not remove the photo', toProblem(e).message);
    }
  }

  // ---------------- download ----------------

  protected async download(reportOnly: boolean): Promise<void> {
    const c = this.c();
    if (!c) return;
    this.downloading.set(true);
    try {
      const blob = await firstValueFrom(this.api.zip(c.assignmentId, reportOnly));
      saveBlob(blob, `${c.number.replace(/\//g, '-')} ${reportOnly ? 'report photos' : 'all photos'}.zip`);
    } catch (e) {
      this.toast.error('Could not download', toProblem(e).message);
    } finally {
      this.downloading.set(false);
    }
  }
}
