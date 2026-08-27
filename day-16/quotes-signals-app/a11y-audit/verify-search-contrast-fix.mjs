// One-off verification for the contrast fix reported via axe DevTools on
// /search (.avatar, .filter-item--active, .primary-button, .danger-button
// all used var(--color-primary)/var(--color-danger) + white text at
// 2.5-3.2:1, below WCAG AA's 4.5:1). Confirms the new "-strong" tokens
// (styles.css) actually clear the bar on the real rendered page — not a
// permanent fixture, just proof for this fix.
import { chromium } from 'playwright';
import AxeBuilder from '@axe-core/playwright';

const APP_URL = 'http://localhost:4214';
const API_URL = 'https://localhost:7210';
const runId = Math.random().toString(36).slice(2, 8);
const testEmail = `contrast-fix-${runId}@example.com`;

async function main() {
  const browser = await chromium.launch();
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  const api = context.request;

  const registerRes = await api.post(`${API_URL}/api/auth/register`, {
    data: { email: testEmail, password: 'auditpass123' },
  });
  const { accessToken, refreshToken } = await registerRes.json();

  await page.goto(APP_URL);
  await page.evaluate(
    ({ accessToken, refreshToken, email }) =>
      localStorage.setItem('quotes-app.session', JSON.stringify({ accessToken, refreshToken, email })),
    { accessToken, refreshToken, email: testEmail },
  );

  await page.goto(`${APP_URL}/search`);
  await page.waitForSelector('.avatar');

  console.log('-- default view: .avatar + .filter-item--active --');
  let results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze();
  console.log(`violations: ${results.violations.length}`);
  results.violations.forEach((v) => v.nodes.forEach((n) => console.log('  ', n.target, n.failureSummary)));

  console.log('-- add-quote modal open: .primary-button --');
  await page.locator('.add-button').click();
  await page.waitForSelector('.primary-button');
  results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze();
  console.log(`violations: ${results.violations.length}`);
  results.violations.forEach((v) => v.nodes.forEach((n) => console.log('  ', n.target, n.failureSummary)));

  // Create a real quote (direct API call — the modal form itself isn't
  // under test here) so there's something this user can delete, to render
  // .danger-button in the delete-confirm modal.
  await page.keyboard.press('Escape').catch(() => {});
  const created = await api.post(`${API_URL}/api/quotes`, {
    headers: { Authorization: `Bearer ${accessToken}` },
    data: { author: 'Contrast Fix Check', text: 'Verifying the danger-button fix.' },
  });
  const createdQuote = await created.json();
  await page.reload();
  await page.waitForSelector('.avatar');

  console.log('-- delete-confirm modal open: .danger-button --');
  await page.locator('.delete-link').first().click();
  await page.waitForSelector('.danger-button');
  results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze();
  console.log(`violations: ${results.violations.length}`);
  results.violations.forEach((v) => v.nodes.forEach((n) => console.log('  ', n.target, n.failureSummary)));

  await api.delete(`${API_URL}/api/quotes/${createdQuote.id}`, { headers: { Authorization: `Bearer ${accessToken}` } }).catch(() => {});
  await browser.close();
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
