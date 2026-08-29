# Managed-identity API proxy

Standalone Azure Function App (`ai-quotes-func2`, resource group `ai-quotes-api`,
Windows Consumption plan — Linux Consumption 503'd persistently on this
subscription, a platform-level restriction, not a config issue; Windows came
up on the first request) — **not** deployed as this Static Web App's own
managed Functions backend, because SWA's Free tier cannot assign a managed
identity to its co-located Functions API (`SkuCode 'Free' is invalid` from
`az staticwebapp identity assign` — that capability needs the Standard tier).

## What it does

One HTTP-triggered function (`apiProxy`, route `{*segments}`) fronts every
`/api/*` call the Angular app makes:

- `GET /api/quotes`, `GET /api/quotes/{id}` (anonymous on the real API today):
  re-signed with a real Azure AD token acquired via `DefaultAzureCredential()`
  — this Function App's own system-assigned managed identity — for audience
  `api://47f5632f-7592-4f54-b328-cf7b71139f4a`. That's the exact `EntraJwt`
  audience already wired in `Day-5/QuotesApi/Extentions/InfrastructureExtensions.cs`;
  confirmed via `az ad sp show` that the app registration's
  `appRoleAssignmentRequired` is `false`, so no extra permission grant was
  needed for a new identity to get a token for it.
- Everything else (`/api/auth/login`, `/api/auth/register`, `POST/PUT/DELETE
  /api/quotes...`): forwarded as-is, including the browser's own bearer token
  when present — these are genuine per-end-user actions; a service identity
  has no business standing in for the user.
- `GET /api/_debug/entra-token`: verification-only, returns the *decoded
  claims* of the same managed-identity token (never the raw token) — proof
  the mechanism works, checkable by anyone without needing Azure Portal
  access.

No CORS change was needed on the real Container App — it never talks to the
browser directly, only to this Function App, server to server.

## No secrets anywhere

Zero connection strings, API keys, or client secrets in this repo or in this
Function App's settings. The only "credential" is `DefaultAzureCredential()`
resolving the platform-assigned managed identity at runtime — nothing to
rotate, nothing to leak.

## Why the browser needs an absolute URL for this, not a relative `/api/*`

This Function App is a **separate origin** from the Static Web App
(`ai-quotes-func2.azurewebsites.net` vs `*.azurestaticapps.net`) — unlike SWA's
own managed/linked Functions feature, a standalone Function App isn't
automatically reachable at the SWA's own `/api/*` path. See
`src/app/interceptors/api-base-url.interceptor.ts`: it rewrites `/api/*`
requests to this Function App's absolute origin, but only when the app isn't
running on `localhost` — locally, `proxy.conf.json` already forwards `/api/*`
to the real backend unchanged, so the same build works in both places with no
environment-file branching.
