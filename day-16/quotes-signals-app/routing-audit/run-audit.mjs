// Evidence script for Day-16 Task 1: routing, lazy loading, guards.
//
// Drives the REAL running dev server (http://localhost:4216) and the REAL
// QuotesApi backend (https://localhost:7210) with a headless browser — no
// mocked HTTP, no fake auth. A fresh user is registered against the real
// /api/auth/register endpoint.
//
// Proves, against the real app:
//   1. The unauthenticated guard: visiting /quotes/:id with no session
//      redirects to /login (authGuard in auth/auth.guard.ts).
//   2. Lazy loading: the quote-detail-page chunk is NOT requested on first
//      load of /quotes, and IS requested only once the user navigates to
//      /quotes/:id (network request log, not just the build output).
//   3. Route params: clicking a quote in the list navigates to
//      /quotes/{the real numeric id from the API}, and the rendered detail
//      matches that same quote's real author/text from GET /api/quotes/{id}.
//   4. View Transitions: document.startViewTransition is actually invoked
//      by the Angular Router during the list -> detail navigation
//      (withViewTransitions() in app.config.ts).
//
// Usage: node routing-audit/run-audit.mjs
// Requires: the day-16 Angular dev server on :4216 and the real QuotesApi
// on :7210 already running.

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
const testEmail = `routing-audit-${runId}@example.com`;
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

