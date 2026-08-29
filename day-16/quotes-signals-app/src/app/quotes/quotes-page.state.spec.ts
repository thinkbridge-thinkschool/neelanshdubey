import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Quote } from '../models/quote.model';
import { QuotesPageState } from './quotes-page.state';

function makePage(startId: number, count: number): Quote[] {
  return Array.from({ length: count }, (_, i) => ({
    id: startId + i,
    author: `Author ${startId + i}`,
    text: `Text ${startId + i}`,
    createdAt: '2026-01-01',
    ownerId: 1,
  }));
}

describe('QuotesPageState', () => {
  let state: QuotesPageState;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), QuotesPageState],
    });
    httpMock = TestBed.inject(HttpTestingController);
    state = TestBed.inject(QuotesPageState);
  });

  afterEach(() => httpMock.verify());

  function expectPageRequest(page: number, size = 10) {
    return httpMock.expectOne(
      (r) => r.url === '/api/quotes' && r.params.get('page') === String(page) && r.params.get('size') === String(size),
    );
  }

  it('loads page 1 on construction', async () => {
    expect(state.loading()).toBe(true);
    expectPageRequest(1).flush(makePage(1, 10));
    await Promise.resolve();

    expect(state.page()).toBe(1);
    expect(state.quotes()).toHaveLength(10);
    expect(state.loading()).toBe(false);
  });

  it('hasNextPage is true for a full page and false for a short (final) page', async () => {
    expectPageRequest(1).flush(makePage(1, 10));
    await Promise.resolve();
    expect(state.hasNextPage()).toBe(true);

    state.next();
    expectPageRequest(2).flush(makePage(11, 6)); // short page: fewer than pageSize
    await Promise.resolve();

    expect(state.page()).toBe(2);
    expect(state.hasNextPage()).toBe(false);
  });

  it('prev() is a no-op on page 1, and next()/prev() are no-ops while a request is already in flight', async () => {
    expectPageRequest(1).flush(makePage(1, 10));
    await Promise.resolve();

    expect(state.hasPrevPage()).toBe(false);
    state.prev();
    httpMock.expectNone(() => true);

    state.next(); // starts a real request for page 2
    state.next(); // should be swallowed — a request for page 2 is already in flight
    const req = expectPageRequest(2);
    httpMock.expectNone(() => true);
    req.flush(makePage(11, 10));
    await Promise.resolve();

    expect(state.page()).toBe(2);
  });

  it('surfaces the real HTTP error message and clears quotes on failure', async () => {
    expectPageRequest(1).flush({ title: 'boom' }, { status: 500, statusText: 'Internal Server Error' });
    await Promise.resolve();

    expect(state.error()).toBe('500 Internal Server Error: boom');
    expect(state.quotes()).toEqual([]);
    expect(state.loading()).toBe(false);
  });

  it('recovers when the true last page happens to be an exact multiple of pageSize (API gives no total count)', async () => {
    expectPageRequest(1).flush(makePage(1, 10)); // full page — looks like more might follow
    await Promise.resolve();
    expect(state.hasNextPage()).toBe(true);

    state.next();
    expectPageRequest(2).flush(makePage(11, 10)); // ALSO full — but this is actually the real last page
    await Promise.resolve();
    expect(state.page()).toBe(2);
    expect(state.hasNextPage()).toBe(true); // false positive: looks like page 3 might exist

    state.next(); // the only way to find out is to ask
    expectPageRequest(3).flush([]); // real API: nothing left
    await Promise.resolve();

    // Must NOT land on a blank page 3 — stays on page 2's real content,
    // and now correctly knows there's no next page.
    expect(state.page()).toBe(2);
    expect(state.quotes()).toHaveLength(10);
    expect(state.quotes()[0].id).toBe(11);
    expect(state.hasNextPage()).toBe(false);
  });

  it('retry() reloads the current page', async () => {
    expectPageRequest(1).flush({ title: 'boom' }, { status: 500, statusText: 'Internal Server Error' });
    await Promise.resolve();
    expect(state.error()).toBeTruthy();

    state.retry();
    expectPageRequest(1).flush(makePage(1, 10));
    await Promise.resolve();

    expect(state.error()).toBeNull();
    expect(state.page()).toBe(1);
  });

  it('ignores a stale response that resolves after a newer request has already won (out-of-order guard)', async () => {
    expectPageRequest(1).flush(makePage(1, 10));
    await Promise.resolve();

    // Directly drive two overlapping loads for different target pages —
    // the private loadPage() is what next()/prev()/retry() all funnel
    // through, and its staleness guard must hold regardless of which
    // public method triggered it.
    const loadPage = (state as unknown as { loadPage(page: number): Promise<void> }).loadPage.bind(state);
    const slowFirstCall = loadPage(2);
    const fastSecondCall = loadPage(3);

    // Resolve them OUT OF ORDER: page 3 (issued second) responds first,
    // page 2 (issued first) responds last — simulating a slow first
    // request that a user has already moved past.
    expectPageRequest(3).flush(makePage(21, 10));
    await fastSecondCall;
    expect(state.page()).toBe(3);

    expectPageRequest(2).flush(makePage(11, 10));
    await slowFirstCall;

    // The late page-2 response must NOT have clobbered page 3.
    expect(state.page()).toBe(3);
    expect(state.quotes()[0].id).toBe(21);
  });
});
