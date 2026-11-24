import { Component, computed, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Dialog } from '../../core/ui/dialog';
import { AuthImage } from './auth-image';
import { metres, PHOTO_TAGS, PhotoSource, PhotoTag, tagMeta } from './photos.api';

/** A photo as the viewer shows it: on the server (serverId) or still waiting on the phone (localUrl). */
export interface ViewerPhoto {
  key: string;
  serverId?: string;
  localUrl?: string;
  tag: PhotoTag;
  caption: string | null;
  includeInReport: boolean;
  takenAt: string;
  takenByName?: string;
  latitude: number | null;
  longitude: number | null;
  distance: number | null;
  source: PhotoSource;
  stamped: boolean;
  /** Upload refused by the server. */
  error?: string;
  /** Case label shown in the gallery ("VAL/2026/0001 · Suresh Agarwal"). */
  caseLabel?: string;
}

export interface PhotoChange { photo: ViewerPhoto; tag: PhotoTag; caption: string | null; includeInReport: boolean }

/** Full-size photo with previous/next (arrow keys), what it shows, caption, report selection and where it was taken. */
@Component({
  selector: 'vp-photo-viewer',
  imports: [Dialog, FormsModule, AuthImage],
  host: { '(document:keydown)': 'onKey($event)' },
  template: `
    @if (current(); as p) {
      <vp-dialog [title]="meta(p.tag).label" [subtitle]="subtitle(p)" icon="fa-image" (closed)="closed.emit()">
        <div class="stage">
          @if (p.serverId) { <vp-auth-img [photoId]="p.serverId" [thumb]="false" [alt]="meta(p.tag).label" /> }
          @else { <img [src]="p.localUrl" [alt]="meta(p.tag).label" /> }
          @if (photos().length > 1) {
            <button class="nav prev" type="button" aria-label="Previous photo" [disabled]="index() === 0" (click)="go(-1)"><i class="fa-solid fa-chevron-left"></i></button>
            <button class="nav next" type="button" aria-label="Next photo" [disabled]="index() === photos().length - 1" (click)="go(1)"><i class="fa-solid fa-chevron-right"></i></button>
            <span class="count">{{ index() + 1 }} / {{ photos().length }}</span>
          }
        </div>

        @if (p.error) { <div class="alert-inline danger gap"><i class="fa-solid fa-circle-exclamation"></i> {{ p.error }}</div> }

        <div class="facts">
          @if (p.caseLabel) { <span><i class="fa-solid fa-folder-open"></i> {{ p.caseLabel }}</span> }
          <span>
            <i class="fa-solid fa-location-dot"></i>
            @if (p.latitude != null && p.longitude != null) {
              <a [href]="map(p)" target="_blank" rel="noopener">{{ p.latitude }}, {{ p.longitude }}</a>
              @if (p.distance != null) { <b [class.warn]="p.distance > 300"> · {{ metres(p.distance) }} from the property</b> }
            } @else { No location{{ p.source === 'Upload' ? ' (added later, not from the site camera)' : '' }} }
          </span>
          <span class="tags">
            <span class="badge" [class]="p.source === 'Site' ? 'b-info' : 'b-gray'">{{ p.source === 'Site' ? 'Site camera' : 'Uploaded' }}</span>
            @if (p.stamped) { <span class="badge b-teal"><i class="fa-solid fa-stamp"></i> Date & location stamped</span> }
          </span>
        </div>

        @if (canEdit()) {
          <div class="fld gap">
            <span class="lbl">What it shows</span>
            <div class="tag-pick">
              @for (t of tags; track t.value) {
                <button type="button" class="tp" [class.on]="tag() === t.value" (click)="tag.set(t.value)"><i class="fa-solid" [class]="t.icon"></i> {{ t.label }}</button>
              }
            </div>
          </div>
          <div class="row2 gap">
            <div class="fld grow"><label for="pv-cap">Caption</label><input id="pv-cap" [ngModel]="caption()" (ngModelChange)="caption.set($event)" placeholder="Living room, crack on east wall…" /></div>
            @if (showReport()) {
              <label class="inrep">
                <span class="switch"><input type="checkbox" [checked]="include()" (change)="include.set($any($event.target).checked)" /><span class="track"></span></span>
                <span class="small">In the report</span>
              </label>
            }
          </div>
        } @else {
          @if (p.caption) { <p class="cap">{{ p.caption }}</p> }
          @if (showReport()) { <p class="small muted">{{ p.includeInReport ? 'Included in the report.' : 'Not in the report.' }}</p> }
        }

        <ng-container footer>
          @if (canEdit()) {
            @if (confirmRemove()) {
              <span class="small confirm">Remove this photo?</span>
              <button class="btn btn-ghost" type="button" (click)="confirmRemove.set(false)">Keep</button>
              <button class="btn btn-danger" type="button" (click)="removed.emit(p); confirmRemove.set(false)"><i class="fa-solid fa-trash-can"></i> Remove</button>
            } @else {
              @if (canDelete() || !p.serverId) {
                <button class="btn btn-ghost danger-text" type="button" (click)="confirmRemove.set(true)"><i class="fa-solid fa-trash-can"></i> Remove</button>
              }
              @if (p.error) { <button class="btn btn-ghost" type="button" (click)="retry.emit(p)">Try upload again</button> }
              <button class="btn btn-pri" type="button" [disabled]="!dirty()" (click)="save(p)"><i class="fa-solid fa-check"></i> Save</button>
            }
          } @else {
            <button class="btn btn-pri" type="button" (click)="closed.emit()">Close</button>
          }
        </ng-container>
      </vp-dialog>
    }
  `,
  styles: `
    .stage { position: relative; display: grid; place-items: center; min-height: 220px; border-radius: 10px; overflow: hidden; background: #111; }
    .stage vp-auth-img { width: 100%; height: auto; min-height: 220px; background: #111; }
    .stage img, .stage ::ng-deep vp-auth-img img { width: 100%; max-height: 60vh; object-fit: contain; }
    .nav { position: absolute; top: 50%; transform: translateY(-50%); display: grid; place-items: center; width: 40px; height: 40px;
      border-radius: 50%; background: rgba(255, 255, 255, .9); color: var(--ink); box-shadow: var(--shadow-sm); }
    .nav:disabled { opacity: .35; }
    .prev { left: 10px; }
    .next { right: 10px; }
    .count { position: absolute; bottom: 10px; right: 12px; padding: 2px 8px; border-radius: 99px; background: rgba(0, 0, 0, .6); color: #fff; font-size: 12px; }
    .facts { display: flex; flex-direction: column; gap: 6px; margin-top: 12px; font-size: 13px; color: var(--ink-2); }
    .facts > span > i { width: 16px; color: var(--muted); }
    .facts a { color: var(--brand); }
    .facts .warn { color: var(--warn); }
    .tags { display: flex; flex-wrap: wrap; gap: 6px; }
    .gap { margin-top: 14px; }
    .tag-pick { display: flex; flex-wrap: wrap; gap: 6px; }
    .tp { display: inline-flex; align-items: center; gap: 6px; min-height: 34px; padding: 5px 10px; border: 1px solid var(--line); border-radius: 99px;
      font-size: 12.5px; color: var(--ink-2); }
    .tp i { font-size: 11px; color: var(--muted); }
    .tp.on { border-color: var(--brand); background: var(--brand-soft); color: var(--brand); font-weight: 600; }
    .tp.on i { color: var(--brand); }
    .row2 { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 14px; }
    .grow { flex: 1 1 220px; }
    .inrep { display: inline-flex; align-items: center; gap: 8px; height: var(--control-h); cursor: pointer; }
    .cap { margin: 12px 0 0; }
    .confirm { margin-right: auto; font-weight: 600; color: var(--danger); }
    .danger-text { color: var(--danger); margin-right: auto; }
  `,
})
export class PhotoViewer {
  readonly photos = input.required<ViewerPhoto[]>();
  readonly start = input(0);
  readonly canEdit = input(false);
  readonly canDelete = input(false);
  /** The inspection screen hides the report switch (the report is chosen on the Photos page). */
  readonly showReport = input(true);
  readonly saved = output<PhotoChange>();
  readonly removed = output<ViewerPhoto>();
  readonly retry = output<ViewerPhoto>();
  readonly closed = output<void>();

