import { Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { BrandingService } from '../core/branding/branding.service';
import { NAVIGATION } from './navigation';

@Component({
  selector: 'vp-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  host: {
    '[class.collapsed]': 'collapsed()',
    '[class.mobile-open]': 'mobileOpen()',
  },
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly brand = inject(BrandingService);
  private readonly router = inject(Router);

  protected readonly collapsed = signal(false);
  protected readonly mobileOpen = signal(false);
  protected readonly menuOpen = signal(false);

  /** Only modules the user may open appear in the sidebar. */
  protected readonly groups = computed(() =>
    NAVIGATION.map((g) => ({ ...g, items: g.items.filter((i) => this.auth.hasPermission(i.permission)) })).filter(
      (g) => g.items.length > 0,
    ),
  );

  protected readonly initials = computed(() =>
    (this.auth.user()?.fullName ?? '?')
      .split(' ')
      .map((p) => p[0])
      .slice(0, 2)
      .join('')
      .toUpperCase(),
  );

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly pageTitle = computed(() => {
    const path = this.url().split(/[/?#]/)[1];
    if (path === 'change-password') return 'Change password';
    if (path === 'account') return 'My signature';
    return NAVIGATION.flatMap((g) => g.items).find((i) => i.path === path)?.label ?? 'ValuPro';
  });

  constructor() {
    // Pick up role/permission changes made since this session's token was issued.
    void this.auth.refreshProfile();

    // Close the phone drawer and profile menu after navigating.
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => {
      this.mobileOpen.set(false);
      this.menuOpen.set(false);
    });
  }

  protected toggleSidebar(): void {
    if (window.matchMedia('(max-width: 991px)').matches) this.mobileOpen.update((v) => !v);
    else this.collapsed.update((v) => !v);
  }
}
