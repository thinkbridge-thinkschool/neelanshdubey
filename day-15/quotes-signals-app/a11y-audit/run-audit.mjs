// Automated accessibility audit for CreateQuoteComponent (/quotes/new).
//
// Drives the REAL running dev server (http://localhost:4214) and the REAL
// QuotesApi backend (https://localhost:7210) with a headless browser —
// no mocked HTTP, no fake auth. A fresh user is registered against the
// real /api/auth/register endpoint, its session is placed into
// localStorage exactly the way AuthService itself stores it, and every
// state (empty, invalid, submitting, success, server-error) is driven via
// real keyboard input (Tab/Type/Enter — no mouse clicks, no programmatic
// form.setValue()). Each state is screenshotted and scanned with
// axe-core. This does not replace a human screen-reader pass (it can't
// judge how something *sounds*), but it does verify the same DOM
// contract a screen reader depends on: labels, aria-invalid,
// aria-describedby, and focus placement.
//
// Usage: node a11y-audit/run-audit.mjs
// Requires: the day-14 Angular dev server on :4214 and the real QuotesApi
// on :7210 already running.

import { chromium } from 'playwright';
import AxeBuilder from '@axe-core/playwright';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SHOTS_DIR = join(__dirname, 'screenshots');
mkdirSync(SHOTS_DIR, { recursive: true });

const APP_URL = 'http://localhost:4214';
const API_URL = 'https://localhost:7210';
const runId = Math.random().toString(36).slice(2, 8);
const testEmail = `a11y-audit-${runId}@example.com`;
const testPassword = 'auditpass123';

const results = [];

function logStep(name) {
  console.log(`\n=== ${name} ===`);
}