async function main() {
  const browser = await chromium.launch();

  // ---------------------------------------------------------------------
  // Check 1: unauthenticated guard on /quotes/:id
  // ---------------------------------------------------------------------
  logStep('Check 1: unauthenticated visit to /quotes/123 redirects to /login');
  {
    const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
    const page = await context.newPage();
    await page.goto(`${APP_URL}/quotes/123`);
    await page.waitForURL('**/login', { timeout: 10000 });
    const url = new URL(page.url());
    await page.screenshot({ path: join(SHOTS_DIR, '01-unauthenticated-redirected-to-login.png'), fullPage: true });
    record('unauthenticated /quotes/123 -> redirected to /login', url.pathname === '/login', `landed on ${url.pathname}`);
    await context.close();
  }

  // ---------------------------------------------------------------------
  // Register a throwaway real user against the real API, seed the session
  // ---------------------------------------------------------------------
  logStep('Register a throwaway real user against the real API');
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
  const apiContext = context.request;
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

  // Create a real quote via the real API so the list/detail have known content to assert on.
  logStep('Create a real quote via the real API to navigate to');
  const createRes = await apiContext.post(`${API_URL}/api/quotes`, {
    data: { author: 'Routing Audit Author', text: `Routing audit quote ${runId}` },
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  if (!createRes.ok()) {
    throw new Error(`create quote failed: ${createRes.status()} ${await createRes.text()}`);
  }
  const createdQuote = await createRes.json();
  console.log(`  created quote #${createdQuote.id}`);

  // ---------------------------------------------------------------------
  // Check 2: lazy loading — detail chunk is absent until navigation
  // ---------------------------------------------------------------------
  logStep('Check 2: quote-detail-page chunk is not requested on /quotes, only after navigating to detail');
  const requestedScripts = [];
  page.on('request', (req) => {
    if (req.resourceType() === 'script') requestedScripts.push(req.url());
  });

  await page.goto(`${APP_URL}/quotes`);
  await page.waitForSelector('.quote-item');
  await page.screenshot({ path: join(SHOTS_DIR, '02-quotes-list.png'), fullPage: true });

  const scriptsBeforeNav = [...requestedScripts];
  const detailChunkBefore = scriptsBeforeNav.some((u) => /quote-detail-page/i.test(u));
  record(
    'no quote-detail-page-* chunk requested while on /quotes',
    !detailChunkBefore,
    `${scriptsBeforeNav.length} script requests so far`,
  );

  // ---------------------------------------------------------------------
  // Check 4 setup: detect document.startViewTransition being invoked
  // ---------------------------------------------------------------------
  await page.evaluate(() => {
    window.__viewTransitionCalls = 0;
    if (document.startViewTransition) {
      const original = document.startViewTransition.bind(document);
      document.startViewTransition = (cb) => {
        window.__viewTransitionCalls += 1;
        return original(cb);
      };
    }
  });

  // ---------------------------------------------------------------------
  // Check 3: click the real quote card, verify route param + content
  // ---------------------------------------------------------------------
  logStep('Check 3: clicking the quote card navigates via a real route param and shows the right quote');
  const link = page.locator(`a.quote-item[href="/quotes/${createdQuote.id}"]`);
  await link.waitFor({ timeout: 10000 });
  await link.click();
  await page.waitForURL(`**/quotes/${createdQuote.id}`, { timeout: 10000 });

  const urlAfterNav = new URL(page.url());
  record('URL becomes /quotes/{real numeric id}', urlAfterNav.pathname === `/quotes/${createdQuote.id}`, urlAfterNav.pathname);

  await page.waitForSelector('.detail-text');
  const detailText = await page.locator('.detail-text').textContent();
  const detailAuthor = await page.locator('.detail-author').textContent();
  record(
    'detail page renders the same text the real API returned for that id',
    !!detailText && detailText.includes(createdQuote.text),
    detailText ?? 'null',
  );
  record(
    'detail page renders the same author the real API returned for that id',
    !!detailAuthor && detailAuthor.includes(createdQuote.author),
    detailAuthor ?? 'null',
  );
  await page.screenshot({ path: join(SHOTS_DIR, '03-quote-detail-page.png'), fullPage: true });

  const scriptsAfterNav = requestedScripts;
  const detailChunkAfter = scriptsAfterNav.some((u) => /quote-detail-page/i.test(u));
  record(
    'quote-detail-page-* chunk WAS requested once the detail route was visited',
    detailChunkAfter,
    scriptsAfterNav.filter((u) => /quote-detail-page/i.test(u)).join(', ') || 'none found',
  );

  // ---------------------------------------------------------------------
  // Check 4: View Transition was actually invoked for that navigation
  // ---------------------------------------------------------------------
  logStep('Check 4: document.startViewTransition was invoked by the router navigation');
  const viewTransitionCalls = await page.evaluate(() => window.__viewTransitionCalls);
  record('document.startViewTransition called at least once during list -> detail nav', viewTransitionCalls >= 1, `calls=${viewTransitionCalls}`);

  // ---------------------------------------------------------------------
  // Check 5: guard also protects /quotes/:id after logging out
  // ---------------------------------------------------------------------
  logStep('Check 5: after logout, revisiting the same /quotes/:id URL redirects to /login again');
  await page.evaluate(() => localStorage.removeItem('quotes-app.session'));
  await page.goto(`${APP_URL}/quotes/${createdQuote.id}`);
  await page.waitForURL('**/login', { timeout: 10000 });
  const urlAfterLogout = new URL(page.url());
  record('logged-out visit to the same /quotes/:id redirects to /login', urlAfterLogout.pathname === '/login', urlAfterLogout.pathname);
  await page.screenshot({ path: join(SHOTS_DIR, '04-logged-out-redirected-to-login.png'), fullPage: true });

  await context.close();

  // ---------------------------------------------------------------------
  // Clean up: delete the quote created during this run via the real API
  // ---------------------------------------------------------------------
  logStep('Clean up: delete the quote created during this run via the real API');
  const cleanupBrowser = await browser.newContext({ ignoreHTTPSErrors: true });
  await cleanupBrowser.request.delete(`${API_URL}/api/quotes/${createdQuote.id}`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  console.log(`  deleted quote #${createdQuote.id}`);
  await cleanupBrowser.close();

  await browser.close();

  writeFileSync(join(__dirname, 'routing-audit-results.json'), JSON.stringify(report, null, 2));

  const failed = report.checks.filter((c) => !c.pass);
  console.log(`\n=== DONE: ${report.checks.length - failed.length}/${report.checks.length} checks passed ===`);
  process.exit(failed.length > 0 ? 1 : 0);
}

main().catch((err) => {
  console.error(err);
  writeFileSync(join(__dirname, 'routing-audit-results.json'), JSON.stringify(report, null, 2));
  process.exit(1);
});
