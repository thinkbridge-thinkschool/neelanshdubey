import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { LoginComponent } from './login.component';

const SESSION_KEY = 'quotes-app.session';

/** Protected members read via a typed harness cast — see other spec files in this project for the same convention. */
interface Harness {
  form: FormGroup<{
    email: import('@angular/forms').FormControl<string>;
    password: import('@angular/forms').FormControl<string>;
    rememberMe: import('@angular/forms').FormControl<boolean>;
  }>;
  authError: () => string | null;
  authenticating: () => boolean;
  forgotPasswordNotice: () => string | null;
  mode: () => 'login' | 'register';
  passwordMinLength: () => number;
  isInvalid: (name: 'email' | 'password') => boolean;
  switchMode: (mode: 'login' | 'register') => void;
  onSubmit: () => void;
  onForgotPassword: () => void;
}

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let harness: Harness;
  let httpMock: HttpTestingController;
  let router: Router;
  let navigateSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideZonelessChangeDetection(),
        // errorInterceptor is what turns a raw 401/409 into the AppError that
        // AuthService branches on for its friendly messages — real app.config.ts wiring.
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    // provideRouter([]) has no real routes, so every test mocks navigation —
    // otherwise a real navigateByUrl('/search') rejects with "no matching route".
    navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(LoginComponent);
    harness = fixture.componentInstance as unknown as Harness;
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('does not call the API when the form is invalid', () => {
    harness.onSubmit();
    httpMock.expectNone(() => true);
    expect(harness.form.touched).toBe(true); // markAllAsTouched surfaces the field errors
  });

  it('flags an invalid email and a too-short password once touched', () => {
    harness.form.controls.email.setValue('not-an-email');
    harness.form.controls.email.markAsTouched();
    harness.form.controls.password.setValue('short');
    harness.form.controls.password.markAsTouched();

    expect(harness.isInvalid('email')).toBe(true);
    expect(harness.isInvalid('password')).toBe(true);
  });

  it('logs in against the real API and navigates to /search on success', async () => {
    harness.form.setValue({ email: 'reader@example.com', password: 'password1', rememberMe: true });
    harness.onSubmit();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.body).toEqual({ email: 'reader@example.com', password: 'password1' });
    req.flush({ accessToken: 'tok', refreshToken: 'ref' });
    await fixture.whenStable();

    expect(navigateSpy).toHaveBeenCalledWith('/search');
    expect(localStorage.getItem(SESSION_KEY)).toContain('reader@example.com');
  });

  it('stores the session in sessionStorage (not localStorage) when "Remember me" is unchecked', async () => {
    harness.form.setValue({ email: 'reader@example.com', password: 'password1', rememberMe: false });
    harness.onSubmit();

    httpMock.expectOne('/api/auth/login').flush({ accessToken: 'tok', refreshToken: 'ref' });
    await fixture.whenStable();

    expect(sessionStorage.getItem(SESSION_KEY)).toContain('reader@example.com');
    expect(localStorage.getItem(SESSION_KEY)).toBeNull();
  });

  it('shows an inline error banner on a 401 and does not navigate', async () => {
    harness.form.setValue({ email: 'reader@example.com', password: 'wrongpass', rememberMe: true });
    harness.onSubmit();

    httpMock.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();

    expect(harness.authError()).toBe('Incorrect email or password.');
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('shows a "not available yet" notice for forgot password without calling the API', () => {
    harness.onForgotPassword();
    httpMock.expectNone(() => true);
    expect(harness.forgotPasswordNotice()).toBeTruthy();
  });

  it('switching to register mode raises the password minimum to match the backend (8 chars)', () => {
    expect(harness.passwordMinLength()).toBe(6);

    harness.switchMode('register');

    expect(harness.mode()).toBe('register');
    expect(harness.passwordMinLength()).toBe(8);

    harness.form.controls.password.setValue('short7x');
    harness.form.controls.password.markAsTouched();
    expect(harness.isInvalid('password')).toBe(true); // 7 chars, still short of the register minimum

    harness.form.controls.password.setValue('longenough');
    expect(harness.isInvalid('password')).toBe(false);
  });

  it('creates a real account via POST /api/auth/register and navigates to /search', async () => {
    harness.switchMode('register');
    harness.form.setValue({ email: 'new@example.com', password: 'longenough', rememberMe: true });
    harness.onSubmit();

    const req = httpMock.expectOne('/api/auth/register');
    expect(req.request.body).toEqual({ email: 'new@example.com', password: 'longenough' });
    req.flush({ accessToken: 'tok', refreshToken: 'ref' });
    await fixture.whenStable();

    expect(navigateSpy).toHaveBeenCalledWith('/search');
    expect(localStorage.getItem(SESSION_KEY)).toContain('new@example.com');
  });

  it('shows a conflict error on register and does not navigate', async () => {
    harness.switchMode('register');
    harness.form.setValue({ email: 'test@example.com', password: 'longenough', rememberMe: true });
    harness.onSubmit();

    // Real body shape from AuthEndpointExtensions.cs: Results.Conflict(new { message = "..." }).
    httpMock
      .expectOne('/api/auth/register')
      .flush(
        { message: 'An account with that email already exists.' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();

    expect(harness.authError()).toBe('An account with that email already exists.');
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
