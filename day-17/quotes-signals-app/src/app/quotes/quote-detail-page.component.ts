import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuoteDetailComponent } from './quote-detail.component';

/**
 * Routed at 'quotes/:id' (lazy-loaded, see app.routes.ts). The `id` input
 * is bound straight from the route param by withComponentInputBinding()
 * (app.config.ts), so it always arrives as the raw string segment from the
 * URL — parsed to a number here before handing it to QuoteDetailComponent,
 * which does the actual fetch.
 */
@Component({
  selector: 'app-quote-detail-page',
  imports: [RouterLink, QuoteDetailComponent],
  templateUrl: './quote-detail-page.component.html',
  styleUrl: './quote-detail-page.component.scss',
})
export class QuoteDetailPageComponent {
  readonly id = input.required<string>();

  protected readonly quoteId = computed(() => {
    const parsed = Number(this.id());
    return Number.isInteger(parsed) ? parsed : null;
  });
}
