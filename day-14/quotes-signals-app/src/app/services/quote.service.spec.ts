import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { QuoteService } from './quote.service';

async function signIn(auth: AuthService, httpMock: HttpTestingController): Promise<void> {
  const promise = auth.login('user@example.com', 'password');
  httpMock.expectOne('/api/auth/login').flush({ accessToken: 'abc123', refreshToken: 'r' });
  await promise;
}

describe('QuoteService', () => {
  let service: QuoteService;
  let auth: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(QuoteService);
    auth = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('requests a page of quotes with page/size query params', async () => {
    const promise = service.getQuotes(2, 5);

    const req = httpMock.expectOne(
      (r) => r.url === '/api/quotes' && r.params.get('page') === '2' && r.params.get('size') === '5',
    );
    expect(req.request.method).toBe('GET');

    req.flush([]);

    await expect(promise).resolves.toEqual([]);
  });

  it('fetches a single quote by id', async () => {
    const promise = service.getQuote(7);

    const req = httpMock.expectOne('/api/quotes/7');
    expect(req.request.method).toBe('GET');

    req.flush({ id: 7, author: 'A', text: 'T', createdAt: '2026-01-01', ownerId: 1, ownerEmail: null });

    await expect(promise).resolves.toMatchObject({ id: 7 });
  });

  it('attaches the bearer token when creating a quote', async () => {
    await signIn(auth, httpMock);

    const promise = service.createQuote({ author: 'Ada', text: 'Hello' });

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ author: 'Ada', text: 'Hello' });
    expect(req.request.headers.get('Authorization')).toBe('Bearer abc123');

    req.flush({ id: 1, author: 'Ada', text: 'Hello', createdAt: '2026-01-01', ownerId: 1, ownerEmail: null });

    await expect(promise).resolves.toMatchObject({ id: 1 });
  });

  it('rejects with an HttpErrorResponse when the API rejects the create request', async () => {
    const promise = service.createQuote({ author: '', text: '' });

    const req = httpMock.expectOne('/api/quotes');
    req.flush('Validation failed', { status: 400, statusText: 'Bad Request' });

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
  });

  it('sends a PUT request with the bearer token when updating a quote', async () => {
    await signIn(auth, httpMock);

    const promise = service.updateQuote(1, { author: 'Ada', text: 'Updated' });

    const req = httpMock.expectOne('/api/quotes/1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ author: 'Ada', text: 'Updated' });
    expect(req.request.headers.get('Authorization')).toBe('Bearer abc123');

    req.flush({ id: 1, author: 'Ada', text: 'Updated', createdAt: '2026-01-01', ownerId: 1, ownerEmail: null });

    await expect(promise).resolves.toMatchObject({ author: 'Ada', text: 'Updated' });
  });

  it('rejects with an HttpErrorResponse when update fails', async () => {
    const promise = service.updateQuote(1, { author: '', text: '' });

    const req = httpMock.expectOne('/api/quotes/1');
    req.flush('Validation failed', { status: 400, statusText: 'Bad Request' });

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
  });

  it('sends a DELETE request with the bearer token', async () => {
    await signIn(auth, httpMock);

    const promise = service.deleteQuote(3);

    const req = httpMock.expectOne('/api/quotes/3');
    expect(req.request.method).toBe('DELETE');
    expect(req.request.headers.get('Authorization')).toBe('Bearer abc123');

    req.flush(null);

    await expect(promise).resolves.toBeNull();
  });

  it('rejects with an HttpErrorResponse when delete fails', async () => {
    const promise = service.deleteQuote(999);

    const req = httpMock.expectOne('/api/quotes/999');
    req.flush('Not found', { status: 404, statusText: 'Not Found' });

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
  });
});
