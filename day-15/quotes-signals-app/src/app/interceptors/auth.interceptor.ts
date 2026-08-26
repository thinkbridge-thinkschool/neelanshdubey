import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

/**
 * Attaches the signed-in user's bearer token to same-origin API calls, so
 * QuoteService (and anything else) no longer has to pass
 * `headers: auth.authHeaders()` into every mutating call by hand.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api')) {
    return next(req);
  }

  const token = inject(AuthService).authHeaders().get('Authorization');

  return next(token ? req.clone({ setHeaders: { Authorization: token } }) : req);
};
