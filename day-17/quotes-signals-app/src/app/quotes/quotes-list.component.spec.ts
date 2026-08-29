import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Quote } from '../models/quote.model';
import { QuotesListComponent } from './quotes-list.component';

function makePage(startId: number, count: number): Quote[] {
  return Array.from({ length: count }, (_, i) => ({
    id: startId + i,
    author: `Author ${startId + i}`,
    text: `Text ${startId + i}`,
    createdAt: '2026-01-01',
    ownerId: 1,
    ownerEmail: null,
  }));
}

describe('QuotesListComponent', () => {
  let fixture: ComponentFixture<QuotesListComponent>;
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

  function expectPageRequest(page: number) {
    return httpMock.expectOne(
      (r) => r.url === '/api/quotes' && r.params.get('page') === String(page) && r.params.get('size') === '10',
    );
  }

  it('fetches page 1 (size 10) on init and renders each quote as a route link', async () => {
    fixture = TestBed.createComponent(QuotesListComponent);
    fixture.detectChanges();

    expectPageRequest(1).flush(makePage(1, 10));
    await fixture.whenStable();
    fixture.detectChanges();

    const links = fixture.nativeElement.querySelectorAll('a.quote-item') as NodeListOf<HTMLAnchorElement>;
    expect(links).toHaveLength(10);
    expect(links[0].getAttribute('href')).toBe('/quotes/1');
  });

  it('shows the real error message when the fetch fails, with a working retry', async () => {
    fixture = TestBed.createComponent(QuotesListComponent);
    fixture.detectChanges();

    expectPageRequest(1).flush({ title: 'boom' }, { status: 500, statusText: 'Internal Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('500 Internal Server Error: boom');

    fixture.nativeElement.querySelector('.retry-button').click();
    expectPageRequest(1).flush(makePage(1, 10));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('a.quote-item')).toHaveLength(10);
  });

  it('Next advances to page 2 and disables itself on the short final page; Previous re-enables', async () => {
    fixture = TestBed.createComponent(QuotesListComponent);
    fixture.detectChanges();
    expectPageRequest(1).flush(makePage(1, 10));
    await fixture.whenStable();
    fixture.detectChanges();

    const [prevBtn, nextBtn] = fixture.nativeElement.querySelectorAll('.pager button') as NodeListOf<HTMLButtonElement>;
    expect(prevBtn.disabled).toBe(true);
    expect(nextBtn.disabled).toBe(false);

    nextBtn.click();
    expectPageRequest(2).flush(makePage(11, 6)); // short page: this is the last one
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Page 2');
    expect(fixture.nativeElement.querySelectorAll('a.quote-item')).toHaveLength(6);
    expect(prevBtn.disabled).toBe(false);
    expect(nextBtn.disabled).toBe(true);

    prevBtn.click();
    expectPageRequest(1).flush(makePage(1, 10));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Page 1');
  });
});
