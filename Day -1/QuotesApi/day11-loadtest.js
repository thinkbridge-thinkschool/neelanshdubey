import http from 'k6/http';
import { check } from 'k6';

// Day 11 Task 1 baseline load test against the deliberately slow
// GET /api/authors/with-quotes (N+1 + missing index on Quotes.AuthorId).
export const options = {
  vus: 10,
  duration: '30s',
  thresholds: {
    http_req_failed: ['rate<0.5'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5292';

export default function () {
  const res = http.get(`${BASE_URL}/api/authors/with-quotes`);
  check(res, {
    'status is 200': (r) => r.status === 200,
  });
}
