import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';

const SESSION_KEY = 'quotes-app.session';

function seedSession(): void {
  localStorage.setItem(
    SESSION_KEY,
    JSON.stringify({ accessToken: 't', refreshToken: 'r', email: 'reader@example.com' }),
  );
}

describe('App routing', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('redirects the default route to /login when signed out', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');

    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('redirects /search to /login when signed out (authGuard)', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/search');

    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('redirects the default route to /search when signed in', async () => {
    seedSession();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([]);
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/search');
  });

  it('renders /search when signed in (authGuard allows it through)', async () => {
    seedSession();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/search');
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([]);
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/search');
    expect(harness.routeNativeElement?.querySelector('.search-shell')).toBeTruthy();
  });

  it('an unknown path (including the removed /museum) falls through to the default redirect', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/museum');

    expect(TestBed.inject(Router).url).toBe('/login'); // signed out, via the '**' -> '' -> homeRedirectGuard chain
  });
});
