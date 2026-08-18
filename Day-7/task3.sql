-- Day 7, Task 3: UNION / INTERSECT / EXCEPT for tag-based author queries
--
-- SQLite syntax as run in dev (Day -3/QuotesApi/quotes.db); UNION, INTERSECT,
-- and EXCEPT are all standard set operators and are portable to SQL Server
-- as-is (SQL Server also requires matching column counts/types across both
-- sides, same as SQLite).
--
-- Schema used below:
--   Quotes(Id, Author, Text, CreatedAt, IsDeleted, OwnerId) -- OwnerId -> Users.Id
--   Users(Id, Email, PasswordHash)
--
-- Tags(Id, QuoteId, Category) -- QuoteId -> Quotes.Id
--   ** SYNTHETIC / NOT PART OF THE REAL SCHEMA **
--   Verified against both the EF Core models (QuotesApi/Models, /Migrations,
--   Data/AppDbContext.cs -- only DbSet<Quote>, DbSet<User>, DbSet<RefreshToken>
--   exist) and the live quotes.db tables (sqlite_master lists only Quotes,
--   Users, RefreshTokens, plus EF's own history/lock tables). There is no
--   Tags table, no QuoteTags junction, no Category column, and nothing
--   resembling classic/modern anywhere in this app. Tags was created here
--   purely as a small teaching fixture so UNION/INTERSECT/EXCEPT have
--   something real to run against; quotes.db was backed up to
--   quotes.db.bak2 before this table was added, alongside the existing
--   quotes.db.bak from Task 1.
--
-- "Author" below follows the same convention as Task 1/2: Users.Email
-- reached via Quotes.OwnerId, not the free-text Quotes.Author column (which
-- has some stale/edited values left over from earlier days' exercises).
--
-- Seed data (12 Tags rows, hand-picked for coverage, not random):
--   seneca@example.com          -> classic, classic, classic   (q4, q5, q6)
--   marcus.aurelius@example.com -> (no tags at all)             (q7, q8)
--   einstein@example.com        -> modern, classic, modern      (q9, q10, q11)
--                                   (q12 also untagged)
--   twain@example.com           -> modern                       (q13)
--   lincoln@example.com         -> classic, classic             (q14, q15)
--   curie@example.com           -> modern                       (q16)
--   gandhi@example.com          -> classic, modern              (q17, q18)
--   test@example.com            -> (no tags at all)             (q1, q3)
-- This gives: two authors with quotes but zero tags (marcus.aurelius, test),
-- two authors straddling both categories (einstein, gandhi), and one
-- fully-untagged quote even for a tagged author (einstein's q12).


-- =====================================================================
-- 1. Authors with quotes but no tags -- EXCEPT
-- =====================================================================
-- "Has quotes" and "has at least one tagged quote" are two row sets over the
-- same column (Author). EXCEPT keeps every row from the left set and removes
-- any row that also shows up in the right set -- pure set subtraction, which
-- is exactly "authors with quotes, minus authors who have a tag anywhere."
-- INTERSECT would answer the opposite question (authors who DO have tags);
-- UNION would just merge the two lists and erase the distinction entirely.
--
-- Result: marcus.aurelius@example.com, test@example.com
SELECT u.Email AS Author
FROM Quotes q
JOIN Users u ON u.Id = q.OwnerId

EXCEPT

SELECT u.Email AS Author
FROM Tags t
JOIN Quotes q ON q.Id = t.QuoteId
JOIN Users u ON u.Id = q.OwnerId

ORDER BY Author;


-- =====================================================================
-- 2. Authors with quotes in BOTH 'classic' and 'modern' -- INTERSECT
-- =====================================================================
-- Two row sets built the same way (classic-tagged authors, modern-tagged
-- authors) -- INTERSECT keeps only the rows present in both, which is
-- literally "in both categories." EXCEPT would strip one category's authors
-- out of the other instead of finding the common ones; UNION would combine
-- them and lose the "in both" condition altogether.
--
-- Result: einstein@example.com, gandhi@example.com
SELECT u.Email AS Author
FROM Tags t
JOIN Quotes q ON q.Id = t.QuoteId
JOIN Users u ON u.Id = q.OwnerId
WHERE t.Category = 'classic'

INTERSECT

SELECT u.Email AS Author
FROM Tags t
JOIN Quotes q ON q.Id = t.QuoteId
JOIN Users u ON u.Id = q.OwnerId
WHERE t.Category = 'modern'

ORDER BY Author;


-- =====================================================================
-- 3. Combined distinct author list across 'classic' + 'modern' -- UNION
-- =====================================================================
-- Straight combination of two lists into one deduplicated list -- no
-- subtraction, no "must be in both" requirement, just "everything that
-- shows up in either." That's UNION. Using the literal distinct Category
-- values here would be trivial ({classic, modern}, two rows, nothing to
-- dedupe) since this fixture only has two category labels, so the query
-- instead unions the two *author* lists, where UNION's dedup is actually
-- visible: einstein and gandhi are in both underlying sets (see query 2)
-- but each appears only once below -- UNION ALL would have listed them
-- twice.
--
-- Result: curie@example.com, einstein@example.com, gandhi@example.com,
--         lincoln@example.com, seneca@example.com, twain@example.com
SELECT u.Email AS Author
FROM Tags t
JOIN Quotes q ON q.Id = t.QuoteId
JOIN Users u ON u.Id = q.OwnerId
WHERE t.Category = 'classic'

UNION

SELECT u.Email AS Author
FROM Tags t
JOIN Quotes q ON q.Id = t.QuoteId
JOIN Users u ON u.Id = q.OwnerId
WHERE t.Category = 'modern'

ORDER BY Author;
