import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { FormControl } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Quote } from '../models/quote.model';
import { SearchComponent } from './search.component';

const SESSION_KEY = 'quotes-app.session';

/** Protected members read via a typed harness cast — see other spec files in this project for the same convention. */
interface Harness {
  quotes: () => Quote[];
  loading: () => boolean;
  error: () => string | null;
  activeFilter: () => 'all' | 'author' | 'tag' | 'favorites';
  filteredQuotes: () => Quote[];
  resultCount: () => number;
  quoteOfTheDay: () => Quote | null;
  userInitials: () => string;
  favoriteIds: () => ReadonlySet<number>;
  searchControl: FormControl<string>;
  setFilter: (filter: 'all' | 'author' | 'tag' | 'favorites') => void;
  toggleFavorite: (id: number, event: Event) => void;
  retry: () => void;
  logout: () => Promise<void>;
  isAddModalOpen: () => boolean;
  newAuthor: () => string;
  newText: () => string;
  addSubmitting: () => boolean;
  addError: () => string | null;
  isAddFormValid: () => boolean;
  openAddModal: () => void;
  closeAddModal: () => void;
  onNewAuthorInput: (e: Event) => void;
  onNewTextInput: (e: Event) => void;
  onAddSubmit: (e: Event) => void;
  canDelete: (quote: Quote) => boolean;
  deleteConfirmId: () => number | null;
  deleteError: () => string | null;
  openDeleteConfirm: (id: number) => void;
  cancelDeleteConfirm: () => void;
  isDeleting: (id: number) => boolean;
  confirmDelete: () => Promise<void>;
  toastMessage: () => string | null;
}

/** A minimal JWT shape (no real signature) — decodeUserId() in AuthService only reads the payload's "sub" claim. */
function fakeJwt(sub: string): string {
  return `header.${btoa(JSON.stringify({ sub }))}.signature`;
}

function inputEvent(value: string): Event {
  const input = document.createElement('input');
  input.value = value;
  return { target: input } as unknown as Event;
}

function submitEvent(): Event {
  return { preventDefault: () => {} } as unknown as Event;
}

const QUOTE_A: Quote = {
  id: 1,
  author: 'Ada Lovelace',
  text: 'That brain of mine is something more than merely mortal.',
  createdAt: '2026-01-01T00:00:00Z',
  ownerId: 1,
};

const QUOTE_B: Quote = {
  id: 2,
  author: 'Grace Hopper',
  text: 'It is easier to ask forgiveness than permission.',
  createdAt: '2026-01-02T00:00:00Z',
  ownerId: 1,
};

function stopEvent(): Event {
  return { stopPropagation: () => {} } as unknown as Event;
}

/**
 * QuoteService.getAllQuotes() adds an extra await hop (component -> service
 * method -> getQuotes -> firstValueFrom) beyond what a single
 * `fixture.whenStable()` reliably drains in this zoneless test setup, so
 * settle a few extra microtask/stability rounds after flushing its request.
 */
async function settle(fixture: ComponentFixture<SearchComponent>): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await fixture.whenStable();
    await Promise.resolve();
  }
}

