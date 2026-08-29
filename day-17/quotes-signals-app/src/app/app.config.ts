import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { routes } from './app.routes';
import { apiBaseUrlInterceptor } from './interceptors/api-base-url.interceptor';
import { authInterceptor } from './interceptors/auth.interceptor';
import { errorInterceptor } from './interceptors/error.interceptor';
import { retryInterceptor } from './interceptors/retry.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Order = request-flow order: apiBaseUrlInterceptor rewrites the target
    // origin first (a no-op locally); errorInterceptor is next-outermost so
    // it maps the *final* failure (after retries are exhausted);
    // retryInterceptor sits in the middle so it still sees the raw
    // HttpErrorResponse from the backend, not an already-mapped AppError;
    // authInterceptor is innermost, closest to the actual HTTP call.
    provideHttpClient(
      withInterceptors([apiBaseUrlInterceptor, errorInterceptor, retryInterceptor, authInterceptor]),
    ),
    // withComponentInputBinding: the ':id' route param below binds straight
    // to QuoteDetailPageComponent's `id` input, no ActivatedRoute needed.
    // withViewTransitions: wraps every navigation (quotes list -> quote
    // detail, and back) in the browser View Transitions API.
    provideRouter(routes, withComponentInputBinding(), withViewTransitions()),
  ]
};
