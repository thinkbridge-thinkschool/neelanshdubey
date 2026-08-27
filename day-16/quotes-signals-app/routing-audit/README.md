# Day-16 Task 1 — routing, lazy loading, guards: verification note

This app is a copy of `day-15/quotes-signals-app` (same real backend, same
`AuthService`/`QuoteService`/interceptors), extended with route-level
lazy loading, route params, and a router-driven View Transition, then
verified against the real Week-1 API — not mocked.

## Real API this was built against

- Backend: `Day-5/QuotesApi`, proxied at `/api` -> `https://localhost:7210`
  (`proxy.conf.json`, unchanged from day-15).
- List endpoint: `GET /api/quotes?page&size`.
- Detail endpoint: `GET /api/quotes/{id}`.
- Route param: `id` — the `Quote.id` field the API returns (see
  `src/app/models/quote.model.ts`), a plain integer.

## What was built

| Requirement | Where |
|---|---|
| Lazy-loaded routes | `src/app/app.routes.ts` — every route still uses `loadComponent()`; added `quotes/:id -> quote-detail-page.component.ts` |
| Functional auth guard | `src/app/auth/auth.guard.ts` (unchanged from day-15) — now also applied to `quotes/:id` |
| Route params | `quote-detail-page.component.ts` — `id = input.required<string>()`, bound straight from the `:id` segment via `withComponentInputBinding()` |
| View Transition | `app.config.ts` — `provideRouter(routes, withComponentInputBinding(), withViewTransitions())`; `quotes-list.component.html` and `quote-detail-page.component.html` set a matching `[style.view-transition-name]="'quote-' + id"` per quote so the browser morphs the *specific* clicked card into the detail page, not just a generic crossfade |

The quotes list (`quotes-list.component.ts`) no longer embeds the detail
panel inline via a `selectedId` signal (that was same-route, not a real
navigation). Clicking a quote is now a real `routerLink` to `/quotes/:id`,
so there is an actual route change for the guard, the lazy chunk, and the
view transition to apply to.

## Evidence

### 1. Unit tests — `npx ng test --watch=false`

82/82 tests pass across 14 spec files, including three new/updated specs
that exercise this task directly:

- `src/app/quotes/quote-detail-page.component.spec.ts` (new) — parses the
  route-bound string id, fetches the right quote, and shows an
  invalid-id message instead of fetching for a non-numeric param.
- `src/app/quotes/quotes-list.component.spec.ts` (updated) — asserts each
  rendered card is a real `<a href="/quotes/{id}">`, not a click handler.
- `src/app/auth/auth.guard.spec.ts` (new) — `authGuard` returns a
  `UrlTree` to `/login` with no session, and `true` once a session exists.

### 2. Build output proves real code-splitting — `build-output.log`

Production `ng build` output (captured in this folder) lists
`quote-detail-page-component` as its own **lazy chunk file**, separate
from `quotes-list-component`, `login-component`, `search-component`, and
`create-quote-component` — confirmed at the bundler level, not just by
reading the route config.

### 3. Runtime proof against the real dev server + real API — `run-audit.mjs`

`node routing-audit/run-audit.mjs` drives a headless browser against the
real `ng serve` dev server (port 4216) and the real `QuotesApi` backend
(port 7210) — registers a throwaway real user via `/api/auth/register`,
creates a real quote via `/api/quotes`, and deletes it again at the end.
Results: `routing-audit-results.json`, screenshots in `screenshots/`.

All 8 checks passed on the last run (`runId: s6pxux`):

1. **Guard, unauthenticated:** `GET /quotes/123` with no session ->
   redirected to `/login` (`01-unauthenticated-redirected-to-login.png`).
2. **Lazy loading, before nav:** while sitting on `/quotes`, no script
   request matching `quote-detail-page` was ever issued (checked against
   every `request` event Playwright observed, not just the visible
   Network panel) (`02-quotes-list.png`).
3. **Route param:** clicking the real quote (server id `#57`) navigated
   the URL to `/quotes/57` — the literal numeric id from the API
   response, not a client-side index.
4. **Route param -> correct data:** the detail page's rendered text and
   author matched that same quote's real `GET /api/quotes/57` response
   (`03-quote-detail-page.png`).
5. **Lazy loading, after nav:** a `quote-detail-page` component request
   *was* observed, and only after that navigation.
6. **View Transition actually invoked:** `document.startViewTransition`
   was monkey-patched before the click; it was called exactly once for
   the list -> detail navigation, driven by the router's
   `withViewTransitions()`, not by hand-rolled CSS.
7. **Guard, after logout:** clearing the session and revisiting the same
   `/quotes/57` URL redirects to `/login` again
   (`04-logged-out-redirected-to-login.png`).

### How to re-run this yourself

```
# terminal 1 — real backend
cd Day-5/QuotesApi && dotnet run

# terminal 2 — real frontend
cd day-16/quotes-signals-app && npx ng serve --port 4216

# terminal 3 — evidence script
cd day-16/quotes-signals-app && node routing-audit/run-audit.mjs
```

### Manual DevTools check (what the task asked to eyeball directly)

With the dev server running, open DevTools -> Network, filter to JS, load
`/quotes` — no `quote-detail-page` request appears. Click any quote card:
a new request for that chunk appears only at that moment, the URL bar
updates to `/quotes/{id}`, and the card visibly morphs into the detail
page instead of a hard cut (the View Transition). Open a private window
(no session) and navigate straight to `/quotes/5` — it lands on `/login`.
