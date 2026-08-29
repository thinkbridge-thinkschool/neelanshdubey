const { app } = require('@azure/functions');
const { DefaultAzureCredential } = require('@azure/identity');

// The real Week-1 API (Day-5/QuotesApi), already deployed to Azure Container
// Apps — see Day-5/QuotesApi/DEPLOYMENT-ai-quotes-api.md. This Function is
// the only thing the browser ever talks to for /api/*; it forwards to the
// real API server-side, so CORS never needs to be touched on the container
// app, and no secret of any kind lives in this repo or in this Function's
// app settings.
const API_BASE = 'https://ai-quotes-app.whitestone-71ebd55e.centralindia.azurecontainerapps.io/api';

// The real API already trusts Azure AD tokens for this exact audience — see
// the EntraJwt scheme in Day-5/QuotesApi/Extentions/InfrastructureExtensions.cs
// and Entra:Audience in appsettings.json. Confirmed via `az ad sp show` that
// the app registration's appRoleAssignmentRequired is false, so any Azure AD
// principal in the tenant — including this Function's own managed identity —
// can already acquire a valid token for it, with nothing to grant.
const ENTRA_API_SCOPE = 'api://47f5632f-7592-4f54-b328-cf7b71139f4a/.default';

// Created once per cold start. getToken() is safe to call on every request —
// the SDK caches the token internally and only makes a network call again
// once it's near expiry.
const credential = new DefaultAzureCredential();

function isAnonymousReadPath(method, segments) {
  return method === 'GET' && /^quotes(\/|$)/.test(segments);
}

/** Base64url JWT payload decode — no library needed, never touches the signature. */
function decodeJwtPayload(jwt) {
  const payload = jwt.split('.')[1];
  const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
  return JSON.parse(Buffer.from(base64, 'base64').toString('utf8'));
}

app.http('apiProxy', {
  methods: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'],
  authLevel: 'anonymous',
  route: '{*segments}',
  handler: async (request, context) => {
    try {
      return await handle(request, context);
    } catch (err) {
      context.error('apiProxy uncaught error', err);
      return { status: 500, jsonBody: { error: err?.message ?? String(err), stack: err?.stack ?? null } };
    }
  },
});

async function handle(request, context) {
    const segments = request.params.segments ?? '';

    // Verification-only endpoint for the Day-17 deployment task: proves the
    // read path really does carry a managed-identity-issued Azure AD token,
    // not a static secret. Handled here (not as a separate app.http()
    // registration) because Azure Functions' route matching does not give
    // literal routes precedence over this file's own '{*segments}' catch-all
    // — a second function at 'route: "_debug/entra-token"' was silently
    // shadowed by this one and never invoked.
    if (segments === '_debug/entra-token' && request.method === 'GET') {
      try {
        const token = await credential.getToken(ENTRA_API_SCOPE);
        const claims = decodeJwtPayload(token.token);
        return {
          status: 200,
          jsonBody: {
            note: 'Decoded claims only — the raw token is never returned or logged.',
            iss: claims.iss,
            aud: claims.aud,
            appid: claims.appid ?? claims.azp ?? null,
            oid: claims.oid ?? null,
            exp: claims.exp,
            tokenExpiresOn: token.expiresOnTimestamp ? new Date(token.expiresOnTimestamp).toISOString() : null,
          },
        };
      } catch (err) {
        context.error('Failed to acquire managed-identity token for verification', err);
        return { status: 500, jsonBody: { error: 'Could not acquire a managed-identity token.' } };
      }
    }

    const targetUrl = `${API_BASE}/${segments}${request.query.size > 0 ? `?${request.query}` : ''}`;

    const headers = new Headers();
    const contentType = request.headers.get('content-type');
    if (contentType) headers.set('content-type', contentType);

    if (isAnonymousReadPath(request.method, segments)) {
      // Managed-identity path: this call carries a real Azure AD token
      // acquired via this Function's own identity, not a stored secret.
      try {
        const token = await credential.getToken(ENTRA_API_SCOPE);
        headers.set('authorization', `Bearer ${token.token}`);
      } catch (err) {
        context.error('Failed to acquire managed-identity token', err);
        // Fall through without the header — the real API's GET endpoints
        // are anonymous today, so the request still succeeds; it just
        // won't carry proof of MI on this one call.
      }
    } else {
      // Every other path (login/register, create/update/delete) is a
      // genuine per-end-user action — forward the browser's own bearer
      // token unchanged. A service identity has no business acting as the
      // user here.
      const incomingAuth = request.headers.get('authorization');
      if (incomingAuth) headers.set('authorization', incomingAuth);
    }

    const init = { method: request.method, headers };
    if (!['GET', 'HEAD'].includes(request.method)) {
      init.body = await request.text();
    }

    const upstream = await fetch(targetUrl, init);
    const body = await upstream.text();

    // A 204 (e.g. the real DELETE /api/quotes/{id} endpoint) or 304 must not
    // carry a body per HTTP semantics — Azure Functions' response layer
    // throws if one is set anyway, turning a real, successful delete into a
    // 500 here even though the delete itself already happened upstream.
    if (upstream.status === 204 || upstream.status === 304) {
      return { status: upstream.status };
    }

    return {
      status: upstream.status,
      headers: { 'content-type': upstream.headers.get('content-type') ?? 'application/json' },
      body,
    };
}
