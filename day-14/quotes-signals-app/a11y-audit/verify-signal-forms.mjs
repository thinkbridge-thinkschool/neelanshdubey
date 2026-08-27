// Manual verification script for CreateQuoteSignalComponent (/quotes/new-signal).
//
// Drives the REAL running dev server (http://localhost:4214) and the REAL
// QuotesApi backend (https://localhost:7210) with a headless browser — no
// mocked HTTP, no fake auth. Mirrors a11y-audit/run-audit.mjs's approach for
// the reactive-forms version, but is a one-off state walkthrough (pristine,
// dirty/touched, validator firing, error display, clean submit, failed
// submit) rather than a re-runnable CI-style audit.
//
// Usage: node a11y-audit/verify-signal-forms.mjs
// Requires: the day-14 Angular dev server on :4214 and the real QuotesApi
// on :7210 already running.

import { chromium } from 'playwright';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SHOTS_DIR = join(__dirname, 'screenshots-signal');
mkdirSync(SHOTS_DIR, { recursive: true });

const APP_URL = 'http://localhost:4214';
const API_URL = 'https://localhost:7210';
const runId = Math.random().toString(36).slice(2, 8);
const testEmail = `signal-verify-${runId}@example.com`;
const testPassword = 'verifypass123';

const notes = [];

function logStep(name) {
  console.log(`\n=== ${name} ===`);
  notes.push({ step: name });
}

