# Day 11 - Task 1: Profile a slow endpoint (N+1 + missing index)

## Environment

- **SQL container used:** `day9-sql` (reused from Day 9, `mcr.microsoft.com/mssql/server:2022-latest`, port `1434`). It had been stopped since Day 9 and was restarted with `docker start day9-sql` rather than creating a fresh `day11-sql` container, since Day 9's container image, port mapping, and SA credentials were still intact and reusable. Database used: `QuotesApiDb` (new database on that same server instance, alongside the pre-existing `Day9Isolation` and `Day10ChangeTracking` databases).
- **Provider switch:** QuotesApi previously ran on SQLite (`Data Source=quotes.db`). This task needs real SQL Server behavior (missing-index table scans, `SET STATISTICS IO`, `SHOWPLAN_XML`), so the app was switched to `Microsoft.EntityFrameworkCore.SqlServer` with the connection string pointed at `localhost,1434`.
- **k6:** v2.2.0. `winget install GrafanaLabs.k6` hung indefinitely (winget.exe sat alive with no `msiexec` child process, most likely blocked on a UAC elevation prompt that can't be answered in a non-interactive shell). Worked around it by downloading the portable `k6-v2.2.0-windows-amd64.zip` release directly from GitHub and running `k6.exe` from the extracted folder -- no admin rights needed.

## Schema

- Added `Author { Id, Name, Quotes }` and `Quote.AuthorId` (required FK to `Author`).
- EF Core creates an index on a required FK column by convention. That index (`IX_Quotes_AuthorId`) is created in the `InitialCreate` migration and then explicitly dropped in a follow-up `DropQuoteAuthorIdIndex` migration, so the database this task profiles against has **zero index on `Quotes.AuthorId`** -- confirmed with:

  ```sql
  SELECT i.name, i.type_desc FROM sys.indexes i
  JOIN sys.tables t ON i.object_id = t.object_id
  WHERE t.name = 'Quotes';
  -- -> only PK_Quotes (CLUSTERED)
  ```

## Seed data

200 authors, ~50 quotes each, generated with a fixed random seed (`AuthorQuoteSeeder`, seed `20261101`) so the dataset is reproducible. Verified counts after seeding:

| Authors | Quotes | Avg quotes/author |
|---|---|---|
| 200 | 9,948 | 49.74 |

## The offending SQL (N+1)

`GET /api/authors/with-quotes` does:

```csharp
var authors = db.Authors.ToList();          // 1 query

foreach (var author in authors)
{
    var quotes = db.Quotes                   // N queries (N = 200)
        .Where(q => q.AuthorId == author.Id)
        .ToList();
    ...
}
```

Captured from one `curl` hit against the endpoint (full detail in [day11-emitted-sql.txt](day11-emitted-sql.txt)):

```sql
-- 1x
SELECT [a].[Id], [a].[Name]
FROM [Authors] AS [a]

-- 200x (identical shape, only @author_Id changes)
SELECT [q].[Id], [q].[AuthorId], [q].[CreatedAt], [q].[IsDeleted], [q].[Text]
FROM [Quotes] AS [q]
WHERE [q].[AuthorId] = @author_Id
```

**Repeat count: 200** (one per seeded author) -> **201 total round trips per request**.

## Execution plan (full detail in [day11-execution-plan.txt](day11-execution-plan.txt))

Running the offending query shape directly:

```sql
SET STATISTICS IO ON;
SELECT * FROM Quotes WHERE AuthorId = 1;
```

```
Table 'Quotes'. Scan count 1, logical reads 501, physical reads 0, ...
(53 rows affected)
```

`SET SHOWPLAN_XML ON` for the same query shows:

- **Scan type:** `Clustered Index Scan` on `PK_Quotes` (`IndexKind="Clustered"`) -- there is no nonclustered index on `AuthorId`, so the only index SQL Server has is the clustered index on `Id`, which *is* the table's storage. Scanning it end to end is functionally a full table scan.
- **Estimated rows matching predicate:** 53
- **Rows actually read to produce those 53:** 9,948 (`EstimatedRowsRead == TableCardinality`) -- the whole table.
- **Estimated I/O cost:** 0.372014, **CPU cost:** 0.0110998, **subtree cost:** 0.383114.
- **Optimization level:** `TRIVIAL` -- the optimizer doesn't even need cost-based search; a scan is the only plan shape available without an index.

## Baseline load test (full detail in [day11-loadtest-results.txt](day11-loadtest-results.txt))

`day11-loadtest.js`, 10 VUs, 30s, against `GET /api/authors/with-quotes`. The endpoint sustained 10 VUs for the full 30s with 0 failed requests, so no need to scale down the config.

```
http_req_duration..............: avg=1.73s min=1.2s med=1.55s max=2.65s p(90)=2.18s p(95)=2.49s
http_req_failed................: 0.00%  0 out of 180
http_reqs......................: 180    5.719748/s
```

- **p50: 1.55s**
- **p95: 2.49s** (this k6 build's default summary reports p90/p95, not p99; max observed was 2.65s and stands in for the tail)
- Throughput topped out at **~5.7 req/s** with only 10 concurrent users -- each request is dominated by 201 sequential round trips, so there's very little room to push more load through before requests start queuing behind each other.

## Two biggest problems

1. **The query pattern forces a full scan per author, and it does it 200 times per request.** The root issue isn't "SQL Server is slow" -- it's that `Where(q => q.AuthorId == author.Id)` has no index to use, so every one of those 200 lookups has to walk the entire 9,948-row `Quotes` table to find ~50 matching rows. Each individual scan is cheap in isolation (501 logical reads, sub-millisecond CPU), but the cost is linear in the number of authors, and it's paid 200 times *per request*, not once. This is the part a single-index fix (an index on `Quotes.AuthorId`) directly resolves: a seek instead of a scan turns "read 9,948 rows to get 50" into "read ~50 rows directly."

2. **The N+1 shape multiplies network/round-trip latency independently of the query cost.** Even if every individual `Quotes` query were instant, the endpoint still pays for 201 separate request/response round trips to the database instead of 1. That's fixed connection/command overhead (parsing, plan lookup, network hop) paid 201 times instead of once, which is why the load test shows ~1.5s median latency even though no single query is expensive on its own. This is a data-access-pattern problem, not a schema problem -- an index alone wouldn't fix it; the loop itself needs to become a single `Include`/join (or a split query) so the database is asked for authors-with-quotes in one or two round trips instead of 201.
