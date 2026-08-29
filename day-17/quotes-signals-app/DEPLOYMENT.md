# Day-17 Task 1 — Azure Static Web Apps deployment, verification note

## Live URL

**https://lemon-smoke-0e5e0530f.7.azurestaticapps.net**

No custom domain (per direction — use the default URL). Resource
`ai-quotes-swa`, resource group `ai-quotes-api`, Free tier, GitHub
Actions CI/CD from branch `day-17task1`.

## Architecture

```
Browser → SWA (static Angular app only)
            │  /api/* rewritten to an absolute origin
            │  (api-base-url.interceptor.ts, no-op on localhost)
            ▼
       ai-quotes-func2  (standalone Azure Function App, Windows
       Consumption plan, system-assigned managed identity)
            │
            │  GET /api/quotes, GET /api/quotes/{id}:
            │  Authorization: Bearer <real AAD token via
            │  DefaultAzureCredential(), audience
            │  api://47f5632f-7592-4f54-b328-cf7b71139f4a>
            │
            │  everything else (login/register/create/update/delete):
            │  forwards the browser's own bearer token unchanged
            ▼
       ai-quotes-app  (the real Week-1 API — Day-5/QuotesApi,
       already deployed to Azure Container Apps before this task)
```

**Why a standalone Function App instead of SWA's own managed Functions
API:** SWA's Free tier cannot assign a managed identity to its
co-located Functions backend (`az staticwebapp identity assign` →
`SkuCode 'Free' is invalid` — that's Standard-tier-only). A Consumption-
plan Function App gets a real system-assigned identity for free, so it
hosts the proxy instead; `api-base-url.interceptor.ts` sends the
Angular app's `/api/*` calls there.

## Zero secrets, real proof

`GET /api/_debug/entra-token` (verification-only, decoded claims only,
never the raw token):

```json
{
  "iss": "https://sts.windows.net/f774bb68-0575-4cd2-9d4c-3b4e593d1110/",
  "aud": "api://47f5632f-7592-4f54-b328-cf7b71139f4a",
  "appid": "04e0412b-28f8-4a5e-8ee7-9cf872d45bba",
  "oid": "f4dbd25f-291e-4148-8659-74a451e0e51c",
  "tokenExpiresOn": "2026-08-30T10:42:16.000Z"
}
```

- `iss` matches the real tenant (`az account show`).
- `aud` matches `QuotesApi-1`'s real, pre-existing app registration
  (confirmed live via `az ad app show` — not invented for this task;
  the real API's `EntraJwt` auth scheme in
  `Day-5/QuotesApi/Extentions/InfrastructureExtensions.cs` already
  trusts this exact audience).
- `oid` matches `ai-quotes-func2`'s own system-assigned identity
  `principalId` from `az functionapp identity assign` — i.e. this is
  provably *this Function App's* managed identity, not a hardcoded
  value. `az ad sp show` confirmed `appRoleAssignmentRequired: false`
  on that app registration, so no extra permission grant was even
  needed for a brand-new identity to get a valid token for it.

No API key, connection string, or client secret exists anywhere in
`day-17/quotes-signals-app/` or in the Function App's settings.

## Lighthouse (`deploy-verification/lighthouse-report.html`)

Run against the live `/login` route:

| Category | Score |
|---|---|
| Performance | 99 |
| Accessibility | 97 |
| Best Practices | 100 |
| SEO | 100 |

All four ≥ 95. SEO started at 82 — fixed two real gaps the live run
surfaced (not assumed): a missing `<meta name="description">`, and
`robots.txt` 404-falling-through to the Angular SPA shell (no such file
existed on disk, so SWA's `navigationFallback` served `index.html`
instead — invalid robots syntax to a crawler). Added both; re-ran, 100.

## Verification log — states/edges actually exercised

Not taken on trust — driven with a headless browser against the real
live URL and the real Function App:

1. Live SWA root returns 200 (real HTML, not a placeholder).
2. `robots.txt` returns real robots syntax, not the SPA shell.
3. Logged in with the real seeded user (`Day-5/QuotesApi/Models/UserSeed.cs`
   — the real `ai-quotes-app` container is running an image frozen
   before `POST /api/auth/register` existed, confirmed by hitting that
   endpoint directly and getting a 404 even bypassing this proxy
   entirely, so registration isn't testable against this specific
   deployment; login already works there).
4. `/quotes` renders through the MI-proxied read path.
5. `/quotes/new` create-quote succeeds — the write path, carrying the
   *browser's own* bearer token (not MI) through the proxy unchanged.
6. `GET /api/_debug/entra-token` returns real, verifiable managed-
   identity claims.

Screenshots: `deploy-verification/final-quotes-list.png`,
`final-create-success.png`.

## Two real bugs caught during this work

1. **Interceptor ordering dropped the user's bearer token.**
   `apiBaseUrlInterceptor` was registered outermost, rewriting `/api/*`
   to the Function App's absolute cross-origin URL *before*
   `authInterceptor` ran its own `req.url.startsWith('/api')` same-
   origin check — which then silently stopped matching, so writes
   (create/update/delete) 401'd once deployed, even though the exact
   same request worked fine via `curl` with a manually-attached header.
   Fixed by moving `apiBaseUrlInterceptor` last, right before the real
   HTTP call, after auth/retry/error have already inspected the
   original relative URL.
2. **Azure Functions' HTTP routing doesn't give a literal route
   precedence over `{*segments}`.** A second `app.http()` registration
   at `route: "_debug/entra-token"` was silently shadowed by the
   catch-all proxy function and never invoked (unlike ASP.NET Core's
   own endpoint routing, which this superficially resembles). Folded
   the debug logic into the same handler instead of relying on route
   precedence between two functions.

## Platform quirks hit (not code bugs, but real and worth recording)

- **Linux Consumption Function Apps 503'd persistently on this
  subscription** — matches the same subscription-tier restriction
  `Day-5/QuotesApi/DEPLOYMENT.md` already documented for `az acr build`.
  Windows Consumption came up on the first request.
- **Deleting and recreating a Function App under the identical name
  left Azure's edge front-end still routed to the deleted site** — the
  new resource was healthy (`state: Running`) but 404'd at the edge
  ("Web Site not found") until renamed (`ai-quotes-func` → `ai-quotes-func2`),
  which resolved immediately. A known same-name-recreate quirk, not
  anything wrong with the deployment itself.

## Explicitly out of scope

- No custom domain (default `*.azurestaticapps.net` URL, per direction).
- No changes to the already-deployed `ai-quotes-app` Container App — it
  already accepts real Entra tokens for the audience this proxy uses;
  touching its live, working deployment wasn't needed.