  protected readonly tags = PHOTO_TAGS;
  protected readonly meta = tagMeta;
  protected readonly metres = metres;
  protected readonly index = signal(0);
  protected readonly current = computed(() => this.photos()[Math.min(this.index(), this.photos().length - 1)] ?? null);
  protected readonly tag = signal<PhotoTag>('Other');
  protected readonly caption = signal<string | null>(null);
  protected readonly include = signal(true);
  protected readonly confirmRemove = signal(false);
  protected readonly dirty = computed(() => {
    const p = this.current();
    return !!p && (p.tag !== this.tag() || (p.caption ?? '') !== (this.caption() ?? '') || p.includeInReport !== this.include());
  });

  private started = false;

  constructor() {
    effect(() => {
      if (!this.started) {
        this.index.set(this.start());
        this.started = true;
      }
    });
    // Load the editable fields whenever another photo is shown.
    effect(() => {
      const p = this.current();
      if (!p) return this.closed.emit();
      this.tag.set(p.tag);
      this.caption.set(p.caption);
      this.include.set(p.includeInReport);
      this.confirmRemove.set(false);
    });
  }

  protected go(delta: number): void {
    this.index.set(Math.max(0, Math.min(this.photos().length - 1, this.index() + delta)));
  }

  protected onKey(e: KeyboardEvent): void {
    const typing = (e.target as HTMLElement)?.closest('input, textarea, select');
    if (typing) return;
    if (e.key === 'ArrowLeft') this.go(-1);
    if (e.key === 'ArrowRight') this.go(1);
  }

  protected save(p: ViewerPhoto): void {
    this.saved.emit({ photo: p, tag: this.tag(), caption: this.caption()?.trim() || null, includeInReport: this.include() });
  }

  protected subtitle(p: ViewerPhoto): string {
    const when = new Date(p.takenAt).toLocaleString('en-IN', { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' });
    return `${when}${p.takenByName ? ' · ' + p.takenByName : ''}${p.serverId ? '' : ' · waiting to upload'}`;
  }

  protected map(p: ViewerPhoto): string {
    return `https://www.google.com/maps/search/?api=1&query=${p.latitude},${p.longitude}`;
  }
}
