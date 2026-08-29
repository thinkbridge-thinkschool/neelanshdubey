import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, throwError, timer } from 'rxjs';

const MAX_RETRIES = 2;
const BASE_DELAY_MS = 300;

// Only failures that are plausibly transient (network blip, upstream/proxy
// hiccup) are worth retrying. A bare 500 from this API comes from an
// unhandled DomainException (see ExceptionMiddleware.cs) — retrying it just
// re-triggers the same bug on a delay, so 500 is deliberately excluded.
const TRANSIENT_STATUSES = new Set([0, 502, 503, 504]);

function isTransient(error: unknown): error is HttpErrorResponse {
  return error instanceof HttpErrorResponse && TRANSIENT_STATUSES.has(error.status);
}

/**
 * Retries idempotent GET requests with exponential backoff on a transient
 * failure. Mutating requests (POST/PUT/DELETE) are never auto-retried here —
 * replaying them safely would need a dedicated idempotency key the API
 * doesn't support.
 */
export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error, retryCount) =>
        isTransient(error) ? timer(BASE_DELAY_MS * 2 ** (retryCount - 1)) : throwError(() => error),
    }),
  );
};
