import { Component, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { EMPTY, catchError, switchMap, tap } from 'rxjs';
import { Quote } from '../models/quote.model';
import { toRawErrorMessage } from '../shared/http-error.util';
import { QuoteService } from '../services/quote.service';

@Component({
  selector: 'app-quote-detail',
  templateUrl: './quote-detail.component.html',
  styleUrl: './quote-detail.component.scss',
})
export class QuoteDetailComponent {
  private readonly quoteService = inject(QuoteService);

  readonly quoteId = input<number | null>(null);

  protected readonly detail = signal<Quote | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // switchMap cancels the previous getQuoteById$ subscription as soon as a
    // new id arrives, so clicking quote A then quickly clicking quote B can
    // never let A's late-arriving response overwrite B's in `detail`.
    toObservable(this.quoteId)
      .pipe(
        tap((id) => {
          this.error.set(null);
          this.detail.set(null);
          this.loading.set(id !== null);
        }),
        switchMap((id) => {
          if (id === null) {
            return EMPTY;
          }

          return this.quoteService.getQuoteById$(id).pipe(
            catchError((err) => {
              this.loading.set(false);
              this.error.set(toRawErrorMessage(err));
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((quote) => {
        this.loading.set(false);
        this.detail.set(quote);
      });
  }
}
