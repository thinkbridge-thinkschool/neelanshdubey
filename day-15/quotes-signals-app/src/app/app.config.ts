import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './interceptors/auth.interceptor';
import { errorInterceptor } from './interceptors/error.interceptor';
import { retryInterceptor } from './interceptors/retry.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Order = request-flow order: errorInterceptor is outermost so it maps
    // the *final* failure (after retries are exhausted); retryInterceptor
    // sits in the middle so it still sees the raw HttpErrorResponse from the
    // backend, not an already-mapped AppError; authInterceptor is innermost,
    // closest to the actual HTTP call.
    provideHttpClient(withInterceptors([errorInterceptor, retryInterceptor, authInterceptor])),
    provideRouter(routes),
  ]
};
