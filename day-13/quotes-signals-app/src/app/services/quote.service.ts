import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { CreateQuoteRequest, Quote, UpdateQuoteRequest } from '../models/quote.model';
import { AuthService } from './auth.service';

/**
 * Talks to the real Day-5/QuotesApi. Requests go through the Angular dev
 * server's proxy (proxy.conf.json forwards /api/* to https://localhost:7210)
 * so the browser only ever sees same-origin http://localhost:4200 calls —
 * no CORS, no self-signed-cert warning in the browser.
 */
// The real API caps `size` at 100 per request (see QuoteEndpointExtensions.cs
// in Day-5/QuotesApi), so getAllQuotes() walks page=1,2,3… at that size and
// concatenates until a short page confirms there's no more.
const FETCH_ALL_BATCH_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class QuoteService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  getQuotes(page: number, size: number): Promise<Quote[]> {
    return firstValueFrom(
      this.http.get<Quote[]>('/api/quotes', { params: { page, size } })
    );
  }

  /**
   * Loads the entire collection by walking the API's own page/size
   * pagination internally. There's no dedicated backend endpoint for
   * "everything" or for search/quote-of-the-day, so the search screen
   * fetches this once and works with it locally.
   */
  async getAllQuotes(): Promise<Quote[]> {
    const collected: Quote[] = [];
    let page = 1;

    while (true) {
      const batch = await this.getQuotes(page, FETCH_ALL_BATCH_SIZE);
      collected.push(...batch);

      if (batch.length < FETCH_ALL_BATCH_SIZE) {
        return collected; // short page: this was the last one
      }

      page += 1;
    }
  }

  getQuote(id: number): Promise<Quote> {
    return firstValueFrom(this.http.get<Quote>(`/api/quotes/${id}`));
  }

  /**
   * Same request as getQuote(), returned as a cold Observable instead of a
   * Promise so a caller chaining it through switchMap (QuoteDetailComponent)
   * can cancel an in-flight request when a newer id is selected.
   */
  getQuoteById$(id: number): Observable<Quote> {
    return this.http.get<Quote>(`/api/quotes/${id}`);
  }

  createQuote(request: CreateQuoteRequest): Promise<Quote> {
    return firstValueFrom(
      this.http.post<Quote>('/api/quotes', request, {
        headers: this.auth.authHeaders(),
      })
    );
  }

  updateQuote(id: number, request: UpdateQuoteRequest): Promise<Quote> {
    return firstValueFrom(
      this.http.put<Quote>(`/api/quotes/${id}`, request, {
        headers: this.auth.authHeaders(),
      })
    );
  }

  deleteQuote(id: number): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`/api/quotes/${id}`, {
        headers: this.auth.authHeaders(),
      })
    );
  }
}
