import { HttpErrorResponse } from '@angular/common/http';
import { isAppError, toAppError } from './app-error';

function httpError(status: number, statusText: string, body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, statusText, error: body, url: '/api/quotes' });
}

describe('toAppError', () => {
  it('picks the first validation message out of a ValidationProblemDetails body', () => {
    const err = httpError(400, 'Bad Request', {
      title: 'One or more validation errors occurred.',
      errors: { author: ['Author is required.'], text: ['Text is required.'] },
    });

    expect(toAppError(err).message).toBe('Author is required.');
  });

  it('falls back to a Results.Conflict-style { message } body', () => {
    const err = httpError(409, 'Conflict', { message: 'An account with that email already exists.' });

    expect(toAppError(err).message).toBe('An account with that email already exists.');
  });

  it('falls back to the ProblemDetails title when there are no field errors', () => {
    const err = httpError(403, 'Forbidden', { title: 'You cannot edit this quote.' });

    expect(toAppError(err).message).toBe('You cannot edit this quote.');
  });

  it('falls back to a generic per-status message for a body-less 4xx', () => {
    const err = httpError(404, 'Not Found', null);

    expect(toAppError(err).message).toBe('That resource could not be found.');
  });

  it('carries status, statusText and the raw errors dictionary through untouched', () => {
    const err = httpError(400, 'Bad Request', { errors: { author: ['Author is required.'] } });
    const appError = toAppError(err);

    expect(appError.status).toBe(400);
    expect(appError.statusText).toBe('Bad Request');
    expect(appError.errors).toEqual({ author: ['Author is required.'] });
  });
});

describe('isAppError', () => {
  it('is true for a value shaped like an AppError', () => {
    expect(isAppError({ status: 400, message: 'nope' })).toBe(true);
  });

  it('is false for a real HttpErrorResponse, even though it also has a numeric status', () => {
    expect(isAppError(httpError(400, 'Bad Request', {}))).toBe(false);
  });

  it('is false for null, undefined, and non-error values', () => {
    expect(isAppError(null)).toBe(false);
    expect(isAppError(undefined)).toBe(false);
    expect(isAppError('boom')).toBe(false);
    expect(isAppError({ status: 400 })).toBe(false); // missing message
  });
});
