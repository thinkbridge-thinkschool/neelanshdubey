import { HttpErrorResponse } from '@angular/common/http';

/**
 * Translates a failed HTTP call into a short, user-safe message.
 * Never surfaces raw exception text/stack traces to the UI.
 */
export function toUserMessage(err: unknown, fallback: string): string {
  if (!(err instanceof HttpErrorResponse)) {
    return fallback;
  }

  if (err.status === 0) {
    return 'Unable to reach the API. Please check that it is running.';
  }

  switch (err.status) {
    case 400:
      return 'The server rejected this request as invalid.';
    case 401:
    case 403:
      return 'You are not authorized to do that.';
    case 404:
      return 'That quote could not be found.';
    case 500:
    case 502:
    case 503:
      return 'The server ran into a problem. Please try again shortly.';
    default:
      return fallback;
  }
}
