import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthImage } from './auth-image';
import { PhotoItem, PhotosApi, tagMeta } from './photos.api';

/** The first report photos of a case, with a link to its Photos page (shown on the assignment). */
@Component({
  selector: 'vp-photo-strip',
  imports: [RouterLink, AuthImage],
  template: `
    <div class="top">
      <b><i class="fa-solid fa-images"></i> Photos</b>
      @if (photos(); as ps) {
        <span class="small muted">{{ ps.length }} total · {{ inReport() }} in report</span>
      }
      <a class="small link" [routerLink]="['/photos', assignmentId()]">Manage <i class="fa-solid fa-arrow-right"></i></a>
    </div>
    @if (photos(); as ps) {
      @if (ps.length) {
        <div class="strip">
          @for (p of shown(); track p.id) {
            <a class="th" [routerLink]="['/photos', assignmentId()]" [title]="meta(p.tag).label"><vp-auth-img [photoId]="p.id" [alt]="meta(p.tag).label" /></a>
          }
          @if (ps.length > shown().length) { <a class="th more" [routerLink]="['/photos', assignmentId()]">+{{ ps.length - shown().length }}</a> }
        </div>
      } @else {
        <span class="small muted">No photos yet. They are added during the site inspection.</span>
      }
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 10px; }
    .top { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
    .top b i { color: var(--brand); margin-right: 4px; }
    .link { margin-left: auto; color: var(--brand); font-weight: 600; text-decoration: none; }
    .strip { display: grid; grid-template-columns: repeat(auto-fill, minmax(76px, 1fr)); gap: 8px; }
    .th { display: block; aspect-ratio: 1; border-radius: 8px; overflow: hidden; background: var(--bg-2); }
    .th vp-auth-img { width: 100%; height: 100%; }
    .more { display: grid; place-items: center; color: var(--brand); font-weight: 700; text-decoration: none; }
  `,
})
export class PhotoStrip implements OnInit {
  private readonly api = inject(PhotosApi);
  readonly assignmentId = input.required<string>();
  protected readonly meta = tagMeta;
  protected readonly photos = signal<PhotoItem[] | null>(null);
  protected readonly inReport = computed(() => (this.photos() ?? []).filter((p) => p.includeInReport).length);
  /** Report photos first, up to six. */
  protected readonly shown = computed(() => {
    const ps = this.photos() ?? [];
    return [...ps.filter((p) => p.includeInReport), ...ps.filter((p) => !p.includeInReport)].slice(0, 6);
  });

  async ngOnInit(): Promise<void> {
    try {
      this.photos.set((await firstValueFrom(this.api.case(this.assignmentId()))).photos);
    } catch {
      this.photos.set([]);
    }
  }
}
