import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router, UrlTree, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { permissionGuard, authGuard } from './core/auth/auth.guards';
import { NAVIGATION } from './layout/navigation';

describe('auth guards', () => {
  const state = { url: '/clients' } as RouterStateSnapshot;
  const route = (permission?: string) => ({ data: { permission } }) as unknown as ActivatedRouteSnapshot;

  function setup(permissions: string[] | null) {
    localStorage.clear();
    if (permissions) {
      localStorage.setItem(
        'valupro.session',
        JSON.stringify({
          token: 't',
          expiresAt: new Date(Date.now() + 60_000).toISOString(),
          user: { id: '1', email: 'a@x.in', fullName: 'A B', registrationNo: null, tenantId: 't', tenantName: 'Firm', roles: [], permissions },
        }),
      );
    }
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient()] });
    return TestBed.inject(Router);
  }

  it('sends signed-out users to login with a return URL', () => {
    const router = setup(null);
    const result = TestBed.runInInjectionContext(() => authGuard(route(), state)) as UrlTree;
    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fclients');
  });

  it('allows a module when the role holds its permission', () => {
    setup(['clients.view']);
    expect(TestBed.runInInjectionContext(() => permissionGuard(route('clients.view'), state))).toBe(true);
  });

  it('blocks a module the role lacks', () => {
    const router = setup(['clients.view']);
    const result = TestBed.runInInjectionContext(() => permissionGuard(route('billing.view'), state)) as UrlTree;
    expect(router.serializeUrl(result)).toBe('/forbidden');
  });

  it('ignores an expired session', () => {
    localStorage.setItem('valupro.session', JSON.stringify({ token: 't', expiresAt: '2000-01-01T00:00:00Z', user: {} }));
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient()] });
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
  });
});

describe('navigation', () => {
  it('lists the 16 prototype modules with unique paths', () => {
    const paths = NAVIGATION.flatMap((g) => g.items).map((i) => i.path);
    expect(paths.length).toBe(16);
    expect(new Set(paths).size).toBe(16);
  });
});
