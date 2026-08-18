-- Day 7, Task 1: JOIN types + CTE-based per-author quote summary
--
-- SQLite syntax as run in dev (Day -3/QuotesApi/quotes.db); CTE structure,
-- ROW_NUMBER(), and JOIN syntax are portable to SQL Server as-is -- only the
-- recursive CTE needs SQLite's WITH RECURSIVE vs T-SQL's WITH.
--
-- Schema used below:
--   Quotes(Id, Author, Text, CreatedAt, IsDeleted, OwnerId) -- OwnerId -> Users.Id
--   Users(Id, Email, PasswordHash)
--   RefreshTokens(Id, Token, UserId, ExpiresAt, RevokedAt, ReplacedByToken, FamilyId)


-- =====================================================================
-- 1. JOIN TYPES: INNER, LEFT, CROSS
-- =====================================================================

-- INNER JOIN: only rows where a matching Users row exists for OwnerId.
-- Authors with zero quotes (e.g. other@example.com) never appear.
SELECT u.Email, q.Id AS QuoteId, q.Text
FROM Quotes q
INNER JOIN Users u ON q.OwnerId = u.Id;

-- LEFT JOIN: every row from Users is kept even with no matching Quotes row.
-- Authors with zero quotes still appear once, with NULL QuoteId/Text.
SELECT u.Email, q.Id AS QuoteId, q.Text
FROM Users u
LEFT JOIN Quotes q ON u.Id = q.OwnerId;

-- CROSS JOIN: every user paired with every quote regardless of ownership
-- (no join condition) -- produces false pairings, rarely what you want.
SELECT u.Email, q.Id AS QuoteId, q.Text
FROM Users u
CROSS JOIN Quotes q;


-- =====================================================================
-- 2. NON-RECURSIVE CTE: quote count per user
-- =====================================================================

WITH QuoteCounts AS (
    SELECT OwnerId, COUNT(*) AS QuoteCount
    FROM Quotes
    GROUP BY OwnerId
)
SELECT u.Email, COALESCE(qc.QuoteCount, 0) AS QuoteCount
FROM Users u
LEFT JOIN QuoteCounts qc ON qc.OwnerId = u.Id;


-- =====================================================================
-- 3. RECURSIVE CTE: walk the refresh-token rotation chain
--    (RefreshTokens.ReplacedByToken -> RefreshTokens.Token forms a real
--    self-referential chain per FamilyId in the seeded dev data)
-- =====================================================================

-- --- ORIGINAL (BUGGY) ANCHOR ---
-- Intent: anchor on the token in the family that nothing else points to
-- (the root of the chain). The inner subquery's "RefreshTokens.Token"
-- was meant to refer to the OUTER row, but with no alias to distinguish
-- the two RefreshTokens references, it resolved to the INNER query's own
-- table instead -- comparing each row's ReplacedByToken to its OWN Token.
-- That's never true, so the subquery always returned an empty set, "NOT IN
-- (empty set)" was true for every row, and BOTH tokens in the chain were
-- wrongly treated as roots (both showed up at GenerationNumber 1).
--
-- WITH RECURSIVE TokenChain AS (
--     SELECT Id, Token, ReplacedByToken, FamilyId, 1 AS GenerationNumber
--     FROM RefreshTokens
--     WHERE FamilyId = '1a2647cd-6564-4dc1-a6c6-c3bd70778b88'
--       AND Id NOT IN (
--           SELECT Id FROM RefreshTokens
--           WHERE ReplacedByToken IS NOT NULL
--             AND ReplacedByToken = RefreshTokens.Token
--       )
--     UNION ALL
--     SELECT rt.Id, rt.Token, rt.ReplacedByToken, rt.FamilyId, tc.GenerationNumber + 1
--     FROM RefreshTokens rt
--     JOIN TokenChain tc ON rt.Token = tc.ReplacedByToken
-- )
-- SELECT Id, Token, GenerationNumber FROM TokenChain;

-- --- FIXED ANCHOR ---
-- Explicit aliases (rt vs rt2) make the correlation unambiguous: a row is a
-- root only if NO OTHER row in the same family has ReplacedByToken equal to
-- this row's Token. Correctly yields one root (generation 1), then the
-- recursive step follows ReplacedByToken forward one hop at a time.
WITH RECURSIVE TokenChain AS (
    SELECT rt.Id, rt.Token, rt.ReplacedByToken, rt.FamilyId, 1 AS GenerationNumber
    FROM RefreshTokens rt
    WHERE rt.FamilyId = '1a2647cd-6564-4dc1-a6c6-c3bd70778b88'
      AND NOT EXISTS (
          SELECT 1 FROM RefreshTokens rt2
          WHERE rt2.FamilyId = rt.FamilyId AND rt2.ReplacedByToken = rt.Token
      )
    UNION ALL
    SELECT rt.Id, rt.Token, rt.ReplacedByToken, rt.FamilyId, tc.GenerationNumber + 1
    FROM RefreshTokens rt
    JOIN TokenChain tc ON rt.Token = tc.ReplacedByToken
)
SELECT Id, Token, GenerationNumber
FROM TokenChain;


-- =====================================================================
-- 4. MAIN TASK: per-author quote count + most recent quote
-- =====================================================================

-- Two CTEs instead of a correlated subquery in the SELECT list:
-- QuoteStats aggregates counts once per author, and RankedQuotes uses
-- ROW_NUMBER() to rank each author's quotes newest-first in a single pass
-- over Quotes. A correlated-subquery version (a scalar subquery per output
-- column, each re-filtering Quotes for the current row) would effectively
-- re-scan Quotes once per author for every such column -- an N+1-style
-- access pattern -- instead of the one pass over Quotes done here, and it
-- also invites the aliasing/correlation mistake seen in section 3.
WITH QuoteStats AS (
    SELECT OwnerId, COUNT(*) AS QuoteCount
    FROM Quotes
    GROUP BY OwnerId
),
RankedQuotes AS (
    SELECT
        OwnerId,
        Text,
        CreatedAt,
        ROW_NUMBER() OVER (PARTITION BY OwnerId ORDER BY CreatedAt DESC) AS rn
    FROM Quotes
)
SELECT
    u.Email                     AS Author,
    COALESCE(qs.QuoteCount, 0)  AS TotalQuoteCount,
    rq.Text                     AS MostRecentQuoteText,
    rq.CreatedAt                AS MostRecentQuoteTimestamp
FROM Users u
LEFT JOIN QuoteStats   qs ON qs.OwnerId = u.Id
LEFT JOIN RankedQuotes rq ON rq.OwnerId = u.Id AND rq.rn = 1
ORDER BY TotalQuoteCount DESC;
