# Day-16 Task 2 — state management, signals first: verification note

## The feature

Real, paginated browsing of `GET /api/quotes?page&size` (`Day-5/QuotesApi`,
`QuoteEndpointExtensions.cs`) — Next/Previous through the actual quote
collection, replacing the old "fetch the first 100 and call it done" list.
State lives in `src/app/quotes/quotes-page.state.ts` — a plain
`@Injectable()` using signals, `provided: [QuotesPageState]` on
`QuotesListComponent` (component-scoped: created fresh per visit to
`/quotes`, thrown away on navigation, no root singleton, no store library).

Fields exposed as readonly signals: `page`, `quotes`, `loading`, `error`,
plus computed `hasNextPage` / `hasPrevPage`. Methods: `next()`, `prev()`,
`retry()`.

## The brief given to the agent

> Direct an AI agent (Claude Code) to model a small feature's state with
> signals + a service against your real Week-1 API, then verify and
> defend what it built [...] Brief it on your real feature and the actual
> endpoints/fields it touches.

Endpoint: `GET /api/quotes?page={n}&size={n}`. Fields touched: the
response is a bare `Quote[]` — no total-count field anywhere in the
contract (confirmed by reading `QuoteEndpointExtensions.cs` and the
existing `quote.service.ts`, which already documents the same
inconsistency for `getAllQuotes()`).

## A bug caught on review

First pass computed `hasNextPage` as `quotes().length === PAGE_SIZE` —
reasonable-looking, wrong in one real case. The API gives no total count,
so a *short* page reliably proves it's the last one, but a *full* page
does **not** prove there's a next one. If the true quote count is an exact
multiple of `PAGE_SIZE` (e.g. exactly 20 quotes at page size 10), the real
last page still comes back full, `hasNextPage` stays `true` forever, and
clicking Next lands on a blank page with no explanation and no way to
tell it's the end.

Fixed in `quotes-page.state.ts`: when a requested page (page > 1) comes
back empty, the service now records that the *previous* page was the real
last one and does not commit the empty result — the user stays on their
last valid page and `hasNextPage` flips to `false`. Pinned down by
`recovers when the true last page happens to be an exact multiple of
pageSize` in `quotes-page.state.spec.ts`. (The real dataset — 16 quotes —
doesn't happen to trigger this today, which is exactly why it needed a
synthetic test rather than relying on the live audit to catch it.)

## My judgment call: signals+service vs. signal-store / NgRx

This is mine to own, not the agent's — it drafted a version of this, I'm
stating the actual rule I'd apply and why.

**Stay with plain signals + an injectable service** as long as all of
these hold:
1. The state belongs to one feature/route and nothing outside it needs to
   read or mutate it — `QuotesPageState` is thrown away the moment you
   leave `/quotes`; nothing else in the app has ever needed to know what
   page you were on.
2. Nobody needs a time-travel/action-replay debugger to answer "what
   sequence of events got us into this state" for a support ticket. If a
   pagination bug shows up, `console.log`-ing the four signals and reading
   the two methods that touch them is enough.
3. The service stays small enough that a new contributor can read the
   whole file top to bottom and know every way the state changes —
   roughly: fits on one screen, under ten public methods. `QuotesPageState`
   is four signals and four methods.
4. I'm not hand-rolling the same defensive pattern — request
   sequencing/staleness guards, in this case — in three or more places
   across the app. One instance of a clever guard is a fine one-off; three
   independent copies is a sign it should become a shared, tested
   primitive instead of copy-paste.

**Move to `@ngrx/signals` (`signalStore`) or NgRx** the moment any of
these becomes true:
- The *same* state needs to be read or mutated from genuinely unrelated
  feature areas at once (not just a parent composing a child component) —
  e.g. a user's session/role needing live updates from auth, billing, and
  an admin-impersonation flow simultaneously. Wiring that by hand with
  injectable services turns into "who updates whom" spaghetti fast.
- A production bug report needs a real audit trail — devtools time-travel
  or a serializable action log — because "read the current signal values"
  isn't enough to reconstruct what happened.
