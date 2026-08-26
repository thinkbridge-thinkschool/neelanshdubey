import { HttpErrorResponse } from '@angular/common/http';

interface ProblemDetailsBody {
  title?: string;
  message?: string;
  errors?: Record<string, string[]>;
}

/**
 * Typed, UI-ready shape for a 4xx failure. Produced by errorInterceptor so
 * components never have to re-parse ProblemDetails/ValidationProblemDetails
 * (Results.ValidationProblem) or the ad-hoc `{ message }` bodies
 * (Results.Conflict) that QuotesApi also returns — see AuthEndpointExtensions.cs.
 */
export interface AppError {
  readonly status: number;
  readonly statusText: string;
  readonly message: string;
  readonly title?: string;
  readonly errors?: Record<string, string[]>;
}

export function isAppError(value: unknown): value is AppError {
  return (
    typeof value === 'object' &&
    value !== null &&
    !(value instanceof HttpErrorResponse) &&
    typeof (value as AppError).status === 'number' &&
    typeof (value as AppError).message === 'string'
  );
}

/** Kept 4xx-only and deliberately small: errorInterceptor never calls this for network failures or 5xx. */
function fallbackMessage(status: number): string {
  switch (status) {
    case 400:
      return 'The server rejected this request as invalid.';
    case 401:
    case 403:
      return 'You are not authorized to do that.';
    case 404:
      return 'That resource could not be found.';
    default:
      return 'The server rejected this request.';
  }
}

export function toAppError(err: HttpErrorResponse): AppError {
  const body = (err.error ?? null) as ProblemDetailsBody | null;
  const firstValidationMessage = body?.errors ? Object.values(body.errors)[0]?.[0] : undefined;

  return {
    status: err.status,
    statusText: err.statusText,
    title: body?.title,
    errors: body?.errors,
    message: firstValidationMessage ?? body?.message ?? body?.title ?? fallbackMessage(err.status),
  };
}
