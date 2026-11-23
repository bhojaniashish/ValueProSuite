import { inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { BrandingService } from './branding.service';

/** Route titles are written as "Page · ValuPro"; show the firm's own name instead. */
@Injectable()
export class FirmTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly brand = inject(BrandingService);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const firm = this.brand.branding().firmName;
    const page = this.buildTitle(snapshot)?.replace(/\s*·\s*ValuPro$/, '');
    this.title.setTitle(page ? `${page} · ${firm}` : firm);
  }
}
