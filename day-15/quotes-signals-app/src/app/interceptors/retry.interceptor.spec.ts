import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { retryInterceptor } from './retry.interceptor';

describe('retryInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([retryInterceptor])), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    vi.useFakeTimers();
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('retries a GET with exponential backoff on a transient failure, then resolves', async () => {
    const promise = firstValueFrom(http.get('/api/quotes'));

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(300); // 1st retry delay: 300ms * 2^0

    httpMock.expectOne('/api/quotes').flush(null, { status: 0, statusText: 'Unknown Error' });
    await vi.advanceTimersByTimeAsync(600); // 2nd retry delay: 300ms * 2^1

    httpMock.expectOne('/api/quotes').flush([{ id: 1, author: 'A', text: 'T' }]);

    await expect(promise).resolves.toEqual([{ id: 1, author: 'A', text: 'T' }]);
  });

  it('gives up and rejects after exhausting retries on a persistent transient failure', async () => {
    const promise = firstValueFrom(http.get('/api/quotes'));
    promise.catch(() => {}); // keep the eventual rejection from surfacing as unhandled while attempts are in flight

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(300);

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(600);

    // 3rd and final attempt (1 initial + 2 retries) — still fails, no more retries left.
    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
    httpMock.expectNone(() => true);
  });

  it('does not retry a non-transient status like 404', async () => {
    const promise = firstValueFrom(http.get('/api/quotes/999'));

    httpMock.expectOne('/api/quotes/999').flush(null, { status: 404, statusText: 'Not Found' });

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
    httpMock.expectNone(() => true);
  });

  it('never retries a mutating request, even on a transient failure', async () => {
    const promise = firstValueFrom(http.post('/api/quotes', { author: 'A', text: 'T' }));

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
    httpMock.expectNone(() => true);
  });
});
