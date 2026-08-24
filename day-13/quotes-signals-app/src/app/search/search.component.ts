import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { Quote } from '../models/quote.model';
import { toUserMessage } from '../shared/http-error.util';
import { validateQuoteFields } from '../shared/quote-validation.util';
import { AuthService } from '../services/auth.service';
import { QuoteService } from '../services/quote.service';

type FilterKey = 'all' | 'author' | 'tag' | 'favorites';

const FAVORITES_KEY_PREFIX = 'quotes-app.favorites.';

function readFavorites(email: string | null): ReadonlySet<number> {
  if (!email) return new Set();

  try {
    const raw = localStorage.getItem(FAVORITES_KEY_PREFIX + email);
    return raw ? new Set(JSON.parse(raw) as number[]) : new Set();
  } catch {
    return new Set();
  }
}

@Component({
  selector: 'app-search',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './search.component.html',
  styleUrl: './search.component.scss',
})
export class SearchComponent {
  private readonly quoteService = inject(QuoteService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);

  protected readonly filters: ReadonlyArray<{ key: FilterKey; label: string }> = [
    { key: 'all', label: 'All quotes' },
    { key: 'author', label: 'By author' },
    { key: 'tag', label: 'By tag' },
    { key: 'favorites', label: 'Favorites' },
  ];

  protected readonly skeletonCards = [0, 1, 2, 3, 4, 5];

  // --- collection state ---------------------------------------------------
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly activeFilter = signal<FilterKey>('all');
  protected readonly favoriteIds = signal<ReadonlySet<number>>(new Set());

  // --- add-quote modal state -----------------------------------------------
  protected readonly isAddModalOpen = signal(false);
  protected readonly newAuthor = signal('');
  protected readonly newText = signal('');
  protected readonly addSubmitting = signal(false);
  protected readonly addError = signal<string | null>(null);

  // --- delete state ---------------------------------------------------------
  protected readonly deleteConfirmId = signal<number | null>(null);
  protected readonly deletingIds = signal<ReadonlySet<number>>(new Set());
  protected readonly deleteError = signal<string | null>(null);

  // --- transient feedback -----------------------------------------------------
  protected readonly toastMessage = signal<string | null>(null);

  // GET /api/quotes/search?q= doesn't exist on the real backend, so the
  // search box filters the already-loaded collection client-side. The
  // debounce/distinctUntilChanged the spec asked for still earns its keep
  // here — without it, every keystroke would re-run the filter computation.
  protected readonly searchControl = new FormControl('', { nonNullable: true });
  private readonly searchTerm = toSignal(
    this.searchControl.valueChanges.pipe(debounceTime(300), distinctUntilChanged()),
    { initialValue: '' },
  );

  // --- derived state ------------------------------------------------------
  protected readonly filteredQuotes = computed(() => {
    const filter = this.activeFilter();

    if (filter === 'tag') {
      return []; // this dataset has no tag data — see the empty-state message
    }

    let items = this.quotes();

    if (filter === 'favorites') {
      const favorites = this.favoriteIds();
      items = items.filter((q) => favorites.has(q.id));
    }

    const term = this.searchTerm().trim().toLowerCase();
    if (!term) {
      return items;
    }

    return items.filter((q) =>
      filter === 'author'
        ? q.author.toLowerCase().includes(term)
        : q.text.toLowerCase().includes(term) || q.author.toLowerCase().includes(term),
    );
  });

  protected readonly resultCount = computed(() => this.filteredQuotes().length);

  // A deterministic, real quote — not a placeholder — chosen by day of year
  // so it's stable all day and changes daily. GET /api/quotes/daily doesn't
  // exist on the real backend, so this picks from the collection already
  // fetched instead of calling a nonexistent endpoint.
  private readonly dayOfEpoch = Math.floor(Date.now() / 86_400_000);
  protected readonly quoteOfTheDay = computed<Quote | null>(() => {
    const items = this.quotes();
    return items.length > 0 ? items[this.dayOfEpoch % items.length] : null;
  });

  protected readonly addFormErrors = computed(() => validateQuoteFields(this.newAuthor(), this.newText()));
  protected readonly isAddFormValid = computed(() => this.addFormErrors().length === 0);

  protected readonly deleteConfirmQuote = computed<Quote | null>(() => {
    const id = this.deleteConfirmId();
    return id === null ? null : this.quotes().find((q) => q.id === id) ?? null;
  });

