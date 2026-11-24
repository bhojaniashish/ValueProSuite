import { Component, effect, inject, Injectable, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { InspectionsApi } from './inspections.api';

/** Photo files need the sign-in token, so they are fetched as blobs and shown from object URLs (kept for the session). */
@Injectable({ providedIn: 'root' })
export class PhotoUrls {
  private readonly api = inject(InspectionsApi);
  private readonly cache = new Map<string, Promise<string>>();

  get(id: string, thumb: boolean): Promise<string> {
    const key = `${id}:${thumb}`;
    let p = this.cache.get(key);
    if (!p) {
      p = firstValueFrom(this.api.photoBlob(id, thumb)).then((b) => URL.createObjectURL(b));
      p.catch(() => this.cache.delete(key));
      this.cache.set(key, p);
    }
    return p;
  }
}

@Component({
  selector: 'vp-auth-img',
  template: `
    @if (url(); as u) { <img [src]="u" [alt]="alt()" loading="lazy" decoding="async" /> }
    @else if (failed()) { <span class="ph"><i class="fa-solid fa-image"></i></span> }
    @else { <span class="ph"><i class="fa-solid fa-spinner fa-spin"></i></span> }
  `,
  styles: `
    :host { display: block; background: var(--bg-2); overflow: hidden; }
    img { display: block; width: 100%; height: 100%; object-fit: cover; }
    .ph { display: grid; place-items: center; width: 100%; height: 100%; color: var(--muted-2); }
  `,
})
export class AuthImage {
  private readonly urls = inject(PhotoUrls);
  readonly photoId = input.required<string>();
  readonly thumb = input(true);
  readonly alt = input('');
  protected readonly url = signal<string | null>(null);
  protected readonly failed = signal(false);

  constructor() {
    effect(() => {
      const id = this.photoId();
      const thumb = this.thumb();
      this.url.set(null);
      this.failed.set(false);
      this.urls.get(id, thumb).then((u) => this.url.set(u), () => this.failed.set(true));
    });
  }
}
