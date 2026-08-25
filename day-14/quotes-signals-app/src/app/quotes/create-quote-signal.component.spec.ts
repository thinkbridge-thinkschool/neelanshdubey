import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FieldTree } from '@angular/forms/signals';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { CreateQuoteSignalComponent } from './create-quote-signal.component';

/** Protected members read via a typed harness cast — see other spec files in this project for the same convention. */
interface Harness {
  quoteForm: FieldTree<{ author: string; text: string }>;
  createdQuoteId: () => number | null;
  serverError: () => string | null;
  isInvalid: (name: 'author' | 'text') => boolean;
  onSubmit: () => Promise<boolean>;
  createAnother: () => void;
}

function authorInputEl(fixture: ComponentFixture<CreateQuoteSignalComponent>): HTMLInputElement {
  return fixture.debugElement.query(By.css('#author')).nativeElement as HTMLInputElement;
}

function textAreaEl(fixture: ComponentFixture<CreateQuoteSignalComponent>): HTMLTextAreaElement {
  return fixture.debugElement.query(By.css('#text')).nativeElement as HTMLTextAreaElement;
}

describe('CreateQuoteSignalComponent', () => {
  let fixture: ComponentFixture<CreateQuoteSignalComponent>;
  let harness: Harness;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CreateQuoteSignalComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CreateQuoteSignalComponent);
    harness = fixture.componentInstance as unknown as Harness;
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('does not call the API when the form is empty and moves focus to the first invalid field', async () => {
    await harness.onSubmit();
    await fixture.whenStable();

    httpMock.expectNone(() => true);
    expect(harness.quoteForm().touched()).toBe(true);
    expect(document.activeElement).toBe(authorInputEl(fixture));
  });

  it('wires aria-invalid and aria-describedby onto an invalid, touched field', async () => {
    harness.quoteForm.author().markAsTouched();
    fixture.detectChanges();
    await fixture.whenStable();

    const input = authorInputEl(fixture);
    expect(harness.isInvalid('author')).toBe(true);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe('author-hint author-error');
  });

  it('does not mark a pristine, untouched field as invalid', () => {
    expect(harness.isInvalid('author')).toBe(false);
    expect(authorInputEl(fixture).getAttribute('aria-invalid')).toBeNull();
  });

  it('moves focus to the quote text field when only it is invalid', async () => {
    harness.quoteForm.author().value.set('Maya Angelou');
    await harness.onSubmit();
    await fixture.whenStable();

    httpMock.expectNone(() => true);
    expect(document.activeElement).toBe(textAreaEl(fixture));
  });

  it('rejects a whitespace-only author the same way the real API would', () => {
    harness.quoteForm.author().value.set('   ');
    expect(harness.quoteForm.author().errors().some((error) => error.kind === 'required')).toBe(true);
  });

  it('flags an author over the API-enforced 200-character limit', () => {
    harness.quoteForm.author().value.set('a'.repeat(201));
    const errors = harness.quoteForm.author().errors();
    expect(errors.some((error) => error.kind === 'maxlength')).toBe(true);
  });

  it('creates a real quote via POST /api/quotes and shows the success state', async () => {
    harness.quoteForm.author().value.set('Maya Angelou');
    harness.quoteForm.text().value.set('Still I rise.');
    const submitted = harness.onSubmit();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.body).toEqual({ author: 'Maya Angelou', text: 'Still I rise.' });
    req.flush({ id: 42, author: 'Maya Angelou', text: 'Still I rise.', createdAt: '2026-01-01', ownerId: 1 });
    await submitted;
    await fixture.whenStable();

    expect(harness.createdQuoteId()).toBe(42);
    expect(harness.quoteForm().submitting()).toBe(false);
  });

  it('shows a raw server error and moves focus to it on a 500', async () => {
    harness.quoteForm.author().value.set('Maya Angelou');
    harness.quoteForm.text().value.set('Still I rise.');
    const submitted = harness.onSubmit();

    httpMock.expectOne('/api/quotes').flush({ title: 'boom' }, { status: 500, statusText: 'Internal Server Error' });
    await submitted;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(harness.serverError()).toBe('The server ran into a problem. Please try again shortly.');
    const banner = fixture.debugElement.query(By.css('.error-banner')).nativeElement as HTMLElement;
    expect(document.activeElement).toBe(banner);
  });

  it('shows the server-provided message and moves focus to it on a 400', async () => {
    harness.quoteForm.author().value.set('Maya Angelou');
    harness.quoteForm.text().value.set('Still I rise.');
    const submitted = harness.onSubmit();

    httpMock
      .expectOne('/api/quotes')
      .flush({ errors: { Author: ['Author is required.'] } }, { status: 400, statusText: 'Bad Request' });
    await submitted;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(harness.serverError()).toBe('Author is required.');
    const banner = fixture.debugElement.query(By.css('.error-banner')).nativeElement as HTMLElement;
    expect(document.activeElement).toBe(banner);
  });

  it('resets and refocuses the author field when starting another quote', async () => {
    harness.quoteForm.author().value.set('Maya Angelou');
    harness.quoteForm.text().value.set('Still I rise.');
    const submitted = harness.onSubmit();

    httpMock.expectOne('/api/quotes').flush({ id: 1, author: 'x', text: 'y', createdAt: '2026-01-01', ownerId: 1 });
    await submitted;
    await fixture.whenStable();
    fixture.detectChanges();

    harness.createAnother();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(harness.createdQuoteId()).toBeNull();
    expect(harness.quoteForm.author().value()).toBe('');
    expect(document.activeElement).toBe(authorInputEl(fixture));
  });
});
