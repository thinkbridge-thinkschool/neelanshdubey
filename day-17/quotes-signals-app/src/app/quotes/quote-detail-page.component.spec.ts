import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Quote } from '../models/quote.model';
import { QuoteDetailPageComponent } from './quote-detail-page.component';

function makeQuote(id: number): Quote {
  return { id, author: `Author ${id}`, text: `Text ${id}`, createdAt: '2026-01-01', ownerId: 1 };
}

describe('QuoteDetailPageComponent', () => {
  let fixture: ComponentFixture<QuoteDetailPageComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuoteDetailPageComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(QuoteDetailPageComponent);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('parses the route-bound string id and fetches that quote', async () => {
    fixture.componentRef.setInput('id', '5');
    fixture.detectChanges();
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes/5');
    req.flush(makeQuote(5));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Text 5');
  });

  it('shows an invalid-id message instead of fetching when the route param is not numeric', async () => {
    fixture.componentRef.setInput('id', 'not-a-number');
    fixture.detectChanges();
    await fixture.whenStable();

    httpMock.expectNone(() => true);
    expect(fixture.nativeElement.textContent).toContain('not a valid quote id');
  });
});
