import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { isAppError } from '../shared/app-error';
import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('maps a 400 ValidationProblemDetails to an AppError carrying the first field message', async () => {
    const promise = firstValueFrom(http.post('/api/quotes', { author: '', text: '' }));

    httpMock.expectOne('/api/quotes').flush(
      { title: 'One or more validation errors occurred.', status: 400, errors: { author: ['Author is required.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    const err = await promise.catch((e: unknown) => e);
    expect(isAppError(err)).toBe(true);
    expect(err).toMatchObject({ status: 400, message: 'Author is required.', errors: { author: ['Author is required.'] } });
  });

  it('maps a Results.Conflict-style { message } body to that message', async () => {
    const promise = firstValueFrom(http.post('/api/auth/register', { email: 'x', password: 'y' }));

    httpMock
      .expectOne('/api/auth/register')
      .flush({ message: 'An account with that email already exists.' }, { status: 409, statusText: 'Conflict' });

    const err = await promise.catch((e: unknown) => e);
    expect(err).toMatchObject({ status: 409, message: 'An account with that email already exists.' });
  });

  it('falls back to a friendly generic message for a bare 4xx with no body', async () => {
    const promise = firstValueFrom(http.get('/api/quotes/999'));

    httpMock.expectOne('/api/quotes/999').flush(null, { status: 404, statusText: 'Not Found' });

    const err = await promise.catch((e: unknown) => e);
    expect(err).toMatchObject({ status: 404, message: 'That resource could not be found.' });
  });

  it('leaves a 5xx as a raw HttpErrorResponse, unmapped', async () => {
    const promise = firstValueFrom(http.get('/api/quotes'));

    httpMock.expectOne('/api/quotes').flush(null, { status: 500, statusText: 'Internal Server Error' });

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
  });

  it('leaves a network failure (status 0) as a raw HttpErrorResponse, unmapped', async () => {
    const promise = firstValueFrom(http.get('/api/quotes'));

    httpMock.expectOne('/api/quotes').error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
  });
});