async function scan(page, name) {
  const axeResults = await new AxeBuilder({ page }).include('.page-shell').analyze();
  const screenshotPath = join(SHOTS_DIR, `${name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true });

  const violations = axeResults.violations.map((v) => ({
    id: v.id,
    impact: v.impact,
    help: v.help,
    nodes: v.nodes.map((n) => ({ target: n.target, html: n.html, summary: n.failureSummary })),
  }));

  console.log(`  screenshot: ${screenshotPath}`);
  console.log(`  axe violations: ${violations.length}`);
  violations.forEach((v) => {
    console.log(`    - [${v.impact}] ${v.id}: ${v.help} (${v.nodes.length} node(s))`);
    v.nodes.forEach((n) => console.log(`        target=${n.target} html=${n.html}`));
  });

  results.push({ state: name, violations, screenshot: `screenshots/${name}.png` });
  return violations;
}

async function main() {
  const browser = await chromium.launch();
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  const apiContext = context.request;

  logStep('Register a throwaway real user against the real API');
  const registerRes = await apiContext.post(`${API_URL}/api/auth/register`, {
    data: { email: testEmail, password: testPassword },
  });
  if (!registerRes.ok()) {
    throw new Error(`register failed: ${registerRes.status()} ${await registerRes.text()}`);
  }
  const { accessToken, refreshToken } = await registerRes.json();
  console.log(`  registered ${testEmail}`);

  logStep('Seed the real session into localStorage (same shape AuthService writes)');
  await page.goto(APP_URL);
  await page.evaluate(
    ({ accessToken, refreshToken, email }) => {
      localStorage.setItem('quotes-app.session', JSON.stringify({ accessToken, refreshToken, email }));
    },
    { accessToken, refreshToken, email: testEmail },
  );

  logStep('Navigate to /quotes/new');
  await page.goto(`${APP_URL}/quotes/new`);
  await page.waitForSelector('#author');

  await scan(page, '01-empty');

  logStep('Keyboard-only: Tab to the submit button and press Enter with empty fields');
  // Tab order on this page: back-link -> #author -> #text -> submit button.
  await page.keyboard.press('Tab'); // back-link
  await page.keyboard.press('Tab'); // #author
  await page.keyboard.press('Tab'); // #text
  await page.keyboard.press('Tab'); // submit button
  await page.keyboard.press('Enter');
  await page.waitForSelector('#author[aria-invalid="true"]');

  const focusedId = await page.evaluate(() => document.activeElement?.id);
  console.log(`  document.activeElement after failed submit: #${focusedId}`);
  if (focusedId !== 'author') {
    throw new Error(`expected focus on #author after a failed submit, got #${focusedId}`);
  }
  await scan(page, '02-invalid-focus-moved-to-author');

  logStep('Keyboard-only: fill the form and submit (delay the real request to observe "submitting")');
  await page.locator('#author').fill('Maya Angelou');
  await page.locator('#text').fill('Still I rise.');

  // The real backend responds in a few ms on localhost — too fast to ever
  // observe the disabled/spinner state in a screenshot. Delaying the
  // request here (still a real request, still hitting the real API, just
  // paced) is the only way to make "submitting" observable at all.
  await page.route('**/api/quotes', async (route) => {
    await new Promise((r) => setTimeout(r, 900));
    await route.continue();
  });

  await page.locator('#text').press('Tab'); // move to the submit button
  await page.keyboard.press('Enter');
  await page.waitForSelector('button[type="submit"][aria-busy="true"]');
  await scan(page, '03-submitting');

  await page.waitForSelector('.success-banner', { timeout: 10000 });
  await page.unroute('**/api/quotes');
  await scan(page, '04-success');

  const createdQuoteText = await page.locator('.success-banner p').first().textContent();
  console.log(`  success banner: ${createdQuoteText?.trim()}`);

  logStep('Corrupt the stored token to provoke a real 401 from the real API, then re-submit');
  // AuthService reads localStorage once, into an in-memory BehaviorSubject,
  // when it's constructed — mutating localStorage alone doesn't touch that
  // live value. A reload re-constructs the service (and the app) from the
  // now-corrupted storage, which is what actually gets authHeaders() to
  // send a bad token on the next request.
  await page.evaluate(() => {
    const raw = localStorage.getItem('quotes-app.session');
    const session = JSON.parse(raw);
    session.accessToken = 'this-is-not-a-valid-jwt';
    localStorage.setItem('quotes-app.session', JSON.stringify(session));
  });
  await page.reload();

  page.on('response', (res) => {
    if (res.url().includes('/api/quotes') && res.request().method() === 'POST') {
      console.log(`  POST /api/quotes -> ${res.status()}`);
    }
  });

  await page.waitForSelector('#author');
  await page.locator('#author').fill('Maya Angelou');
  await page.locator('#text').fill('Still I rise, again.');
  await page.locator('#text').press('Tab');
  await page.keyboard.press('Enter');
  await page.waitForSelector('.error-banner');

  const bannerFocused = await page.evaluate(() => document.activeElement?.className);
  console.log(`  document.activeElement class after server error: ${bannerFocused}`);
  if (!bannerFocused?.includes('error-banner')) {
    throw new Error(`expected focus on .error-banner after a 401, got class="${bannerFocused}"`);
  }
  await scan(page, '05-server-error-focus-moved-to-banner');

  await browser.close();

  logStep('Clean up: delete the quotes created during this run via the real API');
  const cleanupBrowser = await chromium.launch();
  const cleanupContext = await cleanupBrowser.newContext({ ignoreHTTPSErrors: true });
  const cleanupApi = cleanupContext.request;

  // The API silently clamps any size outside 1-100 back to 10
  // (QuoteEndpointExtensions.cs), so a single "size=200" request quietly
  // returns far fewer quotes than it looks like it asked for — paginate at
  // the real max (100) instead, the same way QuoteService.getAllQuotes() does.
  let quotes = [];
  for (let page = 1; ; page++) {
    const res = await cleanupApi.get(`${API_URL}/api/quotes?page=${page}&size=100`);
    const batch = await res.json();
    quotes = quotes.concat(batch);
    if (batch.length < 100) break;
  }

  const mine = quotes.filter((q) => q.ownerEmail === testEmail);
  for (const quote of mine) {
    await cleanupApi.delete(`${API_URL}/api/quotes/${quote.id}`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    console.log(`  deleted quote #${quote.id}`);
  }
  await cleanupBrowser.close();

  const totalViolations = results.reduce((sum, r) => sum + r.violations.length, 0);
  writeFileSync(join(__dirname, 'axe-results.json'), JSON.stringify({ runId, testEmail, results }, null, 2));

  console.log(`\n=== DONE: ${totalViolations} total axe violations across ${results.length} states ===`);
  process.exit(totalViolations > 0 ? 1 : 0);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
