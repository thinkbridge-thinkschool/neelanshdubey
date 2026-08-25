import { Component, ElementRef, Injector, afterNextRender, effect, inject, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormField, FormRoot, form, schema, submit, validate } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { toUserMessage } from '../shared/http-error.util';
import { QuoteService } from '../services/quote.service';

const AUTHOR_MAX_LENGTH = 200;
const TEXT_MAX_LENGTH = 1000;

interface QuoteFormModel {
  author: string;
  text: string;
}

/**
 * Signal Forms' own required()/maxLength() validators don't trim (same gap as
 * Angular's classic Validators.required/maxLength — see requiredTrimmed()/
 * maxLengthTrimmed() in create-quote.component.ts), but the real API trims
 * before checking IsNullOrWhiteSpace and length. validate() is used instead so
 * both checks run against the trimmed value, mirroring the reactive-forms
 * version's two custom validators field-for-field.
 */
const quoteFormSchema = schema<QuoteFormModel>((path) => {
  validate(path.author, (ctx) => (ctx.value().trim() ? null : { kind: 'required', message: 'Author is required.' }));
  validate(path.author, (ctx) => {
    const length = ctx.value().trim().length;
    return length > AUTHOR_MAX_LENGTH
      ? { kind: 'maxlength', message: `Author must be ${AUTHOR_MAX_LENGTH} characters or fewer.` }
      : null;
  });

  validate(path.text, (ctx) => (ctx.value().trim() ? null : { kind: 'required', message: 'Quote text is required.' }));
  validate(path.text, (ctx) => {
    const length = ctx.value().trim().length;
    return length > TEXT_MAX_LENGTH
      ? { kind: 'maxlength', message: `Quote text must be ${TEXT_MAX_LENGTH} characters or fewer.` }
      : null;
  });
});

type FieldName = 'author' | 'text';

@Component({
  selector: 'app-create-quote-signal',
  imports: [FormField, FormRoot, RouterLink],
  templateUrl: './create-quote-signal.component.html',
  styleUrl: './create-quote.component.scss',
})
export class CreateQuoteSignalComponent {
  private readonly quoteService = inject(QuoteService);
  private readonly injector = inject(Injector);

  protected readonly authorMaxLength = AUTHOR_MAX_LENGTH;
  protected readonly textMaxLength = TEXT_MAX_LENGTH;

  protected readonly createdQuoteId = signal<number | null>(null);

  private readonly model = signal<QuoteFormModel>({ author: '', text: '' });

  /**
   * Deliberately a plain signal managed by this component, not a read of
   * `quoteForm().errors()`. Signal Forms links a field's `submissionErrors`
   * to that field's *value* signal (a `linkedSignal`), so an error returned
   * from `action` below would silently vanish the instant the user edits
   * *any* field — even one that has nothing to do with the failure (e.g. a
   * 401 from an expired token isn't fixed by editing the quote text). The
   * reactive sibling only clears `serverError` at the start of the next
   * submit attempt, so it's cleared here the same way, to keep that parity
   * rather than adopt the framework's more eager default.
   */
  protected readonly serverError = signal<string | null>(null);

  protected readonly quoteForm = form(this.model, quoteFormSchema, {
    submission: {
      action: async () => {
        this.serverError.set(null);

        const { author, text } = this.quoteForm().value();

        try {
          const created = await this.quoteService.createQuote({ author: author.trim(), text: text.trim() });
          this.createdQuoteId.set(created.id);
          this.quoteForm().reset({ author: '', text: '' });
          return undefined;
        } catch (err) {
          const message = this.describeError(err);
          this.serverError.set(message);
          // Still returned to submit() (attached to the form root since
          // `fieldTree` is omitted) so the framework's own error-tracking
          // stays consistent — it's just not what the banner reads from.
          return [{ kind: 'server', message }];
        }
      },
      onInvalid: () => this.focusFirstInvalid(),
    },
  });

  private readonly errorBanner = viewChild<ElementRef<HTMLElement>>('errorBanner');

  constructor() {
    // Same rationale as create-quote.component.ts: the error banner only
    // exists in the DOM once serverError() is set, so viewChild() only
    // resolves to it on the render right after — this effect fires again
    // once that happens and moves focus there.
    effect(() => {
      const banner = this.errorBanner();
      if (banner && this.serverError()) {
        banner.nativeElement.focus();
      }
    });
  }

  protected isInvalid(name: FieldName): boolean {
    const state = this.fieldState(name);
    return state.invalid() && (state.dirty() || state.touched());
  }

  protected describedBy(name: FieldName): string {
    const ids = [`${name}-hint`];
    if (this.isInvalid(name)) {
      ids.push(`${name}-error`);
    }
    return ids.join(' ');
  }

  protected errorMessage(name: FieldName): string {
    return this.fieldState(name).errors()[0]?.message ?? '';
  }

  /**
   * Exposed as a plain method (not bound via `(ngSubmit)`/`(click)` in the
   * template) purely as a deterministic test hook — the real UI relies on
   * `[formRoot]`, whose own submit handler does exactly this internally
   * (`submit(fieldTree())`) in response to Enter/submit-button activation.
   */
  protected onSubmit(): Promise<boolean> {
    return submit(this.quoteForm);
  }

  protected createAnother(): void {
    this.createdQuoteId.set(null);
    this.quoteForm().reset({ author: '', text: '' });

    // Same reasoning as create-quote.component.ts's createAnother(): the form
    // (and its author field binding) only re-enters the DOM once change
    // detection runs after createdQuoteId() flips back to null, so focusing
    // synchronously here would still target a detached/unbound field.
    afterNextRender(() => this.quoteForm.author().focusBoundControl(), { injector: this.injector });
  }

  private fieldState(name: FieldName) {
    return name === 'author' ? this.quoteForm.author() : this.quoteForm.text();
  }

  private focusFirstInvalid(): void {
    if (this.quoteForm.author().invalid()) {
      this.quoteForm.author().focusBoundControl();
    } else if (this.quoteForm.text().invalid()) {
      this.quoteForm.text().focusBoundControl();
    }
  }

  /** Same rationale as create-quote.component.ts's describeError(). */
  private describeError(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.status === 400) {
      const errors = (err.error as { errors?: Record<string, string[]> } | null)?.errors;
      const firstMessage = errors ? Object.values(errors)[0]?.[0] : undefined;
      return firstMessage ?? 'The server rejected this quote as invalid.';
    }

    return toUserMessage(err, 'Unable to add that quote.');
  }
}
