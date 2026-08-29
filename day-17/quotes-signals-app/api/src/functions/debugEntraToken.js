const { app } = require('@azure/functions');
const { DefaultAzureCredential } = require('@azure/identity');

const ENTRA_API_SCOPE = 'api://47f5632f-7592-4f54-b328-cf7b71139f4a/.default';
const credential = new DefaultAzureCredential();

/** Base64url JWT payload decode — no library needed, and this never touches the signature. */
function decodeJwtPayload(jwt) {
  const payload = jwt.split('.')[1];
  const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
  const json = Buffer.from(base64, 'base64').toString('utf8');
  return JSON.parse(json);
}

/**
 * Verification-only endpoint for the Day-17 deployment task: proves the
 * proxy's read path really does carry a managed-identity-issued Azure AD
 * token, not a static secret. Returns only decoded claims — the raw token
 * itself is never exposed here or logged anywhere.
 */
app.http('debugEntraToken', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: '_debug/entra-token',
  handler: async (_request, context) => {
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
          tokenExpiresOn: token.expiresOnTimestamp
            ? new Date(token.expiresOnTimestamp).toISOString()
            : null,
        },
      };
    } catch (err) {
      context.error('Failed to acquire managed-identity token for verification', err);
      return { status: 500, jsonBody: { error: 'Could not acquire a managed-identity token.' } };
    }
  },
});
