import { describe, it, expect, beforeAll } from 'vitest';

/**
 * Characterization test for the real Week-1 QuotesApi contract (Day-5/QuotesApi),
 * pinned against the actual running backend — not a mock. This is intentionally
 * plain `fetch`, not Angular's HttpClient/TestBed: the point is to record what the
 * wire protocol really does before any interceptor is built on top of it, so a
 * future contract change breaks this test instead of silently breaking the app.
 *
 * Requires the API running locally first:
 *   cd Day-5/QuotesApi && ASPNETCORE_URLS=http://localhost:5292 dotnet run --no-launch-profile
 */
const API_BASE = 'http://localhost:5292';

async function requireApiUp(): Promise<void> {
  try {
    await fetch(`${API_BASE}/api/quotes?page=1&size=1`);
  } catch {
    throw new Error(
      `QuotesApi is not reachable at ${API_BASE}. Start it first: ` +
        'cd Day-5/QuotesApi && ASPNETCORE_URLS=http://localhost:5292 dotnet run --no-launch-profile',
    );
  }
}

describe('QuotesApi contract (live backend)', () => {
  beforeAll(requireApiUp);

  it('GET /api/quotes?page=&size= returns 200 with an array shaped like {id, author, text, ...}', async () => {
    const res = await fetch(`${API_BASE}/api/quotes?page=1&size=2`);
    expect(res.status).toBe(200);

    const body = await res.json();
    expect(Array.isArray(body)).toBe(true);
    expect(body.length).toBeGreaterThan(0);
    expect(body.length).toBeLessThanOrEqual(2);

    for (const quote of body) {
      expect(typeof quote.id).toBe('number');
      expect(typeof quote.author).toBe('string');
      expect(typeof quote.text).toBe('string');
    }
  });

  it('GET /api/quotes honors size as a per-page cap', async () => {
    const res = await fetch(`${API_BASE}/api/quotes?page=1&size=1`);
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body.length).toBe(1);
  });

  it('POST /api/quotes with an invalid body returns 400 as ValidationProblemDetails', async () => {
    const email = `contract-test-${Date.now()}@example.com`;
    const registerRes = await fetch(`${API_BASE}/api/auth/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password: 'Passw0rd!123' }),
    });
    expect(registerRes.status).toBe(200);
    const { accessToken } = await registerRes.json();

    const res = await fetch(`${API_BASE}/api/quotes`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${accessToken}`,
      },
      body: JSON.stringify({ author: '', text: '' }),
    });

    expect(res.status).toBe(400);
    expect(res.headers.get('content-type')).toContain('application/problem+json');

    const problem = await res.json();
    expect(problem.title).toEqual(expect.any(String));
    expect(problem.status).toBe(400);
    // ValidationProblemDetails: a dictionary of field name -> array of messages.
    expect(problem.errors).toBeTruthy();
    expect(Array.isArray(Object.values(problem.errors)[0])).toBe(true);
    expect(typeof (Object.values(problem.errors)[0] as string[])[0]).toBe('string');
  });

  it('GET /api/quotes/{id} for a nonexistent id returns a bare 404 (no ProblemDetails body)', async () => {
    const res = await fetch(`${API_BASE}/api/quotes/999999999`);
    expect(res.status).toBe(404);
  });
});
