# Day 8 verification run

Both `task1.sql` and `task2.sql` were executed end-to-end via `sqlcmd` inside the
`day8-sql` container against a freshly-reset `Day8Indexing` database (dropped and
rebuilt first, since the container still had leftover state from before the
original `task1.sql` file was lost). Full raw `sqlcmd` output is in
`task1_output.txt` and `task2_output.txt` in this folder. No graphical execution
plans are captured here (that view only renders inside the VS Code mssql
extension) -- these numbers are the text-based STATISTICS IO/TIME evidence only.
Toggle **Enable Actual Plan** yourself for the Key Lookup / Index Seek operator
screenshots.

## Final schema state (confirmed clean, no duplicates)

| Index | Type | Key | Include |
|---|---|---|---|
| PK_Orders | CLUSTERED | OrderId | -- |
| IX_Orders_CustomerId | NONCLUSTERED | CustomerId | -- |
| IX_Orders_OrderDate_Include_Amount | NONCLUSTERED | OrderDate | Amount |
| IX_Orders_Status_Covering | NONCLUSTERED | Status | OrderId, CustomerId, OrderDate, Amount |

110,000 rows after task1.sql (100,000 initial + 5,000 from step 8a + 5,000 from step 8b).

## task1.sql -- clustered vs. non-clustered

**Query 1: `SELECT * FROM Orders WHERE CustomerId = 2500`**

| Stage | Rows | Scan count | Logical reads |
|---|---|---|---|
| BEFORE -- heap | 18 | 13 | 7,143 |
| AFTER -- clustered PK only (no index on CustomerId yet) | 18 | 13 | 7,517 |
| AFTER -- + IX_Orders_CustomerId | 21 | 1 | **65** |

Clustered index alone barely moves the needle (still a full scan) -- the win comes
from the non-clustered index on CustomerId (109x fewer logical reads).

**Query 2: `... WHERE OrderDate >= '2025-01-01' AND OrderDate < '2025-02-01'`**

| Stage | Rows | Scan count | Logical reads |
|---|---|---|---|
| BEFORE -- heap | 4,237 | 13 | 7,143 |
| AFTER -- + IX_Orders_OrderDate_Include_Amount (covering) | 4,432 | 1 | **18** |

**Write cost, 5,000-row INSERT (step 8a vs 8b):**

| Stage | Non-clustered indexes | CPU time | Elapsed time | Logical reads (Orders + Worktable) |
|---|---|---|---|---|
| 8a | 0 (clustered PK only) | 34 ms | 36 ms | 2,783 |
| 8b | 2 (CustomerId + OrderDate covering) | 127 ms | 130 ms | 32,519 + 10,186 = 42,705 |

~3.7x more CPU/elapsed time and ~15x more logical reads to insert the same 5,000
rows once both non-clustered indexes exist -- the write-cost tradeoff the exercise
is meant to show.

## task2.sql -- covering index / Key Lookup elimination

**`SELECT OrderId, CustomerId, OrderDate, Amount FROM Orders WHERE Status = 'Shipped'`**

| Stage | Rows | Scan count | Logical reads |
|---|---|---|---|
| BEFORE -- IX_Orders_Status (key-only, non-covering) | 20,573 | 3 | 1,134 |
| AFTER -- IX_Orders_Status_Covering (INCLUDE 4 cols) | 20,573 | 1 | **113** |

~10x fewer logical reads once the Key Lookup is eliminated. Scan count dropping
from 3 to 1 is also consistent with the Nested Loops/Key Lookup plan shape going
away -- worth confirming visually with Enable Actual Plan, but the IO numbers
already tell the story.

## One data-generation quirk worth knowing about

`Status` ended up split roughly 42% Cancelled / 25% Pending / 19% Shipped / 14%
Delivered, not the even 25/25/25/25 the CASE expression in task1.sql was written
to produce. Cause: `CASE ABS(CHECKSUM(NEWID())) % 4 WHEN 0 ... WHEN 1 ... END`
re-evaluates `NEWID()` separately for each WHEN comparison in SQL Server, instead
of evaluating the input expression once -- a known gotcha with non-deterministic
functions inside CASE. It doesn't invalidate either demo above (18.7% for
'Shipped' was still low-selectivity enough to get an Index Seek + Key Lookup
plan rather than a table scan, and CustomerId/OrderDate queries don't touch
Status at all), but it means task2.sql's own comment claiming "~5% Shipped" only
describes what its *own* generation block would produce on a blank database --
it never actually ran here, since task1.sql had already populated Orders. Not
fixed in the committed scripts; flagging it here rather than silently leaving it.
