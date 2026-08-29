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

app.http('apiProxy', {
  methods: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'],
  authLevel: 'anonymous',
  route: '{*segments}',
  handler: async (request, context) => {
    const segments = request.params.segments ?? '';
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

    return {
      status: upstream.status,
      headers: { 'content-type': upstream.headers.get('content-type') ?? 'application/json' },
      body,
    };
  },
});
