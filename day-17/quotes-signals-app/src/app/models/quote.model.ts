/**
 * QuotesApi is inconsistent about what it returns (verified against the
 * running API, not assumed): GET /api/quotes projects an anonymous object
 * with `ownerEmail`, while POST /api/quotes and GET /api/quotes/{id} return
 * the raw Quote entity instead, which has no `ownerEmail` at all. `ownerEmail`
 * is therefore optional here to reflect that real inconsistency rather than
 * paper over it.
 */
export interface Quote {
  id: number;
  author: string;
  text: string;
  createdAt: string;
  ownerId: number;
  ownerEmail?: string | null;
  /**
   * Only present on the GET /api/quotes/{id} raw-entity response (Quote.cs
   * has a public getter), never on the GET /api/quotes list projection.
   * In practice this is always false: DELETE /api/quotes/{id} hard-deletes
   * the row (QuoteRepository.DeleteAsync), and nothing ever calls
   * Quote.SoftDelete(). Kept optional and typed rather than silently
   * dropped, since it's still a real field on the wire.
   */
  isDeleted?: boolean;
}

/** Body shape for POST /api/quotes (QuotesApi.Models.CreateQuoteRequest). */
export interface CreateQuoteRequest {
  author: string;
  text: string;
}

/** Body shape for PUT /api/quotes/{id} (QuotesApi.Models.UpdateQuoteRequest) — same fields as create. */
export type UpdateQuoteRequest = CreateQuoteRequest;

/** The four mutually-exclusive states the quotes list can be in. */
export type ViewState = 'loading' | 'success' | 'empty' | 'error';
