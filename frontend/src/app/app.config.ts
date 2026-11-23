import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, TitleStrategy, withComponentInputBinding } from '@angular/router';
import { FirmTitleStrategy } from './core/branding/firm-title.strategy';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { BrandingService } from './core/branding/branding.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useClass: FirmTitleStrategy },
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    // Firm colours and logo before first paint, so nothing flashes in the default colours.
    provideAppInitializer(() => inject(BrandingService).load()),
  ],
};
