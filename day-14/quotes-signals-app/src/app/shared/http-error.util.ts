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

/**
 * Renders the real HTTP status and server-provided message, for surfaces
 * (quotes list/detail) that intentionally show the raw failure instead of a
 * generic one — there's no sensitive data in these responses since the
 * quotes collection is public read.
 */
export function toRawErrorMessage(err: unknown): string {
  if (!(err instanceof HttpErrorResponse)) {
    return 'Something went wrong.';
  }

  if (err.status === 0) {
    return 'Unable to reach the API. Please check that it is running.';
  }

  // Results.NotFound()/Forbid()/Unauthorized() send an empty body (verified
  // against the live API: content-length 0), so there's often no `title` to
  // show. Falling back to err.message here would print Angular's synthesized
  // "Http failure response for <url>: 404 Not Found" — a garbled duplicate of
  // the status line already shown. Drop the trailing part instead.
  const body = err.error as { title?: string } | null;
  const statusLine = `${err.status} ${err.statusText}`;

  return body?.title ? `${statusLine}: ${body.title}` : statusLine;
}
