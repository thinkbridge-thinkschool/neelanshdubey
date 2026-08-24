import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { BehaviorSubject, firstValueFrom, map } from 'rxjs';
import { toUserMessage } from '../shared/http-error.util';

interface TokenResponse {
  accessToken: string;
  refreshToken: string;
}

interface StoredSession {
  accessToken: string;
  refreshToken: string;
  email: string;
}

const STORAGE_KEY = 'quotes-app.session';

/** The JWT's own "sub" claim carries the user id — decoded client-side so the UI can tell which quotes belong to the signed-in user (no extra "whoami" endpoint exists). */
function decodeUserId(accessToken: string): number | null {
  try {
    const payload = accessToken.split('.')[1];
    const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as { sub?: string };
    return json.sub !== undefined ? Number(json.sub) : null;
  } catch {
    return null;
  }
}

function readStoredSession(): StoredSession | null {
  try {
    const fromLocal = localStorage.getItem(STORAGE_KEY);
    if (fromLocal) return JSON.parse(fromLocal) as StoredSession;

    const fromSession = sessionStorage.getItem(STORAGE_KEY);
    return fromSession ? (JSON.parse(fromSession) as StoredSession) : null;
  } catch {
    return null;
  }
}

/**
 * Real authentication against QuotesApi's own JWT endpoints
 * (AuthEndpointExtensions.cs) — no hardcoded credentials. Create/update/
 * delete all require the resulting Bearer token (RequireAuthorization() in
 * QuoteEndpointExtensions.cs); browsing the collection does not.
 *
 * The session itself is a BehaviorSubject persisted to localStorage or
 * sessionStorage (so a page refresh on /search doesn't bounce the user back
 * to /login) — bridged to signal() views below for signal-based consumers.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly session$ = new BehaviorSubject<StoredSession | null>(readStoredSession());

  /** RxJS-native view of the session, for reactive-forms-style consumers (LoginComponent, SearchComponent). */
  readonly session = this.session$.asObservable();

  readonly authError = signal<string | null>(null);
  readonly isAuthenticating = signal(false);

  readonly isAuthenticated = toSignal(this.session$.pipe(map((s) => s !== null)), {
    requireSync: true,
  });

  readonly email = toSignal(this.session$.pipe(map((s) => s?.email ?? null)), {
    requireSync: true,
  });

  readonly userId = toSignal(
    this.session$.pipe(map((s) => (s ? decodeUserId(s.accessToken) : null))),
    { requireSync: true },
  );

  async login(email: string, password: string, rememberMe = true): Promise<void> {
    this.isAuthenticating.set(true);
    this.authError.set(null);

    try {
      const response = await firstValueFrom(
        this.http.post<TokenResponse>('/api/auth/login', { email, password })
      );
      this.setSession({ ...response, email }, rememberMe);
    } catch (err) {
      this.authError.set(
        err instanceof HttpErrorResponse && err.status === 401
          ? 'Incorrect email or password.'
          : toUserMessage(err, 'Unable to sign in right now.')
      );
    } finally {
      this.isAuthenticating.set(false);
    }
  }

  async register(email: string, password: string): Promise<void> {
    this.isAuthenticating.set(true);
    this.authError.set(null);

    try {
      const response = await firstValueFrom(
        this.http.post<TokenResponse>('/api/auth/register', { email, password })
      );
      this.setSession({ ...response, email }, true);
    } catch (err) {
      this.authError.set(this.describeRegisterError(err));
    } finally {
      this.isAuthenticating.set(false);
    }
  }

  async logout(): Promise<void> {
    const refreshToken = this.session$.value?.refreshToken;
    this.clearSession();

    if (refreshToken) {
      try {
        await firstValueFrom(this.http.post('/api/auth/logout', { refreshToken }));
      } catch {
        // Best-effort server-side revoke; the client-side session is already cleared.
      }
    }
  }

  authHeaders(): HttpHeaders {
    const token = this.session$.value?.accessToken;
    return token ? new HttpHeaders({ Authorization: `Bearer ${token}` }) : new HttpHeaders();
  }

  /** "Remember me" decides which storage survives a closed browser vs just a refresh. */
  private setSession(session: StoredSession, rememberMe: boolean): void {
    const serialized = JSON.stringify(session);

    if (rememberMe) {
      localStorage.setItem(STORAGE_KEY, serialized);
      sessionStorage.removeItem(STORAGE_KEY);
    } else {
      sessionStorage.setItem(STORAGE_KEY, serialized);
      localStorage.removeItem(STORAGE_KEY);
    }

    this.session$.next(session);
  }

  private clearSession(): void {
    localStorage.removeItem(STORAGE_KEY);
    sessionStorage.removeItem(STORAGE_KEY);
    this.session$.next(null);
  }

  private describeRegisterError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 409) {
        return 'An account with that email already exists.';
      }

      if (err.status === 400) {
        const errors = (err.error as { errors?: Record<string, string[]> } | null)?.errors;
        const firstMessage = errors ? Object.values(errors)[0]?.[0] : undefined;
        return firstMessage ?? 'Please check your email and password.';
      }
    }

    return toUserMessage(err, 'Unable to create an account right now.');
  }
}
