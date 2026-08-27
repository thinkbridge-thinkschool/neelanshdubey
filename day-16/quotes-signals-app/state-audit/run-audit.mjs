// Evidence script for Day-16 Task 2: signals-first state management.
//
// Drives the REAL running dev server (http://localhost:4216) and the REAL
// QuotesApi backend (https://localhost:7210) with a headless browser — no
// mocked HTTP, no fake auth. Exercises QuotesPageState (signals + a plain
// injectable service, no store library) against the real
// GET /api/quotes?page&size endpoint.
//
// Proves, against the real app:
//   1. Page 1 loads the real first PAGE_SIZE(=10) quotes; Previous starts
//      disabled.
//   2. Next fires a real GET /api/quotes?page=2&size=10 request, advances
//      the URL-less in-page state, and disables itself once the page comes
//      back short (the real API's only "last page" signal — no total
//      count field).
//   3. Previous goes back to page 1 with the same content as before.
//   4. The out-of-order-response guard actually holds when running
//      compiled, in the real browser, against the real backend: a slow
//      page-2 response that resolves AFTER a fast page-1 re-fetch must
//      NOT clobber the page-1 state that already rendered.
//
// Usage: node state-audit/run-audit.mjs
// Requires: the day-16 Angular dev server on :4216 and the real QuotesApi
// on :7210 already running, with at least 11 real quotes in the DB (so
// page 1 is a full page of 10 and page 2 is a real, non-empty short page).

import { chromium } from 'playwright';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SHOTS_DIR = join(__dirname, 'screenshots');
mkdirSync(SHOTS_DIR, { recursive: true });

const APP_URL = 'http://localhost:4216';
const API_URL = 'https://localhost:7210';
const runId = Math.random().toString(36).slice(2, 8);
const testEmail = `state-audit-${runId}@example.com`;
const testPassword = 'auditpass123';

const report = { runId, testEmail, checks: [] };

function logStep(name) {
  console.log(`\n=== ${name} ===`);
}

function record(name, pass, detail) {
  console.log(`  [${pass ? 'PASS' : 'FAIL'}] ${name}${detail ? ' — ' + detail : ''}`);
  report.checks.push({ name, pass, detail });
  if (!pass) throw new Error(`Check failed: ${name} — ${detail ?? ''}`);
}

async function quoteItemCount(page) {
  return page.locator('a.quote-item').count();
}

async function pagerText(page) {
  return (await page.locator('.pager-status').textContent())?.trim();
}