- Two or more features need one entity kept in sync everywhere it's shown
  at once (the same `Quote` edited in a list, a detail page, and a
  favorites drawer all updating instantly) — that's what `withEntities` /
  NgRx's entity adapters exist for; hand-rolling it is writing a small ORM.
- The request-sequencing pattern in this file (`latestRequestId`, the
  overshoot guard) needs to be copy-pasted into a third unrelated feature
  service. At that point it's not "small feature state" anymore, it's an
  unwritten library.

**For this app today:** one developer, four screens, one feature
(`/quotes`) with real pagination state, nothing else in the app reads or
depends on it. None of the four "move to a store" conditions are met. I'm
not introducing NgRx or `@ngrx/signals` here — the plain signals + service
approach in `quotes-page.state.ts` is the right amount of machinery, and
adding a store now would be solving a scaling problem this app doesn't
have yet.

## Verification and defense — not taken on the agent's word

### 1. Unit tests — `npx ng test --watch=false`

88/88 passing across 15 spec files. The load-bearing one is
`quotes-page.state.spec.ts`:
- loads page 1 on construction, with the real `page`/`size` params;
- `hasNextPage`/`hasPrevPage` correctly derived from a full vs. short page;
- `next()`/`prev()` are no-ops at the boundaries and while a request is
  already in flight (no duplicate requests fired — verified with
  `httpMock.expectNone`);
- the exact-multiple-of-pageSize false positive (the bug above), fixed
  and pinned;
- real HTTP error messages surface via `error()`, and `retry()` recovers;
- **out-of-order guard**: two overlapping `loadPage()` calls resolved
  *out of order* (the later-issued one resolves first) — asserts the
  earlier, now-stale response cannot overwrite the state the later one
  already committed.

### 2. Build output — `build-output.log`

Production `ng build`: `quotes-list-component` is still its own lazy
chunk (grew from ~3.8kB to ~5.9kB — the new state service is bundled
into it, not the initial bundle, confirming it's genuinely
component-scoped and not accidentally promoted to root).

### 3. Runtime proof against the real dev server + real API — `run-audit.mjs`

`node state-audit/run-audit.mjs` drives a headless browser against the
real `ng serve` (port 4216) and the real `QuotesApi` backend (port 7210).
All 8 checks passed on the last run:

1. `/quotes` loads a real full page of 10 (the DB has 16 real quotes at
   the time of the run); Previous starts disabled.
2. Next fires an observed real `GET /api/quotes?page=2&size=10` request
   and renders the real short remainder (6 quotes); Next disables itself.
3. Previous returns to the real, identical page 1.
4. **The out-of-order guard holds in the compiled app, not just the unit
   test**: the real page-2 request is intercepted and delayed 1.5s, then
   `QuotesPageState.loadPage(2)` and `loadPage(1)` are invoked directly
   (via Angular's `window.ng.getComponent()` dev API, bypassing the
   disabled buttons the same way the unit test bypasses the public
   `next()`/`prev()` gating) so page 1 resolves first — asserted that
   page 1 "wins" immediately, and that the slow, stale page-2 response
   changes nothing when it finally arrives.

Screenshots: `screenshots/01-page-1.png`, `02-page-2.png`,
`03-after-race-still-page-1.png`. Results: `state-audit-results.json`.

### How to re-run this yourself

```
# terminal 1 — real backend
cd Day-5/QuotesApi && dotnet run --launch-profile https

# terminal 2 — real frontend
cd day-16/quotes-signals-app && npx ng serve --port 4216

# terminal 3 — evidence script
cd day-16/quotes-signals-app && node state-audit/run-audit.mjs
```

### Manual DevTools check

With both servers running, open `/quotes`: Previous is disabled, Next is
enabled. Open Network, filter to Fetch/XHR, click Next — a single real
`GET /api/quotes?page=2&size=10` fires; if your real dataset's total is a
multiple of 10, page 2 also looks full and Next stays enabled — click it
again and confirm you stay on page 2 rather than seeing a blank page.
