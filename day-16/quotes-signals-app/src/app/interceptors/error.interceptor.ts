import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { toAppError } from '../shared/app-error';

/**
 * Converts a 4xx HttpErrorResponse into a typed, already-friendly AppError
 * (see shared/app-error.ts) so callers don't each have to re-parse a
 * ProblemDetails/ValidationProblemDetails body themselves. Network failures
 * and 5xx are left as raw HttpErrorResponse — those are transport/server
 * problems, not something the request body caused, and retryInterceptor
 * still needs to see the real HttpErrorResponse to decide whether to retry.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
        return throwError(() => toAppError(error));
      }
      return throwError(() => error);
    }),
  );
