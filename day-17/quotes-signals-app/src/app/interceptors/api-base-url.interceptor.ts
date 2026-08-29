import { HttpInterceptorFn } from '@angular/common/http';

// Locally, `ng serve`'s proxy.conf.json forwards relative '/api/*' calls to
// the real backend running on localhost — no rewrite needed there. Once
// deployed to Azure Static Web Apps, '/api/*' has nothing behind it on that
// same origin (Free tier can't host a managed Functions API with a managed
// identity — see day-17/quotes-signals-app/api/README.md), so every '/api/*'
// call gets redirected here to the standalone Function App that proxies to
// the real Week-1 API with a real managed-identity token attached server
// side. Runtime-checked (not a build-time environment file) so the exact
// same build artifact works unmodified in both places.
const DEPLOYED_API_ORIGIN = 'https://ai-quotes-func.azurewebsites.net';

export const apiBaseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  if (typeof location !== 'undefined' && location.hostname !== 'localhost' && req.url.startsWith('/api/')) {
    return next(req.clone({ url: `${DEPLOYED_API_ORIGIN}${req.url}` }));
  }

  return next(req);
};
