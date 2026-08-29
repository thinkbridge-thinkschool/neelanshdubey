import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup, FormControl } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { CreateQuoteComponent } from './create-quote.component';

/** Protected members read via a typed harness cast — see other spec files in this project for the same convention. */
interface Harness {
  form: FormGroup<{ author: FormControl<string>; text: FormControl<string> }>;
  submitting: () => boolean;
  serverError: () => string | null;
  createdQuoteId: () => number | null;
  isInvalid: (name: 'author' | 'text') => boolean;
  onSubmit: () => void;
  createAnother: () => void;
}

function authorInputEl(fixture: ComponentFixture<CreateQuoteComponent>): HTMLInputElement {
  return fixture.debugElement.query(By.css('#author')).nativeElement as HTMLInputElement;
}

function textAreaEl(fixture: ComponentFixture<CreateQuoteComponent>): HTMLTextAreaElement {
  return fixture.debugElement.query(By.css('#text')).nativeElement as HTMLTextAreaElement;
}

describe('CreateQuoteComponent', () => {
  let fixture: ComponentFixture<CreateQuoteComponent>;
  let harness: Harness;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CreateQuoteComponent],
      providers: [
        provideZonelessChangeDetection(),
        // errorInterceptor is what turns a 400 ValidationProblemDetails into
        // the AppError describeError() reads — real app.config.ts wiring.
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CreateQuoteComponent);
    harness = fixture.componentInstance as unknown as Harness;
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('does not call the API when the form is empty and moves focus to the first invalid field', async () => {
    harness.onSubmit();
    await fixture.whenStable();

    httpMock.expectNone(() => true);
    expect(harness.form.touched).toBe(true);
    expect(document.activeElement).toBe(authorInputEl(fixture));
  });

  it('wires aria-invalid and aria-describedby onto an invalid, touched field', async () => {
    harness.form.controls.author.markAsTouched();
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
    harness.form.controls.author.setValue('Maya Angelou');
    harness.onSubmit();
    await fixture.whenStable();

    httpMock.expectNone(() => true);
    expect(document.activeElement).toBe(textAreaEl(fixture));
  });

  it('rejects a whitespace-only author the same way the real API would', () => {
    harness.form.controls.author.setValue('   ');
    expect(harness.form.controls.author.errors?.['required']).toBeTruthy();
  });

  it('flags an author over the API-enforced 200-character limit', () => {
    harness.form.controls.author.setValue('a'.repeat(201));
    expect(harness.form.controls.author.errors?.['maxlength']).toEqual({
      requiredLength: 200,
      actualLength: 201,
    });
  });

  it('creates a real quote via POST /api/quotes and shows the success state', async () => {
    harness.form.setValue({ author: 'Maya Angelou', text: 'Still I rise.' });
    harness.onSubmit();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.body).toEqual({ author: 'Maya Angelou', text: 'Still I rise.' });
    req.flush({ id: 42, author: 'Maya Angelou', text: 'Still I rise.', createdAt: '2026-01-01', ownerId: 1 });
    await fixture.whenStable();

    expect(harness.createdQuoteId()).toBe(42);
    expect(harness.submitting()).toBe(false);
  });

  it('shows a raw server error and moves focus to it on a 500', async () => {
    harness.form.setValue({ author: 'Maya Angelou', text: 'Still I rise.' });
    harness.onSubmit();

    httpMock.expectOne('/api/quotes').flush({ title: 'boom' }, { status: 500, statusText: 'Internal Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(harness.serverError()).toBe('The server ran into a problem. Please try again shortly.');
    const banner = fixture.debugElement.query(By.css('.error-banner')).nativeElement as HTMLElement;
    expect(document.activeElement).toBe(banner);
  });

  it('shows the first field message from a 400 ValidationProblemDetails', async () => {
    harness.form.setValue({ author: 'Maya Angelou', text: 'Still I rise.' });
    harness.onSubmit();

    httpMock.expectOne('/api/quotes').flush(
      { title: 'One or more validation errors occurred.', status: 400, errors: { text: ['Text is required.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    expect(harness.serverError()).toBe('Text is required.');
  });

  it('resets and refocuses the author field when starting another quote', async () => {
    harness.form.setValue({ author: 'Maya Angelou', text: 'Still I rise.' });
    harness.onSubmit();

    httpMock.expectOne('/api/quotes').flush({ id: 1, author: 'x', text: 'y', createdAt: '2026-01-01', ownerId: 1 });
    await fixture.whenStable();
    fixture.detectChanges();

    harness.createAnother();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(harness.createdQuoteId()).toBeNull();
    expect(harness.form.controls.author.value).toBe('');
    expect(document.activeElement).toBe(authorInputEl(fixture));
  });
});