async function shot(page, name) {
  const path = join(SHOTS_DIR, `${name}.png`);
  await page.screenshot({ path, fullPage: true });
  console.log(`  screenshot: ${path}`);
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

  logStep('Navigate to /quotes/new-signal');
  await page.goto(`${APP_URL}/quotes/new-signal`);
  await page.waitForSelector('#author');

  // --- State 1: pristine ---
  const pristineAuthorInvalid = await page.locator('#author').getAttribute('aria-invalid');
  const pristineErrorVisible = await page.locator('#author-error').count();
  console.log(`  pristine: author aria-invalid=${pristineAuthorInvalid}, #author-error present=${pristineErrorVisible > 0}`);
  notes.push({ state: 'pristine', authorAriaInvalid: pristineAuthorInvalid, errorSpanPresent: pristineErrorVisible > 0 });
  await shot(page, '01-pristine');

  // --- State 2: dirty (typed then cleared) but not yet touched-away ---
  logStep('Type into author, then clear it (dirty, not yet blurred elsewhere)');
  await page.locator('#author').fill('x');
  await page.locator('#author').fill('');
  const dirtyStillFocusedInvalid = await page.locator('#author').getAttribute('aria-invalid');
  console.log(`  after typing+clearing while still focused: aria-invalid=${dirtyStillFocusedInvalid}`);
  notes.push({ state: 'dirty-still-focused', authorAriaInvalid: dirtyStillFocusedInvalid });
  await shot(page, '02-dirty-still-focused');

  // --- State 3: touched (blur away) with validator firing ---
  logStep('Blur away from the empty, dirty author field — required validator should now show');
  await page.locator('#text').focus();
  await page.waitForSelector('#author[aria-invalid="true"]');
  const touchedError = await page.locator('#author-error').textContent();
  console.log(`  author error after blur: "${touchedError?.trim()}"`);
  notes.push({ state: 'touched-empty-author', errorText: touchedError?.trim() });
  await shot(page, '03-touched-required-error');

  // --- State 4: maxlength validator firing ---
  logStep('Type 201 characters into author — maxlength validator should fire');
  await page.locator('#author').fill('a'.repeat(201));
  await page.locator('#text').focus();
  await page.waitForSelector('#author[aria-invalid="true"]');
  const maxLenError = await page.locator('#author-error').textContent();
  console.log(`  author error at 201 chars: "${maxLenError?.trim()}"`);
  notes.push({ state: 'author-over-maxlength', errorText: maxLenError?.trim() });
  await shot(page, '04-maxlength-error');

  // --- State 5: keyboard-only submit-when-invalid moves focus to first invalid field ---
  logStep('Fix author, leave text empty, submit via Enter — focus should move to #text');
  await page.locator('#author').fill('Maya Angelou');
  await page.locator('#text').fill('');
  await page.locator('#text').focus();
  await page.locator('#text').blur();
  await page.locator('button[type="submit"]').focus();
  await page.keyboard.press('Enter');
  await page.waitForSelector('#text[aria-invalid="true"]');
  const focusedAfterInvalidSubmit = await page.evaluate(() => document.activeElement?.id);
  console.log(`  document.activeElement after submitting with empty text: #${focusedAfterInvalidSubmit}`);
  notes.push({ state: 'submit-invalid', focusedId: focusedAfterInvalidSubmit });
  if (focusedAfterInvalidSubmit !== 'text') {
    throw new Error(`expected focus on #text, got #${focusedAfterInvalidSubmit}`);
  }
  await shot(page, '05-submit-invalid-focus-on-text');

  // --- State 6: submitting + clean success ---
  logStep('Fill both fields, delay the real request, observe "submitting", then success');
  await page.locator('#text').fill('Still I rise.');

  await page.route('**/api/quotes', async (route) => {
    await new Promise((r) => setTimeout(r, 900));
    await route.continue();
  });

  await page.locator('button[type="submit"]').click();
  await page.waitForSelector('button[type="submit"][aria-busy="true"]');
  const submitDisabled = await page.locator('button[type="submit"]').isDisabled();
  console.log(`  submit button disabled while submitting: ${submitDisabled}`);
  notes.push({ state: 'submitting', buttonDisabled: submitDisabled });
  await shot(page, '06-submitting');

  await page.waitForSelector('.success-banner', { timeout: 10000 });
  await page.unroute('**/api/quotes');
  const successText = await page.locator('.success-banner p').first().textContent();
  console.log(`  success banner: ${successText?.trim()}`);
  notes.push({ state: 'success', bannerText: successText?.trim() });
  await shot(page, '07-success');

  // --- State 7: "Add another" resets and refocuses author ---
  logStep('Click "Add another quote" — form should reset and author should refocus');
  await page.locator('text=Add another quote').click();
  await page.waitForSelector('#author');
  const authorValueAfterReset = await page.locator('#author').inputValue();
  const focusedAfterReset = await page.evaluate(() => document.activeElement?.id);
  console.log(`  #author value after reset: "${authorValueAfterReset}", focused: #${focusedAfterReset}`);
  notes.push({ state: 'create-another', authorValue: authorValueAfterReset, focusedId: focusedAfterReset });
  await shot(page, '08-reset-after-create-another');

  // --- State 8: failed submit (real 401 from a corrupted token) ---
  logStep('Corrupt the stored token to provoke a real 401, then re-submit');
  await page.evaluate(() => {
    const raw = localStorage.getItem('quotes-app.session');
    const session = JSON.parse(raw);
    session.accessToken = 'this-is-not-a-valid-jwt';
    localStorage.setItem('quotes-app.session', JSON.stringify(session));
  });
  await page.reload();

  let sawStatus = null;
  page.on('response', (res) => {
    if (res.url().includes('/api/quotes') && res.request().method() === 'POST') {
      sawStatus = res.status();
      console.log(`  POST /api/quotes -> ${res.status()}`);
    }
  });

  await page.waitForSelector('#author');
  await page.locator('#author').fill('Maya Angelou');
  await page.locator('#text').fill('Still I rise, again.');
  await page.locator('button[type="submit"]').click();
  await page.waitForSelector('.error-banner');

  const bannerText = await page.locator('.error-banner').textContent();
  const bannerFocusedClass = await page.evaluate(() => document.activeElement?.className);
  console.log(`  error banner text: "${bannerText?.trim()}"`);
  console.log(`  document.activeElement class after server error: ${bannerFocusedClass}`);
  notes.push({ state: 'server-error', status: sawStatus, bannerText: bannerText?.trim(), focusedClass: bannerFocusedClass });
  if (!bannerFocusedClass?.includes('error-banner')) {
    throw new Error(`expected focus on .error-banner after a failed submit, got class="${bannerFocusedClass}"`);
  }
  await shot(page, '09-server-error-focus-moved-to-banner');

  await browser.close();

  logStep('Clean up: delete quotes created during this run via the real API');
  const cleanupBrowser = await chromium.launch();
  const cleanupContext = await cleanupBrowser.newContext({ ignoreHTTPSErrors: true });
  const cleanupApi = cleanupContext.request;

  let quotes = [];
  for (let p = 1; ; p++) {
    const res = await cleanupApi.get(`${API_URL}/api/quotes?page=${p}&size=100`);
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

  writeFileSync(join(__dirname, 'signal-forms-verification-notes.json'), JSON.stringify({ runId, testEmail, notes }, null, 2));
  console.log('\n=== DONE ===');
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
