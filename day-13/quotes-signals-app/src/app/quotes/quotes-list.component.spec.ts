import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Quote } from '../models/quote.model';
import { QuotesListComponent } from './quotes-list.component';

interface Harness {
  quotes: () => Quote[];
  loading: () => boolean;
  error: () => string | null;
  selectedId: () => number | null;
  select: (id: number) => void;
  retry: () => void;
}

function makeQuote(id: number): Quote {
  return { id, author: `Author ${id}`, text: `Text ${id}`, createdAt: '2026-01-01', ownerId: 1, ownerEmail: null };
}

describe('QuotesListComponent', () => {
  let fixture: ComponentFixture<QuotesListComponent>;
  let harness: Harness;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuotesListComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('fetches page 1 on init and populates the list', async () => {
    fixture = TestBed.createComponent(QuotesListComponent);
    harness = fixture.componentInstance as unknown as Harness;
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/quotes' && r.params.get('page') === '1' && r.params.get('size') === '100',
    );
    req.flush([makeQuote(1), makeQuote(2)]);
    await fixture.whenStable();

    expect(harness.quotes()).toHaveLength(2);
    expect(harness.loading()).toBe(false);
  });

  it('shows the real error message when the fetch fails', async () => {
    fixture = TestBed.createComponent(QuotesListComponent);
    harness = fixture.componentInstance as unknown as Harness;
    fixture.detectChanges();

    const req = httpMock.expectOne(() => true);
    req.flush({ title: 'boom' }, { status: 500, statusText: 'Internal Server Error' });
    await fixture.whenStable();

    expect(harness.error()).toBe('500 Internal Server Error: boom');
    expect(harness.quotes()).toEqual([]);
  });

  it('shows an empty state when the API returns no quotes', async () => {
    fixture = TestBed.createComponent(QuotesListComponent);
    harness = fixture.componentInstance as unknown as Harness;
    fixture.detectChanges();

    const req = httpMock.expectOne(() => true);
    req.flush([]);
    await fixture.whenStable();

    expect(harness.quotes()).toEqual([]);
    expect(harness.error()).toBeNull();
  });

  it('tracks the selected quote id when a list item is clicked', async () => {
    fixture = TestBed.createComponent(QuotesListComponent);
    harness = fixture.componentInstance as unknown as Harness;
    fixture.detectChanges();

    const req = httpMock.expectOne(() => true);
    req.flush([makeQuote(1)]);
    await fixture.whenStable();

    expect(harness.selectedId()).toBeNull();
    harness.select(1);
    expect(harness.selectedId()).toBe(1);

    // Selecting a quote also drives the embedded <app-quote-detail>, which
    // issues its own fetch — drain it so httpMock.verify() sees no pending requests.
    await fixture.whenStable();
    httpMock.expectOne('/api/quotes/1').flush(makeQuote(1));
    await fixture.whenStable();
  });
});
