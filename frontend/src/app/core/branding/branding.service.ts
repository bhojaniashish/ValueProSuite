import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Branding {
  firmName: string;
  primaryColor: string;
  accentColor: string;
  logoUrl: string | null;
}

const DEFAULT: Branding = { firmName: 'ValuPro', primaryColor: '#2B50C4', accentColor: '#C2922E', logoUrl: null };

/**
 * The firm's name, colours and logo (Settings → Branding). Loaded before the app renders,
 * so the login page already looks like the firm. Colours are applied as CSS variables;
 * lighter shades are derived in styles.scss.
 */
@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly http = inject(HttpClient);
  private readonly title = inject(Title);
  readonly branding = signal<Branding>(DEFAULT);

  async load(): Promise<void> {
    try {
      this.apply(await firstValueFrom(this.http.get<Branding>(`${environment.apiUrl}/api/branding`)));
    } catch {
      this.apply(DEFAULT); // API unreachable: keep the default look
    }
  }

  /** Called after Settings saves, so changes show at once. */
  apply(b: Branding): void {
    this.branding.set(b);
    const root = document.documentElement.style;
    root.setProperty('--brand', b.primaryColor);
    root.setProperty('--gold', b.accentColor);
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', b.primaryColor);
    const current = this.title.getTitle();
    this.title.setTitle(current.includes('·') ? current.replace(/·[^·]*$/, `· ${b.firmName}`) : b.firmName);
  }

  /** Preview colours while editing without saving them. */
  preview(primary: string, accent: string): void {
    const root = document.documentElement.style;
    root.setProperty('--brand', primary);
    root.setProperty('--gold', accent);
  }

  /** Undo a preview. */
  restore(): void {
    this.apply(this.branding());
  }
}