  protected readonly userInitials = computed(() => {
    const email = this.auth.email();
    if (!email) return '?';

    const localPart = email.split('@')[0];
    const segments = localPart.split(/[._-]/).filter(Boolean);

    return segments.length >= 2
      ? (segments[0][0] + segments[1][0]).toUpperCase()
      : localPart.slice(0, 2).toUpperCase();
  });

  constructor() {
    void this.loadQuotes();

    // Reacts to the signed-in user changing by reloading that user's own
    // locally-stored favorites — a genuine side effect (reading/switching
    // localStorage state), not a value computed() could derive.
    effect(() => {
      this.favoriteIds.set(readFavorites(this.auth.email()));
    });

    // Auto-dismisses the transient toast a few seconds after it appears —
    // a scheduled side effect, which computed() cannot express.
    effect((onCleanup) => {
      if (!this.toastMessage()) {
        return;
      }

      const timer = setTimeout(() => this.toastMessage.set(null), 3200);
      onCleanup(() => clearTimeout(timer));
    });
  }

  /** DELETE requires the "can-delete-own-quote" ownership policy server-side — only offer it where it would actually succeed. */
  protected canDelete(quote: Quote): boolean {
    return quote.ownerId === this.auth.userId();
  }

  protected setFilter(filter: FilterKey): void {
    this.activeFilter.set(filter);
  }

  protected toggleFavorite(quoteId: number, event: Event): void {
    event.stopPropagation();
    const email = this.auth.email();
    if (!email) return;

    this.favoriteIds.update((ids) => {
      const next = new Set(ids);
      if (next.has(quoteId)) {
        next.delete(quoteId);
      } else {
        next.add(quoteId);
      }

      localStorage.setItem(FAVORITES_KEY_PREFIX + email, JSON.stringify([...next]));
      return next;
    });
  }

  protected retry(): void {
    void this.loadQuotes();
  }

  // --- add quote ------------------------------------------------------------

  protected openAddModal(): void {
    this.newAuthor.set('');
    this.newText.set('');
    this.addError.set(null);
    this.isAddModalOpen.set(true);
  }

  protected closeAddModal(): void {
    this.isAddModalOpen.set(false);
  }

  protected onNewAuthorInput(event: Event): void {
    this.newAuthor.set((event.target as HTMLInputElement).value);
  }

  protected onNewTextInput(event: Event): void {
    this.newText.set((event.target as HTMLTextAreaElement).value);
  }

  protected onAddSubmit(event: Event): void {
    event.preventDefault();
    void this.submitAdd();
  }

  private async submitAdd(): Promise<void> {
    if (this.addSubmitting() || !this.isAddFormValid()) {
      return;
    }

    this.addSubmitting.set(true);
    this.addError.set(null);

    try {
      const created = await this.quoteService.createQuote({
        author: this.newAuthor().trim(),
        text: this.newText().trim(),
      });

      this.quotes.update((quotes) => [...quotes, created]);
      this.isAddModalOpen.set(false);
      this.toastMessage.set('Quote added to the collection.');
    } catch (err) {
      this.addError.set(toUserMessage(err, 'Unable to add that quote.'));
    } finally {
      this.addSubmitting.set(false);
    }
  }

  // --- delete quote ------------------------------------------------------------

  protected openDeleteConfirm(quoteId: number): void {
    this.deleteError.set(null);
    this.deleteConfirmId.set(quoteId);
  }

  protected cancelDeleteConfirm(): void {
    this.deleteConfirmId.set(null);
  }

  protected isDeleting(quoteId: number): boolean {
    return this.deletingIds().has(quoteId);
  }

  protected async confirmDelete(): Promise<void> {
    const quoteId = this.deleteConfirmId();
    if (quoteId === null || this.isDeleting(quoteId)) {
      return;
    }

    this.deletingIds.update((ids) => new Set(ids).add(quoteId));
    this.deleteError.set(null);

    try {
      await this.quoteService.deleteQuote(quoteId);

      this.quotes.update((quotes) => quotes.filter((q) => q.id !== quoteId));
      this.deleteConfirmId.set(null);
      this.toastMessage.set('Quote removed from the collection.');
    } catch (err) {
      this.deleteError.set(toUserMessage(err, 'Unable to remove that quote.'));
    } finally {
      this.deletingIds.update((ids) => {
        const next = new Set(ids);
        next.delete(quoteId);
        return next;
      });
    }
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }

  private async loadQuotes(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const quotes = await this.quoteService.getAllQuotes();
      this.quotes.set(quotes);
    } catch (err) {
      this.quotes.set([]);
      this.error.set(toUserMessage(err, 'Unable to load quotes.'));
    } finally {
      this.loading.set(false);
    }
  }
}
