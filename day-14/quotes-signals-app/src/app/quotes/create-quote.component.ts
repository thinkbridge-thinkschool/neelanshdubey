import { Component, Injector, afterNextRender, effect, inject, signal, viewChild, ElementRef } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { toUserMessage } from '../shared/http-error.util';
import { QuoteService } from '../services/quote.service';

const AUTHOR_MAX_LENGTH = 200;
const TEXT_MAX_LENGTH = 1000;

/**
 * Angular's own Validators.required/maxLength don't trim, but the real API
 * does (Quote.ValidateFields in Day-5/QuotesApi/Models/Quote.cs trims before
 * checking IsNullOrWhiteSpace and length). Without trimming here, a
 * whitespace-only author or text that only exceeds the limit through
 * trailing spaces would pass client-side and only fail once submitted.
 */
function requiredTrimmed(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim() ? null : { required: true };
}

function maxLengthTrimmed(max: number): ValidatorFn {
  return (control: AbstractControl<string>) => {
    const length = control.value.trim().length;
    return length > max ? { maxlength: { requiredLength: max, actualLength: length } } : null;
  };
}

type FieldName = 'author' | 'text';

@Component({
  selector: 'app-create-quote',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './create-quote.component.html',
  styleUrl: './create-quote.component.scss',
})
export class CreateQuoteComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly quoteService = inject(QuoteService);
  private readonly injector = inject(Injector);

  protected readonly authorMaxLength = AUTHOR_MAX_LENGTH;
  protected readonly textMaxLength = TEXT_MAX_LENGTH;

  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly createdQuoteId = signal<number | null>(null);

  protected readonly form = this.fb.group({
    author: this.fb.control('', [requiredTrimmed, maxLengthTrimmed(AUTHOR_MAX_LENGTH)]),
    text: this.fb.control('', [requiredTrimmed, maxLengthTrimmed(TEXT_MAX_LENGTH)]),
  });

  private readonly authorInput = viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput = viewChild<ElementRef<HTMLTextAreaElement>>('textInput');
  private readonly errorBanner = viewChild<ElementRef<HTMLElement>>('errorBanner');

  constructor() {
    // The error banner only exists in the DOM once serverError() is set, so
    // viewChild() only resolves to it on the render right after — this effect
    // fires again once that happens and moves focus there, the same way
    // focusFirstInvalid() does for client-side validation failures.
    effect(() => {
      const banner = this.errorBanner();
      if (banner && this.serverError()) {
        banner.nativeElement.focus();
      }
    });
  }

  protected isInvalid(name: FieldName): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.dirty || control.touched);
  }

  protected describedBy(name: FieldName): string {
    const ids = [`${name}-hint`];
    if (this.isInvalid(name)) {
      ids.push(`${name}-error`);
    }
    return ids.join(' ');
  }

  protected errorMessage(name: FieldName): string {
    const errors = this.form.controls[name].errors;
    if (!errors) {
      return '';
    }

    if (errors['required']) {
      return name === 'author' ? 'Author is required.' : 'Quote text is required.';
    }

    if (errors['maxlength']) {
      const max = errors['maxlength'].requiredLength;
      return name === 'author'
        ? `Author must be ${max} characters or fewer.`
        : `Quote text must be ${max} characters or fewer.`;
    }

    return '';
  }

  protected onSubmit(): void {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    void this.submit();
  }

  protected createAnother(): void {
    this.createdQuoteId.set(null);
    this.form.reset({ author: '', text: '' });

    // The form (and #authorInput) only re-enters the DOM once change
    // detection runs after createdQuoteId() flips back to null, so focusing
    // synchronously here would still target the stale/detached element from
    // before the success banner replaced it.
    afterNextRender(() => this.authorInput()?.nativeElement.focus(), { injector: this.injector });
  }

  private focusFirstInvalid(): void {
    if (this.form.controls.author.invalid) {
      this.authorInput()?.nativeElement.focus();
    } else if (this.form.controls.text.invalid) {
      this.textInput()?.nativeElement.focus();
    }
  }

  private async submit(): Promise<void> {
    this.submitting.set(true);
    this.serverError.set(null);

    const { author, text } = this.form.getRawValue();

    try {
      const created = await this.quoteService.createQuote({ author: author.trim(), text: text.trim() });
      this.createdQuoteId.set(created.id);
      this.form.reset({ author: '', text: '' });
    } catch (err) {
      this.serverError.set(this.describeError(err));
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * The backend's own validator (QuoteValidator.cs) only rejects missing
   * fields with a 400 ValidationProblem; a too-long field instead throws a
   * DomainException that ExceptionMiddleware turns into a bare 500. Both are
   * covered here even though client-side maxLengthTrimmed should prevent the
   * 500 case in practice.
   */
  private describeError(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.status === 400) {
      const errors = (err.error as { errors?: Record<string, string[]> } | null)?.errors;
      const firstMessage = errors ? Object.values(errors)[0]?.[0] : undefined;
      return firstMessage ?? 'The server rejected this quote as invalid.';
    }

    return toUserMessage(err, 'Unable to add that quote.');
  }
}
