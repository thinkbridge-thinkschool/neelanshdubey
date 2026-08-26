# Accessibility audit — CreateQuoteComponent (`/quotes/new`)

Automated proof for the a11y requirements in the Day 14 Task 1 brief
(labels, `aria-invalid`/`aria-describedby`, keyboard operability, focus
moved to the first error on submit). Run with:

```
node a11y-audit/run-audit.mjs
```

Requires the day-14 Angular dev server (`ng serve`, default `:4214`) and
the real `Day-5/QuotesApi` backend (`:7210`) already running.

## What this is, and isn't

This is a headless-browser + [axe-core](https://github.com/dequelabs/axe-core)
scan, driven with real keyboard input against the real running app and the
real API — it is **not** a substitute for actually listening to NVDA/JAWS/
VoiceOver read the page, which needs a human at a physical machine. What it
does verify, because it's the same DOM contract a screen reader depends on:

- every input has an associated, programmatically-determinable label
- `aria-invalid` and `aria-describedby` are present exactly when a field is
  actually invalid, and point at the right hint/error text
- the whole flow (fill, submit, retry) is reachable by `Tab` / `Enter` alone
  — no mouse events anywhere in the script
- focus lands on the first invalid field after a failed submit, and on the
  server-error banner after a failed request
- WCAG color-contrast thresholds on every scanned state

## How each state was produced (no mocked HTTP)

| # | State | How it's real |
|---|-------|----------------|
| 1 | Empty | Fresh page load, session seeded from a real `POST /api/auth/register` response |
| 2 | Invalid, focus on Author | `Tab` ×4 + `Enter` on an empty form — real client-side validators reject it |
| 3 | Submitting | Real `POST /api/quotes` request, deliberately delayed 900ms in flight (still hits the real backend) so the disabled/spinner state is screenshot-able |
| 4 | Success | The delayed request above actually completes against the real API and returns a real created quote |
| 5 | Server error, focus on banner | The stored JWT is corrupted and the page reloaded (forcing `AuthService` to re-read the bad token), so the resulting `401` is a real rejection from the real backend, not a mock |

Every quote created during the run is deleted again via the real
`DELETE /api/quotes/{id}` endpoint at the end of the script.

## Result

**0 axe-core violations across all 5 states** — see `axe-results.json` and
`screenshots/`.

The first run *did* catch a real, serious issue: the `.submit-button`'s
white text on `var(--color-primary)` (`#5b8cff`) measured **3.16:1**
contrast — below WCAG AA's 4.5:1 minimum for 15px bold text. Fixed in
[`create-quote.component.scss`](../src/app/quotes/create-quote.component.scss)
by darkening the button to `#3f66c9` (5.31:1) / `#4a72d1` on hover (4.55:1),
scoped to this component only. Re-running the audit after the fix confirms
0 violations.

**Not fixed, flagged for follow-up:** `login.component.scss`'s
`.submit-button` uses the exact same `var(--color-primary)` + white-text
pattern and almost certainly has the identical contrast failure — it
wasn't touched here since it's outside this task's scope (the day-13 login
form), but it's the same bug.

## Files

- `run-audit.mjs` — the audit script (re-runnable, self-contained)
- `axe-results.json` — machine-readable violations per state (empty arrays = clean)
- `screenshots/` — one PNG per state, listed in the table above
