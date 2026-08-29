import { Injectable, computed, inject, signal } from '@angular/core';
import { Quote } from '../models/quote.model';
import { QuoteService } from '../services/quote.service';
import { toRawErrorMessage } from '../shared/http-error.util';

const PAGE_SIZE = 10;

/**
 * Signals-first state for one paginated browse of GET /api/quotes?page&size
 * (Day-5/QuotesApi, QuoteEndpointExtensions.cs). Provided on
 * QuotesListComponent (component-scoped, not root) so it's created fresh
 * and thrown away with the page — no cross-navigation leakage, no store
 * library. See ../../state-audit/README.md for why this is the right
 * amount of state management at this scale.
 */
@Injectable()
export class QuotesPageState {
  private readonly quoteService = inject(QuoteService);

  private readonly _page = signal(1);
  private readonly _quotes = signal<Quote[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);

  // A short page proves it was the last one, but a *full* page doesn't
  // prove there's a next one — if the real total happens to be an exact
  // multiple of PAGE_SIZE, the true last page still looks full (the API
  // returns no total count, see QuoteEndpointExtensions.cs). This is only
  // ever discovered by actually requesting the next page and getting
  // nothing back, at which point it records which page turned out to be
  // the real last one.
  private readonly _knownLastPage = signal<number | null>(null);

  readonly page = this._page.asReadonly();
  readonly quotes = this._quotes.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly pageSize = PAGE_SIZE;

  readonly hasNextPage = computed(
    () => this._quotes().length === PAGE_SIZE && this._page() !== this._knownLastPage(),
  );
  readonly hasPrevPage = computed(() => this._page() > 1);

  // Guards against out-of-order responses: whichever loadPage() call was
  // issued *last* is the only one allowed to write to state, even if an
  // earlier call's response arrives later (e.g. Next then Retry fired in
  // quick succession, and the Next response is slow). Without this, a
  // late-arriving stale response can silently clobber newer state.
  private latestRequestId = 0;

  constructor() {
    void this.loadPage(1);
  }

  next(): void {
    if (!this.hasNextPage() || this._loading()) return;
    void this.loadPage(this._page() + 1);
  }

  prev(): void {
    if (!this.hasPrevPage() || this._loading()) return;
    void this.loadPage(this._page() - 1);
  }

  retry(): void {
    void this.loadPage(this._page());
  }

  private async loadPage(page: number): Promise<void> {
    const requestId = ++this.latestRequestId;
    this._loading.set(true);
    this._error.set(null);

    try {
      const quotes = await this.quoteService.getQuotes(page, PAGE_SIZE);
      if (requestId !== this.latestRequestId) return; // superseded — drop it

      if (quotes.length === 0 && page > 1) {
        // Overshot: the page we were already showing was the real last
        // page, we just couldn't know that until this request came back
        // empty. Stay put rather than showing a dead "no quotes" screen.
        this._knownLastPage.set(page - 1);
        return;
      }

      this._quotes.set(quotes);
      this._page.set(page);
    } catch (err) {
      if (requestId !== this.latestRequestId) return;
      this._quotes.set([]);
      this._error.set(toRawErrorMessage(err));
    } finally {
      if (requestId === this.latestRequestId) this._loading.set(false);
    }
  }
}
