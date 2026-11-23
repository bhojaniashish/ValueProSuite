import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LoginRequest, LoginResponse, UserProfile } from './auth.models';

const STORAGE_KEY = 'valupro.session';

interface Session {
  token: string;
  expiresAt: string;
  user: UserProfile;
}

/**
 * Holds the signed-in session. The API re-checks every token against the user's current
 * status and permissions, so this copy of the profile is refreshed on start and after changes.
 * The token is kept in localStorage for now; refresh tokens in an httpOnly cookie are a later step.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<Session | null>(this.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly token = computed(() => this.session()?.token ?? null);

  hasPermission(permission: string): boolean {
    return this.user()?.permissions.includes(permission) ?? false;
  }

  async login(request: LoginRequest): Promise<void> {
    this.apply(await firstValueFrom(this.http.post<LoginResponse>(`${environment.apiUrl}/api/auth/login`, request)));
  }

  /** Changes the user's own password; the API returns a fresh token because the old one is revoked. */
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    this.apply(
      await firstValueFrom(
        this.http.post<LoginResponse>(`${environment.apiUrl}/api/auth/change-password`, { currentPassword, newPassword }),
      ),
    );
  }

  /** Re-reads roles and permissions, e.g. after an admin edits the permission matrix. */
  async refreshProfile(): Promise<void> {
    const current = this.session();
    if (!current) return;
    try {
      const user = await firstValueFrom(this.http.get<UserProfile>(`${environment.apiUrl}/api/auth/me`));
      const updated = { ...current, user };
      this.session.set(updated);
      this.save(updated);
    } catch {
      /* a 401 is handled by the interceptor (signs out); other errors keep the cached profile */
    }
  }

  logout(): void {
    this.session.set(null);
    this.save(null);
    void this.router.navigate(['/login']);
  }

  private apply(res: LoginResponse): void {
    const session: Session = { token: res.accessToken, expiresAt: res.expiresAt, user: res.user };
    this.session.set(session);
    this.save(session);
  }

  private restore(): Session | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const s = JSON.parse(raw) as Session;
      return new Date(s.expiresAt) > new Date() ? s : null;
    } catch {
      return null;
    }
  }

  private save(session: Session | null): void {
    try {
      if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
      else localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* storage unavailable (private mode): session lasts for this tab only */
    }
  }
}
