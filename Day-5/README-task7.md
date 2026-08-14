# Day 5 — Task 7: End-to-End Smoke Test + Week 1 Reflection

## Deployment gap check (do this before smoke-testing)

Compared when the Task 6 resilience commit landed against when the live Container App revision was last created:

| Source | Timestamp |
|---|---|
| Task 6 resilience commit (`367f35c`, "Add Day-5 Task 6: Polly resilience for outbound HTTP calls") | 2026-08-14 21:18:47 +0530 (15:48:47 UTC) |
| Task 6 screenshots commit (`eafee69`) | 2026-08-14 22:18:41 +0530 (16:48:41 UTC) |
| Live revision `quotesapi-dev--0000001` created (`az containerapp revision list`) | 2026-08-14T12:26:43 UTC (17:56:43 +0530) |

**The live revision predates the Task 6 commit.** There is only one revision on the app (still `Active: True`), so nothing has been deployed since 12:26 UTC — the Polly retry/circuit-breaker/timeout pipeline in [`HttpClientExtensions.cs`](QuotesApi/Extentions/HttpClientExtensions.cs) and the `GET /api/quotes/inspiration` endpoint have never been pushed to the live Container App.

This was confirmed empirically during the smoke test below: `GET /api/quotes/inspiration` returns a plain routing `404 Not Found` (`content-length: 0`, no `application/problem+json` body) on the live app — the same shape as a request to a URL that was never registered, not the app's own `Results.NotFound()`/`Results.Problem()` responses used elsewhere.

**Decision (per instruction, asked and confirmed before proceeding):** smoke-test what's currently live, and call out the gap here rather than redeploying. So every result below reflects the pre-Task-6 revision — Task 6's resilience behavior itself was already proven separately in [README-task6.md](README-task6.md) against a local run, not against this deployment.

## Routes tested

Enumerated by grepping `Map(Get|Post|Put|Delete|Patch)\(` across every `*EndpointExtensions.cs` file under `Day-5/QuotesApi/Extentions`, plus the one root route mapped directly in `Program.cs`:

- `GET /` (Program.cs)
- `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout` (AuthEndpointExtensions.cs)
- `GET /api/quotes/inspiration` (InspirationEndpointExtensions.cs — Task 6, not live, see above)
- `GET /api/quotes`, `POST /api/quotes`, `GET /api/quotes/{id}`, `PUT /api/quotes/{id}`, `DELETE /api/quotes/{id}` (QuoteEndpointExtensions.cs)

Base URL confirmed unchanged via `az containerapp show -n quotesapi-dev -g rg-quotesapi-dev --query "properties.configuration.ingress.fqdn"`: `https://quotesapi-dev.whitestone-71ebd55e.centralindia.azurecontainerapps.io`

Auth-required calls used the seeded test account (`UserSeed.cs`: `test@example.com` / synthetic placeholder password), the only credential the app provisions — there is no `/api/auth/register` endpoint.

## Results

| # | Method | Path | Status | Time | Note |
|---|--------|------|--------|------|------|
| 1 | GET | `/` | 200 | 0.36s | Plain-text `"Quotes API is running!"` — this is the closest thing to a health check the app exposes. |
| 2 | GET | `/api/quotes` | 200 | 0.39s | Returned `[]` — no quotes exist. Consistent with SQLite living on the container's ephemeral filesystem (no volume mount in `resources.bicep`); data does not survive a redeploy. |
| 3 | GET | `/api/quotes/1` | 404 | 0.29s | Negative path — no quote with id 1 yet at this point in the run. |
| 4 | GET | `/api/quotes/999999` | 404 | 0.54s | Negative path — nonexistent id, always 404 by design. |
| 5 | GET | `/api/quotes/inspiration` | 404 | 0.15–0.21s | **Route not live** — see deployment gap above. Not an app-level 404; the route simply doesn't exist in the running revision. |
| 6 | POST | `/api/auth/login` (wrong password) | 401 | 0.51s | Negative path. |
| 7 | POST | `/api/auth/login` (correct seeded creds) | 200 | 0.57s | Returned a valid access + refresh token pair. |
| 8 | POST | `/api/quotes` (no token) | 401 | 0.15s | Negative path — write endpoint correctly rejects unauthenticated calls. |
| 9 | POST | `/api/quotes` (valid token) | 201 | 0.28s | Created quote id 1, `ownerId` correctly set from the token's subject claim. |
| 10 | GET | `/api/quotes/1` | 200 | 0.27s | Confirms the quote created in #9 is readable. |
| 11 | PUT | `/api/quotes/1` (no token) | 401 | 0.16s | Negative path. |
| 12 | PUT | `/api/quotes/1` (valid token, owner) | 200 | 0.20s | `RequireAuthorization("can-edit-quotes")` policy passed for the owner; text updated. |
| 13 | PUT | `/api/quotes/999999` (valid token) | 404 | 0.15s | Negative path — authorized but nonexistent id. |
| 14 | DELETE | `/api/quotes/1` (no token) | 401 | 0.21s | Negative path. |
| 15 | DELETE | `/api/quotes/1` (valid token, owner) | 204 | 0.27s | Resource-based `"can-delete-own-quote"` policy passed. |
| 16 | DELETE | `/api/quotes/1` (already deleted) | 404 | 0.18s | Negative path — deleting twice correctly 404s instead of erroring. |
| 17 | POST | `/api/auth/refresh` (valid refresh token) | 200 | 0.37s | Rotated to a new access + refresh token pair. |
| 18 | POST | `/api/auth/refresh` (garbage token) | 401 | 0.22s | Negative path. |
| 19 | POST | `/api/auth/logout` (valid refresh token) | 204 | 0.15s | Revoked successfully. |
| 20 | POST | `/api/auth/logout` (same token again) | 401 | 0.14s | Negative path — reuse of a revoked/already-consumed refresh token correctly rejected. |

Every route responded, every positive path returned the expected 2xx, and every negative path (unauthenticated write, wrong password, nonexistent id, reused/invalid token) returned the correct 4xx. No 5xx, no timeouts, no unexpected payloads.

## What's fragile

- **The Task 6 resilience code is not live** (see deployment gap section above) — this is the most material finding of this smoke test, not a hypothetical.
- **SQLite data does not survive a redeploy.** The empty `[]` from `GET /api/quotes` at the start of this run, combined with no volume mount for the SQLite file in `resources.bicep`, confirms the database resets to just the seeded test user on every new revision. Fine for a dev exercise; would lose all data in anything closer to production.
- **No dedicated health-check route.** `GET /` returns a hardcoded string with no dependency checks (DB connectivity, JWT signing key presence, etc.), and the `Dockerfile` has no `HEALTHCHECK` directive. A container that's up but can't reach its SQLite file or has a missing JWT secret would still report as healthy to Container Apps' default TCP probe.
- **JWT secret is read once at startup** (`IOptions<JwtOptions>` for token issuance vs. `IOptionsSnapshot<JwtOptions>` only for the audience/lifetime logging in `AuthEndpointExtensions.cs`) — rotating the signing key via a Container App secret update requires restarting/creating a new revision to take effect; it won't hot-reload.

Not claiming as fragile (checked and ruled out): the app's `minReplicas` is `1` (`az containerapp show ... properties.template.scale`), so it does **not** scale to zero — no cold-start-after-idle risk here.
