import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrlInterceptor } from './api-base-url.interceptor';

const DEPLOYED_ORIGIN = 'https://ai-quotes-func.azurewebsites.net';

function setHostname(hostname: string): void {
  Object.defineProperty(window, 'location', {
    value: { ...window.location, hostname },
    writable: true,
    configurable: true,
  });
}

describe('apiBaseUrlInterceptor', () => {
  const originalLocation = window.location;
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiBaseUrlInterceptor])), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    Object.defineProperty(window, 'location', { value: originalLocation, writable: true, configurable: true });
  });

  it('leaves /api/* requests untouched on localhost (the real ng serve dev-server proxy handles them)', async () => {
    setHostname('localhost');
    const promise = firstValueFrom(http.get('/api/quotes'));

    httpMock.expectOne('/api/quotes').flush([]);
    await expect(promise).resolves.toEqual([]);
  });

  it('rewrites /api/* requests to the deployed managed-identity proxy off localhost', async () => {
    setHostname('lemon-smoke-0e5e0530f.7.azurestaticapps.net');
    const promise = firstValueFrom(http.get('/api/quotes'));

    httpMock.expectOne(`${DEPLOYED_ORIGIN}/api/quotes`).flush([]);
    await expect(promise).resolves.toEqual([]);
  });

  it('leaves non-/api requests alone regardless of hostname', async () => {
    setHostname('lemon-smoke-0e5e0530f.7.azurestaticapps.net');
    const promise = firstValueFrom(http.get('/assets/config.json'));

    httpMock.expectOne('/assets/config.json').flush({});
    await expect(promise).resolves.toEqual({});
  });
});
