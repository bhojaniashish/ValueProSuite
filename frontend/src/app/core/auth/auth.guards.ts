import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Sends signed-out visitors to the login page, remembering where they were going. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isAuthenticated()
    ? true
    : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Users given a temporary password must choose their own before using the app. */
export const passwordChangedGuard: CanActivateFn = () =>
  inject(AuthService).user()?.mustChangePassword ? inject(Router).createUrlTree(['/change-password']) : true;

/** Route data `permission` must be held by the user (from the role × module matrix). */
export const permissionGuard: CanActivateFn = (route) => {
  const permission = route.data['permission'] as string | undefined;
  if (!permission || inject(AuthService).hasPermission(permission)) return true;
  return inject(Router).createUrlTree(['/forbidden']);
};

/** Keeps signed-in users off the login page. */
export const guestGuard: CanActivateFn = () =>
  inject(AuthService).isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
