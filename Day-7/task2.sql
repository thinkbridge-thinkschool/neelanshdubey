-- Day 7, Task 2: Window functions + per-author running count and quote-gap analysis
--
-- SQLite syntax as run in dev (Day -3/QuotesApi/quotes.db); ROW_NUMBER(), RANK(),
-- LAG()/LEAD(), and SUM()/COUNT() OVER (...) are all standard window-function
-- syntax and are portable to SQL Server as-is.
--
-- Schema used below:
--   Quotes(Id, Author, Text, CreatedAt, IsDeleted, OwnerId) -- OwnerId -> Users.Id
--   Users(Id, Email, PasswordHash)


-- =====================================================================
-- 1. TEACHING EXAMPLES
-- =====================================================================

-- --- 1a. ROW_NUMBER(): number each author's quotes in chronological order ---
-- PARTITION BY resets the counter per author; ORDER BY CreatedAt decides the
-- sequence within that partition. Unlike a plain aggregate (e.g. COUNT(*)
-- GROUP BY OwnerId), this keeps every quote row and just labels its position
-- -- a GROUP BY would collapse the rows down to one per author and lose them.
SELECT
    u.Email AS Author,
    q.Text,
    q.CreatedAt,
    ROW_NUMBER() OVER (PARTITION BY q.OwnerId ORDER BY q.CreatedAt) AS QuoteSeq
FROM Quotes q
JOIN Users u ON u.Id = q.OwnerId
ORDER BY Author, QuoteSeq;

-- --- 1b. RANK(): rank quotes by text length ---
-- CreatedAt has no duplicate values in this seeded data, so ranking by
-- CreatedAt would never show a tie. LENGTH(Text) does have real ties (two
-- quotes at 56 chars, two at 32 chars), so it's used here to make RANK()'s
-- tie behavior visible: tied rows share a rank, and the rank sequence then
-- skips the number of tied rows (rank 3, 3, 5 -- no rank 4). A plain
-- ORDER BY LENGTH(Text) DESC would sort the same rows but never expose that
-- two of them are actually tied.
SELECT
    u.Email AS Author,
    q.Text,
    LENGTH(q.Text) AS TextLength,
    RANK() OVER (ORDER BY LENGTH(q.Text) DESC) AS LengthRank
FROM Quotes q
JOIN Users u ON u.Id = q.OwnerId
ORDER BY LengthRank;

-- --- 1c. LAG() / LEAD(): previous and next quote per author, by CreatedAt ---
-- Each row can see a neighboring row's value without a self-join. LAG looks
-- one row back within the partition, LEAD one row forward; both are NULL at
-- the partition's edges (an author's first quote has no PrevQuoteText, the
-- last has no NextQuoteText). A plain aggregate has no notion of "the row
-- before this one" at all -- it only summarizes the whole group.
SELECT
    u.Email AS Author,
    q.CreatedAt,
    q.Text,
    LAG(q.Text)  OVER (PARTITION BY q.OwnerId ORDER BY q.CreatedAt) AS PrevQuoteText,
    LEAD(q.Text) OVER (PARTITION BY q.OwnerId ORDER BY q.CreatedAt) AS NextQuoteText
FROM Quotes q
JOIN Users u ON u.Id = q.OwnerId
ORDER BY Author, q.CreatedAt;

-- --- 1d. Running total: SUM() OVER (...) as a running count per author ---
-- ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW turns the aggregate into
-- a cumulative one: each row sums (here, counts) itself plus every earlier
-- row in its partition, instead of the whole partition at once. A plain
-- COUNT(*) GROUP BY OwnerId gives only the final total per author, one row
-- each -- this gives the running total at every point in time, one row per
-- quote.
SELECT
    u.Email AS Author,
    q.CreatedAt,
    q.Text,
    SUM(1) OVER (
        PARTITION BY q.OwnerId
        ORDER BY q.CreatedAt
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningQuoteCount
FROM Quotes q
JOIN Users u ON u.Id = q.OwnerId
ORDER BY Author, q.CreatedAt;


-- =====================================================================
-- 2. MAIN TASK: per-author running quote count + days since previous quote
-- =====================================================================

-- One pass over Quotes via a CTE: RankedQuotes computes both window values
-- (COUNT() OVER for the running count, LAG() OVER for the previous
-- CreatedAt) per author in a single scan, then the outer SELECT turns the
-- CreatedAt gap into a day count with julianday() subtraction. PARTITION BY
-- OwnerId is what makes this "one window per author" instead of one window
-- over the whole table; ORDER BY CreatedAt inside each window is what makes
-- "running" and "previous" mean something chronologically.
--
-- Why PARTITION BY does what GROUP BY can't here: GROUP BY collapses all of
-- an author's rows into a single summary row, so a GROUP BY query could give
-- you "5 quotes total" per author but never "this specific quote was the
-- 3rd, 47 days after the one before it." PARTITION BY keeps every input row
-- intact and only scopes the window's calculation (COUNT, LAG) to rows
-- sharing the same OwnerId -- so the result is still one row per quote, each
-- carrying a value computed relative to its own author's other quotes.
WITH RankedQuotes AS (
    SELECT
        u.Email AS Author,
        q.Id AS QuoteId,
        q.Text,
        q.CreatedAt,
        COUNT(*) OVER (
            PARTITION BY q.OwnerId
            ORDER BY q.CreatedAt
            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        ) AS RunningQuoteCount,
        LAG(q.CreatedAt) OVER (
            PARTITION BY q.OwnerId
            ORDER BY q.CreatedAt
        ) AS PrevCreatedAt
    FROM Quotes q
    JOIN Users u ON u.Id = q.OwnerId
)
SELECT
    Author,
    QuoteId,
    Text,
    CreatedAt,
    RunningQuoteCount,
    CASE
        WHEN PrevCreatedAt IS NULL THEN NULL
        ELSE ROUND(julianday(CreatedAt) - julianday(PrevCreatedAt), 2)
    END AS DaysSincePreviousQuote
FROM RankedQuotes
ORDER BY Author, CreatedAt;
