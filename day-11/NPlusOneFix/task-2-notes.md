# Day 11 Task 2: Fix N+1, add covering index, prove ≥10x p99 improvement

Project: `day-11/NPlusOneFix` (new minimal console app - `Author`/`Book` didn't fit the
JWT-secured `QuotesApi` domain from Task 1, so this exercise gets its own EF Core model).
Database: `NPlusOneFixDb` on the existing `day9-sql` container (`localhost,1434`).

## Schema and seed summary

- `Author(Id, Name)` 1-to-many `Book(Id, Title, AuthorId, PublishedYear)`, FK on
  `Book.AuthorId`, lazy-loading proxies enabled (`UseLazyLoadingProxies`) so the baseline
  N+1 is genuine, not simulated.
- Seeded with a fixed RNG seed (42) for reproducibility:
  - **500 authors**
  - **9,960 books** (15-25 per author, randomized, ~19.9 avg)
- Initial migration creates the table pair plus the *conventional* FK index EF Core
  generates by default: `IX_Books_AuthorId` on `AuthorId` alone - no `INCLUDE` columns.
  This is the realistic "before" state: an index exists (it's the FK), but it doesn't
  cover the columns the query actually reads.

## Part A - the N+1 (before)

```csharp
// Program.cs - RunBaselineCount / the timed body of RunBaselineBenchmark
var authors = context.Authors.ToList();          // query #1
long totalBooks = 0;
foreach (var author in authors)
{
    totalBooks += author.Books.Count();          // 1 lazy-load query per author
}
```

`QueryCounterInterceptor` (a `DbCommandInterceptor` counting `ReaderExecuting` calls)
confirms the count:

```
Authors: 500, Books counted: 9960
Query count (N+1 baseline): 501
```

1 query for authors + 500 queries for books = **501 queries** for 500 authors.

## Part B - the fix (after)

**Variant 1 - projection** (chosen as the benchmarked fix):

```csharp
var summaries = context.Authors
    .Select(a => new AuthorBookSummaryDto
    {
        AuthorId = a.Id,
        Name = a.Name,
        BookCount = a.Books.Count(),
        LatestBookTitle = a.Books
            .OrderByDescending(b => b.PublishedYear)
            .Select(b => b.Title)
            .FirstOrDefault()
    })
    .ToList();
```

**Variant 2 - `Include` + `AsSplitQuery`** (implemented for comparison, not benchmarked):

```csharp
var authors = context.Authors
    .AsNoTracking()
    .Include(a => a.Books)
    .AsSplitQuery()
    .ToList();
```

### Query count comparison

| Approach                        | Queries issued | Scales with author count? |
|----------------------------------|:--:|:--:|
| Baseline (lazy-load N+1)         | 501 | Yes - N+1 |
| Projection (`Select`)            | 1   | No - flat |
| `Include` + `AsSplitQuery`       | 2   | No - flat |

(`fix-query-count.txt`: `Projection: 500 summaries, query count: 1` /
`Split query: 500 authors, query count: 2`.)

**Projection vs split query:** projection is the better default here - one round trip,
and the payload is exactly the three fields the caller needs. `AsSplitQuery` is worth
choosing instead when the caller genuinely needs full `Book` entities (not a summary) and
there are multiple collection navigations to include at once - a single query with two
`Include`d collections would cartesian-multiply rows (author × books × otherCollection),
inflating the result set and CPU cost; split queries avoid that at the cost of an extra
round trip and losing single-statement transactional consistency between the two selects.

## Before/after benchmark (50 runs, first 5 discarded as warm-up)

| Metric | Baseline (N+1) | Fixed (projection + covering index) | Improvement |
|---|--:|--:|--:|
| p50 | 581.645 ms | 13.541 ms | 42.9x |
| p95 | 660.048 ms | 20.456 ms | 32.3x |
| **p99** | **680.100 ms** | **30.453 ms** | **22.3x** |

p99 improvement is **≥10x** (22.3x) - target met without needing further iteration.
Each run used a fresh `AppDbContext`/connection so the numbers include connection-pool
overhead on both sides evenly; the gap is real query-plan and round-trip-count savings,
not measurement noise (raw files: `baseline-benchmark.txt`, `fix-benchmark.txt`).

## Part C - the index

```csharp
modelBuilder.Entity<Book>()
    .HasIndex(b => b.AuthorId)
    .IncludeProperties(b => new { b.PublishedYear, b.Title });
```

Generated via `dotnet ef migrations add AddCoveringIndexOnBooksAuthorId` (not hand-written
SQL). Because EF Core already creates `IX_Books_AuthorId` by convention for the FK, this
call matches the existing index by key column and the migration alters it in place:

```csharp
migrationBuilder.DropIndex(name: "IX_Books_AuthorId", table: "Books");
migrationBuilder.CreateIndex(name: "IX_Books_AuthorId", table: "Books", column: "AuthorId")
    .Annotation("SqlServer:Include", new[] { "PublishedYear", "Title" });
```

Applied with `dotnet ef database update`.

## Execution plans - representative query

Representative query (`dotnet run -- sql-sample`), run via `sqlcmd` against `day9-sql`
with `SET STATISTICS IO, TIME ON` + `SET STATISTICS XML ON` (`sql/capture-plan-query.sql`):

```sql
SELECT [b].[Id], [b].[Title], [b].[PublishedYear]
FROM [Books] AS [b]
WHERE [b].[AuthorId] = 250;
```

**Before** (`before-plan.txt`) - `IX_Books_AuthorId` (AuthorId only):

- `Index Seek` on `IX_Books_AuthorId` → `Nested Loops` → **`Clustered Index Seek` (Key
  Lookup)** on `PK_Books` to fetch `Title`/`PublishedYear`.
- `Table 'Books'. Scan count 1, logical reads 51`.

**After** (`after-plan.txt`) - `IX_Books_AuthorId` now `INCLUDE (PublishedYear, Title)`:

- A single `Index Seek` on `IX_Books_AuthorId` - **no Key Lookup, no Nested Loops**; every
  column the query needs is already in the index leaf pages.
- `Table 'Books'. Scan count 1, logical reads 4`.

**What changed and why:** the FK index only ever guaranteed the engine could find the
right rows fast (`AuthorId` in the key). It still had to jump back to the clustered index
once per row to fetch `Title` and `PublishedYear` - a Key Lookup, one extra random I/O per
row (51 logical reads for 24 rows). Adding those two columns to the index's `INCLUDE` list
makes the index *covering* for this query: every column in the `SELECT` is present in the
non-clustered index itself, so the Key Lookup and its Nested Loops join disappear entirely
- reads drop from 51 to 4. The 22x p99 improvement across all 500 authors is this same
per-row saving (no Key Lookup) compounded with the 501→1 query-count fix from Part B; both
matter, but the index is what makes the *single remaining query* cheap.