async function main() {
  const browser = await chromium.launch();
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
  const apiContext = context.request;

  logStep('Confirm the real DB has enough quotes for a meaningful two-page test');
  const seedRes = await apiContext.get(`${API_URL}/api/quotes?page=1&size=100`);
  const seedQuotes = await seedRes.json();
  console.log(`  ${seedQuotes.length} real quotes in the DB`);
  if (seedQuotes.length < 11) {
    throw new Error(`Need at least 11 real quotes to exercise a full page-1 + non-empty page-2; found ${seedQuotes.length}`);
  }

  logStep('Register a throwaway real user against the real API');
  const registerRes = await apiContext.post(`${API_URL}/api/auth/register`, {
    data: { email: testEmail, password: testPassword },
  });
  if (!registerRes.ok()) {
    throw new Error(`register failed: ${registerRes.status()} ${await registerRes.text()}`);
  }
  const { accessToken, refreshToken } = await registerRes.json();
  console.log(`  registered ${testEmail}`);

  const page = await context.newPage();
  await page.goto(APP_URL);
  await page.evaluate(
    ({ accessToken, refreshToken, email }) => {
      localStorage.setItem('quotes-app.session', JSON.stringify({ accessToken, refreshToken, email }));
    },
    { accessToken, refreshToken, email: testEmail },
  );

  const requests = [];
  page.on('request', (req) => {
    if (req.url().includes('/api/quotes') && req.method() === 'GET') requests.push(req.url());
  });

  // -----------------------------------------------------------------------
  // Check 1: page 1 is a real full page, Previous starts disabled
  // -----------------------------------------------------------------------
  logStep('Check 1: /quotes loads a real full page of 10, Previous disabled');
  await page.goto(`${APP_URL}/quotes`);
  await page.waitForSelector('a.quote-item');
  const page1Count = await quoteItemCount(page);
  record('page 1 shows exactly PAGE_SIZE (10) real quotes', page1Count === 10, `got ${page1Count}`);

  const prevDisabled1 = await page.locator('.pager button').first().isDisabled();
  record('Previous is disabled on page 1', prevDisabled1, `disabled=${prevDisabled1}`);
  await page.screenshot({ path: join(SHOTS_DIR, '01-page-1.png'), fullPage: true });

  // -----------------------------------------------------------------------
  // Check 2: Next fires a real request and shows the real short page 2
  // -----------------------------------------------------------------------
  logStep('Check 2: Next fires GET /api/quotes?page=2&size=10 and renders the real (short) page 2');
  const requestsBeforeNext = requests.length;
  await page.locator('.pager button', { hasText: 'Next' }).click();
  await page.waitForFunction(() => document.querySelector('.pager-status')?.textContent?.includes('Page 2'));

  const page2Requested = requests.slice(requestsBeforeNext).some((u) => u.includes('page=2') && u.includes('size=10'));
  record('a real GET /api/quotes?page=2&size=10 request was observed', page2Requested, requests.slice(requestsBeforeNext).join(', '));

  const page2Count = await quoteItemCount(page);
  const expectedPage2Count = seedQuotes.length - 10;
  record('page 2 shows the real remainder (short page)', page2Count === expectedPage2Count, `got ${page2Count}, expected ${expectedPage2Count}`);

  const nextDisabledOnLastPage = await page.locator('.pager button', { hasText: 'Next' }).isDisabled();
  record('Next disables itself once the page comes back short', nextDisabledOnLastPage, `disabled=${nextDisabledOnLastPage}`);
  await page.screenshot({ path: join(SHOTS_DIR, '02-page-2.png'), fullPage: true });

  // -----------------------------------------------------------------------
  // Check 3: Previous returns to the real page 1
  // -----------------------------------------------------------------------
  logStep('Check 3: Previous returns to page 1 with the same real content');
  await page.locator('.pager button', { hasText: 'Previous' }).click();
  await page.waitForFunction(() => document.querySelector('.pager-status')?.textContent?.includes('Page 1'));
  const backOnPage1Count = await quoteItemCount(page);
  record('Previous returns to a full page 1', backOnPage1Count === 10, `got ${backOnPage1Count}`);

  // -----------------------------------------------------------------------
  // Check 4: out-of-order guard holds in the real compiled app
  // -----------------------------------------------------------------------
  logStep('Check 4: a slow, superseded response cannot clobber newer state (real app, real backend)');
  // Delay the FIRST page=2 request the browser makes from here on, so that
  // a fast, later page=1 re-fetch resolves and renders *before* the slow
  // page=2 response arrives. Drives QuotesPageState.loadPage() directly
  // (bypassing the disabled Next/Previous buttons) via Angular's
  // window.ng.getComponent() dev helper — the same private-method access
  // the unit test uses, now proven against the real running app.
  let delayedOnce = false;
  await page.route('**/api/quotes*', async (route) => {
    const url = route.request().url();
    if (!delayedOnce && url.includes('page=2') && url.includes('size=10')) {
      delayedOnce = true;
      await new Promise((r) => setTimeout(r, 1500));
    }
    await route.continue();
  });

  const raceResult = await page.evaluate(async () => {
    const el = document.querySelector('app-quotes-list');
    // @ts-ignore — dev-mode-only Angular debugging API
    const cmp = window.ng.getComponent(el);
    const state = cmp.state;
    const loadPage = state.loadPage.bind(state);
    const slow = loadPage(2); // issued first, artificially delayed
    const fast = loadPage(1); // issued second, should resolve first and win
    await fast;
    const pageRightAfterFast = state.page();
    await slow; // let the stale response try (and fail) to land
    return { pageRightAfterFast, pageAfterBothSettled: state.page(), quoteCount: state.quotes().length };
  });

  record(
    'the later call (page 1) already won right after it resolved',
    raceResult.pageRightAfterFast === 1,
    `page()=${raceResult.pageRightAfterFast}`,
  );
  record(
    'the stale, late-arriving page-2 response did not overwrite page 1 once it finally resolved',
    raceResult.pageAfterBothSettled === 1 && raceResult.quoteCount === 10,
    JSON.stringify(raceResult),
  );
  await page.screenshot({ path: join(SHOTS_DIR, '03-after-race-still-page-1.png'), fullPage: true });

  await context.close();
  await browser.close();

  writeFileSync(join(__dirname, 'state-audit-results.json'), JSON.stringify(report, null, 2));

  const failed = report.checks.filter((c) => !c.pass);
  console.log(`\n=== DONE: ${report.checks.length - failed.length}/${report.checks.length} checks passed ===`);
  process.exit(failed.length > 0 ? 1 : 0);
}

main().catch((err) => {
  console.error(err);
  writeFileSync(join(__dirname, 'state-audit-results.json'), JSON.stringify(report, null, 2));
  process.exit(1);
});
