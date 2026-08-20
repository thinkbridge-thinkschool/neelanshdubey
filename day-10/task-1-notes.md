# Day 10, Task 1: EF Core Change Tracker + AsNoTracking

Project: [`day-10/ChangeTrackerDemo`](ChangeTrackerDemo) — a .NET 10 console app using
`Microsoft.EntityFrameworkCore.SqlServer` 10.0.10 against the existing Day 9 SQL Server
Docker container (`day9-sql`, host port 1434). The Day 8 `Day8Indexing` database wasn't
running (that container was stopped after Day 8), so this task seeds a fresh
`Day10ChangeTracking` database with the same core `dbo.Orders` schema (`OrderId`,
`CustomerId`, `OrderDate`, `Status`, `Amount`) and exactly 10,000 rows of generated
order data, using the same set-based tally-CTE technique as `Day-8/task1.sql`.

## Part A — Identity resolution

`context.Orders.Find(id)` called twice on the **same** `DbContext` returns the same
object reference, and the second call issues **zero** additional SQL (proven via
`OnConfiguring`'s `LogTo(..., [RelationalEventId.CommandExecuted], ...)` SQL logging —
round-trip count stayed at 1). An explicit LINQ query (`.First(o => o.OrderId == id)`)
called twice **does** round-trip to the database both times, but EF Core's identity
map still performs fixup during materialization and returns the same tracked instance.
A second, separate `DbContext` querying the same PK returns a **different** instance,
because each context has its own identity map.

## Part B — Tracked vs untracked

- `context.Orders.ToList()` populates `ChangeTracker.Entries()` with one entry per row.
- `context.Orders.AsNoTracking().ToList()` leaves `ChangeTracker.Entries()` empty.
- Mutating a tracked entity's property and calling `SaveChanges()` — with no explicit
  `Update()` — persists the change, because EF's change tracker snapshots the original
  values and detects the diff.
- Mutating an `AsNoTracking()` entity and calling `SaveChanges()` persists **nothing**
  (`0` rows affected) — the entity was never added to the change tracker, so there's
  nothing to compare or save.
- Reattaching that same detached entity via `context.Orders.Update(order)` before
  `SaveChanges()` **does** persist it, since `Update()` explicitly tracks it as `Modified`.

## Part C — Benchmark (10,000 rows)

Query variants:

```csharp
// Tracked (default)
using var ctx = new OrdersDbContext(DbInitializer.ConnectionString);
var rows = ctx.Orders.ToList();
```

```csharp
// Untracked
using var ctx = new OrdersDbContext(DbInitializer.ConnectionString);
var rows = ctx.Orders.AsNoTracking().ToList();
```

Each variant: 1 warmup iteration (discarded) + 5 measured iterations, each against a
fresh `DbContext`. Timing via `Stopwatch`; allocations via
`GC.GetAllocatedBytesForCurrentThread()` before/after the 5 measured iterations;
GC counts via `GC.CollectionCount(0|1|2)` before/after, with a forced full collection
before measurement starts to reduce cross-run noise.

### Actual run output (this machine, 2026-08-20)

```
=== Part C: Benchmark (10,000-row Orders table) ===
-- Running: Tracked .ToList() (default) --
   iteration 1: 255.05 ms, 10000 rows
   iteration 2: 199.07 ms, 10000 rows
   iteration 3: 202.36 ms, 10000 rows
   iteration 4: 277.55 ms, 10000 rows
   iteration 5: 247.86 ms, 10000 rows
-- Running: AsNoTracking().ToList() --
   iteration 1: 57.23 ms, 10000 rows
   iteration 2: 56.73 ms, 10000 rows
   iteration 3: 61.84 ms, 10000 rows
   iteration 4: 46.97 ms, 10000 rows
   iteration 5: 62.10 ms, 10000 rows

-- Before/after comparison (averaged over measured iterations, 1 warmup discarded) --
Variant                           Avg ms    Alloc (MB)    Gen0    Gen1    Gen2
------------------------------------------------------------------------------
Tracked .ToList() (default)       236.38         44.31       7       3       2
AsNoTracking().ToList()            56.97         15.53       2       0       0

AsNoTracking is 75.9% faster and allocates 65.0% less than the tracked query over 5 measured iterations.
```

| Variant | Avg ms | Alloc (MB) | Gen0 | Gen1 | Gen2 |
|---|---:|---:|---:|---:|---:|
| Tracked `.ToList()` (default) | 236.38 | 44.31 | 7 | 3 | 2 |
| `.AsNoTracking().ToList()` | 56.97 | 15.53 | 2 | 0 | 0 |

**When NOT to use `AsNoTracking()`:** skip it whenever you need to mutate entities and
persist those changes via `SaveChanges()` on the same context — untracked entities are
never diffed or saved unless explicitly reattached (`Update()`/`Attach()`), as shown in
Part B's scenario 2.
