import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Quote } from '../models/quote.model';
import { QuoteDetailComponent } from './quote-detail.component';

interface Harness {
  detail: () => Quote | null;
  loading: () => boolean;
  error: () => string | null;
}

function makeQuote(id: number): Quote {
  return { id, author: `Author ${id}`, text: `Text ${id}`, createdAt: '2026-01-01', ownerId: 1 };
}

describe('QuoteDetailComponent', () => {
  let fixture: ComponentFixture<QuoteDetailComponent>;
  let harness: Harness;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteDetailComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(QuoteDetailComponent);
    harness = fixture.componentInstance as unknown as Harness;
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows nothing selected when quoteId is null', async () => {
    fixture.componentRef.setInput('quoteId', null);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(harness.detail()).toBeNull();
    expect(harness.loading()).toBe(false);
  });

  it('fetches and displays the quote for the given id', async () => {
    fixture.componentRef.setInput('quoteId', 1);
    fixture.detectChanges();
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes/1');
    req.flush(makeQuote(1));
    await fixture.whenStable();

    expect(harness.detail()).toMatchObject({ id: 1 });
    expect(harness.loading()).toBe(false);
  });

  it('surfaces the real HTTP status and message on failure, not a generic string', async () => {
    fixture.componentRef.setInput('quoteId', 404);
    fixture.detectChanges();
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes/404');
    req.flush({ title: 'No such quote' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(harness.error()).toBe('404 Not Found: No such quote');
    expect(harness.loading()).toBe(false);
  });

  it('cancels the earlier in-flight request and lets the later selection win (stale-response race)', async () => {
    fixture.componentRef.setInput('quoteId', 1);
    fixture.detectChanges();
    await fixture.whenStable();
    const reqA = httpMock.expectOne('/api/quotes/1');

    fixture.componentRef.setInput('quoteId', 2);
    fixture.detectChanges();
    await fixture.whenStable();
    const reqB = httpMock.expectOne('/api/quotes/2');

    // switchMap unsubscribes from quote 1's request as soon as quote 2 is
    // selected — Angular's HTTP layer marks it cancelled, so it can never
    // resolve into `detail` even if the server responds to it late.
    expect(reqA.cancelled).toBe(true);

    reqB.flush(makeQuote(2));
    await fixture.whenStable();

    expect(harness.detail()).toMatchObject({ id: 2 });
  });
});
