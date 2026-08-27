import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Quote } from '../models/quote.model';
import { toRawErrorMessage } from '../shared/http-error.util';
import { QuoteService } from '../services/quote.service';

const LIST_PAGE_SIZE = 100;

@Component({
  selector: 'app-quotes-list',
  imports: [RouterLink],
  templateUrl: './quotes-list.component.html',
  styleUrl: './quotes-list.component.scss',
})
export class QuotesListComponent {
  private readonly quoteService = inject(QuoteService);

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.loadQuotes();
  }

  protected retry(): void {
    void this.loadQuotes();
  }

  private async loadQuotes(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const quotes = await this.quoteService.getQuotes(1, LIST_PAGE_SIZE);
      this.quotes.set(quotes);
    } catch (err) {
      this.quotes.set([]);
      this.error.set(toRawErrorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }
}
