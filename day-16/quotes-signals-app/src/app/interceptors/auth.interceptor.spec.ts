import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let auth: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    auth = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('does not attach an Authorization header when signed out', async () => {
    void firstValueFrom(http.get('/api/quotes'));

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('attaches the bearer token to /api requests once signed in', async () => {
    const loginPromise = auth.login('user@example.com', 'password');
    httpMock.expectOne('/api/auth/login').flush({ accessToken: 'abc123', refreshToken: 'r' });
    await loginPromise;

    void firstValueFrom(http.post('/api/quotes', { author: 'A', text: 'T' }));

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.headers.get('Authorization')).toBe('Bearer abc123');
    req.flush({});
  });

  it('leaves requests to other origins untouched even when signed in', async () => {
    const loginPromise = auth.login('user@example.com', 'password');
    httpMock.expectOne('/api/auth/login').flush({ accessToken: 'abc123', refreshToken: 'r' });
    await loginPromise;

    void firstValueFrom(http.get('https://example.com/unrelated'));

    const req = httpMock.expectOne('https://example.com/unrelated');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });
});