describe('SearchComponent', () => {
  let fixture: ComponentFixture<SearchComponent>;
  let harness: Harness;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ accessToken: fakeJwt('1'), refreshToken: 'ref', email: 'reader@example.com' }),
    );

    await TestBed.configureTestingModule({
      imports: [SearchComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SearchComponent);
    harness = fixture.componentInstance as unknown as Harness;
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the collection on init via the real API', async () => {
    expect(harness.loading()).toBe(true);

    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A, QUOTE_B]);
    await settle(fixture);

    expect(harness.loading()).toBe(false);
    expect(harness.quotes()).toEqual([QUOTE_A, QUOTE_B]);
    expect(harness.resultCount()).toBe(2);
    expect(harness.quoteOfTheDay()).not.toBeNull();
  });

  it('surfaces a load failure without exposing the raw error', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush('boom', { status: 500, statusText: 'Server Error' });
    await settle(fixture);

    expect(harness.error()).toBeTruthy();
    expect(harness.error()).not.toContain('boom');

    harness.retry();
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A]);
    await settle(fixture);

    expect(harness.error()).toBeNull();
    expect(harness.quotes()).toEqual([QUOTE_A]);
  });

  it('derives sensible initials from the signed-in email', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([]);
    await settle(fixture);

    expect(harness.userInitials()).toBe('RE');
  });

  it('debounces the search input and filters text or author in "all" mode', async () => {
    // Settle the initial load with REAL timers first — fixture.whenStable()
    // relies on real timers internally, so it must not run once fake timers
    // are active (that's what the second test's "By author" case needs too).
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A, QUOTE_B]);
    await settle(fixture);

    vi.useFakeTimers();
    try {
      harness.searchControl.setValue('hopper');
      expect(harness.filteredQuotes()).toEqual([QUOTE_A, QUOTE_B]); // not yet — debounce hasn't elapsed

      // toSignal's subscription updates the signal synchronously when the
      // debounced value emits, so advancing the timer is enough — no
      // further await/whenStable needed to observe it.
      await vi.advanceTimersByTimeAsync(300);

      expect(harness.filteredQuotes()).toEqual([QUOTE_B]);
    } finally {
      vi.useRealTimers();
    }
  });

  it('narrows matches to the author field in "By author" mode', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A, QUOTE_B]);
    await settle(fixture);

    vi.useFakeTimers();
    try {
      harness.setFilter('author');
      harness.searchControl.setValue('forgiveness'); // in the text, not the author
      await vi.advanceTimersByTimeAsync(300);

      expect(harness.filteredQuotes()).toEqual([]);
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows no results for "By tag" since this dataset has no tag data', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A, QUOTE_B]);
    await settle(fixture);

    harness.setFilter('tag');
    expect(harness.filteredQuotes()).toEqual([]);
  });

  it('toggles and persists a favorite, and the Favorites filter narrows to it', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A, QUOTE_B]);
    await settle(fixture);

    harness.toggleFavorite(QUOTE_B.id, stopEvent());
    expect(harness.favoriteIds().has(QUOTE_B.id)).toBe(true);
    expect(localStorage.getItem('quotes-app.favorites.reader@example.com')).toContain('2');

    harness.setFilter('favorites');
    expect(harness.filteredQuotes()).toEqual([QUOTE_B]);

    harness.toggleFavorite(QUOTE_B.id, stopEvent());
    expect(harness.filteredQuotes()).toEqual([]);
  });

  it('adds a quote via the real API and appends it to the grid', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A]);
    await settle(fixture);

    harness.openAddModal();
    expect(harness.isAddModalOpen()).toBe(true);

    harness.onNewAuthorInput(inputEvent('Grace Hopper'));
    harness.onNewTextInput(inputEvent('It is easier to ask forgiveness than permission.'));
    harness.onAddSubmit(submitEvent());

    const req = httpMock.expectOne((r) => r.url === '/api/quotes' && r.method === 'POST');
    expect(req.request.body).toEqual({
      author: 'Grace Hopper',
      text: 'It is easier to ask forgiveness than permission.',
    });
    req.flush(QUOTE_B);
    await settle(fixture);

    expect(harness.isAddModalOpen()).toBe(false);
    expect(harness.quotes()).toEqual([QUOTE_A, QUOTE_B]);
    expect(harness.toastMessage()).toBeTruthy();
  });

  it('rejects an empty add form without calling the API', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([]);
    await settle(fixture);

    harness.openAddModal();
    harness.onAddSubmit(submitEvent());

    httpMock.expectNone((r) => r.url === '/api/quotes' && r.method === 'POST');
    expect(harness.isAddFormValid()).toBe(false);
  });

  it('shows Delete only for quotes the signed-in user owns', async () => {
    const otherUsersQuote: Quote = { ...QUOTE_B, ownerId: 99 };
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A, otherUsersQuote]);
    await settle(fixture);

    expect(harness.canDelete(QUOTE_A)).toBe(true); // ownerId 1 matches the fake JWT's sub
    expect(harness.canDelete(otherUsersQuote)).toBe(false);
  });

  it('deletes an owned quote via the real API after confirmation', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A, QUOTE_B]);
    await settle(fixture);

    harness.openDeleteConfirm(QUOTE_A.id);
    expect(harness.deleteConfirmId()).toBe(QUOTE_A.id);

    const deletePromise = harness.confirmDelete();
    const req = httpMock.expectOne((r) => r.url === '/api/quotes/1' && r.method === 'DELETE');
    req.flush(null);
    await deletePromise;

    expect(harness.quotes()).toEqual([QUOTE_B]);
    expect(harness.deleteConfirmId()).toBeNull();
  });

  it('cancelDeleteConfirm closes the confirmation without calling the API', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A]);
    await settle(fixture);

    harness.openDeleteConfirm(QUOTE_A.id);
    harness.cancelDeleteConfirm();

    httpMock.expectNone((r) => r.method === 'DELETE');
    expect(harness.deleteConfirmId()).toBeNull();
    expect(harness.quotes()).toEqual([QUOTE_A]);
  });

  it('surfaces a 403 from deleting a quote you do not own without removing it', async () => {
    httpMock.expectOne((r) => r.url === '/api/quotes').flush([QUOTE_A]);
    await settle(fixture);

    harness.openDeleteConfirm(QUOTE_A.id);
    const deletePromise = harness.confirmDelete();
    httpMock.expectOne('/api/quotes/1').flush(null, { status: 403, statusText: 'Forbidden' });
    await deletePromise;

    expect(harness.deleteError()).toBeTruthy();
    expect(harness.quotes()).toEqual([QUOTE_A]); // still there — the delete failed
  });

  it('logs out against the real API and navigates to /login', async () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    httpMock.expectOne((r) => r.url === '/api/quotes').flush([]);
    await settle(fixture);

    const logoutPromise = harness.logout();
    httpMock.expectOne('/api/auth/logout').flush(null);
    await logoutPromise;

    expect(navigateSpy).toHaveBeenCalledWith('/login');
    expect(localStorage.getItem(SESSION_KEY)).toBeNull();
  });
});
