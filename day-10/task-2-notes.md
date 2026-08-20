# Day 10, Task 2: Query Translation + Projections

Project: [`day-10/ChangeTrackerDemo`](ChangeTrackerDemo) (same project, database, and
Docker container as [Task 1](task-1-notes.md) — `day9-sql`, `Day10ChangeTracking`,
10,000-row `dbo.Orders`). `OrdersDbContext` gained an `enableSensitiveDataLogging`
constructor flag (default `false`, so Task 1's callers are unaffected) that calls
`EnableSensitiveDataLogging()` — **dev-only, never enable in production**, since it
bakes real parameter values into logged SQL and can leak PII into logs.

## Part A — Whole-entity query

[`Task2_PartA_WholeEntityQuery.cs`](ChangeTrackerDemo/Task2_PartA_WholeEntityQuery.cs):

```csharp
var orders = context.Orders.Where(o => o.Status == "Shipped").ToList();
```

Generated SQL (captured via `LogTo(..., [RelationalEventId.CommandExecuted], ...)`):

```sql
SELECT [o].[OrderId], [o].[Amount], [o].[CustomerId], [o].[OrderDate], [o].[Status]
FROM [Orders] AS [o]
WHERE [o].[Status] = N'Shipped'
```

All 5 mapped columns come back, even though only `Status` is used in the filter and
nothing downstream reads `CustomerId`. Rows returned: 1,856.

## Part B — Projection

New DTO ([`OrderSummaryDto.cs`](ChangeTrackerDemo/OrderSummaryDto.cs)):

```csharp
public record OrderSummaryDto(int OrderId, DateTime OrderDate, decimal Amount);
```

Rewritten query ([`Task2_PartB_Projection.cs`](ChangeTrackerDemo/Task2_PartB_Projection.cs)):

```csharp
var summaries = context.Orders
    .Where(o => o.Status == "Shipped")
    .Select(o => new OrderSummaryDto(o.OrderId, o.OrderDate, o.Amount))
    .ToList();
```

Generated SQL:

```sql
SELECT [o].[OrderId], [o].[OrderDate], [o].[Amount]
FROM [Orders] AS [o]
WHERE [o].[Status] = N'Shipped'
```

**Column-count difference:** the projected query selects only the 3 columns the DTO
needs (`OrderId`, `OrderDate`, `Amount`) instead of all 5 — `CustomerId` and `Status`
are dropped from the column list entirely, even though `Status` still drives the
`WHERE` clause server-side. Rows returned: 1,856 (same row set, less data over the wire
per row).

### Side-by-side comparison

```
-- Whole-entity query: 5 columns (OrderId, CustomerId, OrderDate, Status, Amount) --
SELECT [o].[OrderId], [o].[Amount], [o].[CustomerId], [o].[OrderDate], [o].[Status]
FROM [Orders] AS [o]
WHERE [o].[Status] = N'Shipped'

-- Projected query: 3 columns (OrderId, OrderDate, Amount only) --
SELECT [o].[OrderId], [o].[OrderDate], [o].[Amount]
FROM [Orders] AS [o]
WHERE [o].[Status] = N'Shipped'
```

## Part C — Catching a client-side evaluation

[`Task2_PartC_ClientEval.cs`](ChangeTrackerDemo/Task2_PartC_ClientEval.cs).

**Broken:** filtering on a plain C# helper method inside `Where()`. EF Core has no SQL
translator for an arbitrary user-defined method, so it throws at runtime instead of
silently falling back to client evaluation (that silent-fallback behavior was removed
in EF Core 3.0+):

```csharp
private static bool IsShippedStatus(string status) =>
    status.IndexOf("hip", StringComparison.OrdinalIgnoreCase) >= 0;
...
var broken = context.Orders.Where(o => IsShippedStatus(o.Status)).ToList();
```

Actual exception:

```
System.InvalidOperationException: The LINQ expression 'DbSet<Order>()
    .Where(o => Task2_PartC_ClientEval.IsShippedStatus(o.Status))' could not be
translated. Additional information: Translation of method
'ChangeTrackerDemo.Task2_PartC_ClientEval.IsShippedStatus' failed. If this method can
be mapped to your custom function, see
https://go.microsoft.com/fwlink/?linkid=2132413 for more information. Either rewrite
the query in a form that can be translated, or switch to client evaluation explicitly
by inserting a call to 'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'.
See https://go.microsoft.com/fwlink/?linkid=2101038 for more information.
```

**Fixed:** replace the custom method with `EF.Functions.Like`, which EF Core does have
a SQL translator for:

```csharp
var fixedRows = context.Orders.Where(o => EF.Functions.Like(o.Status, "%hip%")).ToList();
```

Corrected SQL — the filter is pushed down to the database as a `LIKE`, not evaluated
in a C# loop:

```sql
SELECT [o].[OrderId], [o].[Amount], [o].[CustomerId], [o].[OrderDate], [o].[Status]
FROM [Orders] AS [o]
WHERE [o].[Status] LIKE N'%hip%'
```

Rows returned: 1,856 — matches Part A/B's exact `Status == "Shipped"` count exactly,
since `"Shipped"` is the only one of the four status values containing the substring
`"hip"`. This confirms the `WHERE` clause is doing real filtering server-side rather
than shipping all 10,000 rows over the wire.

## Full run output (this machine, 2026-08-20)

```
Day 10, Task 2: Query Translation + Projections
======================================================

=== Part A: Whole-entity query - generated SQL ===
-- context.Orders.Where(o => o.Status == "Shipped").ToList() --
   Rows returned: 1856

Generated SQL:
info: 20-08-2026 10:49:38.561 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (7ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[Amount], [o].[CustomerId], [o].[OrderDate], [o].[Status]
      FROM [Orders] AS [o]
      WHERE [o].[Status] = N'Shipped'

=== Part B: Projected query (OrderSummaryDto) - generated SQL ===
-- context.Orders.Where(o => o.Status == "Shipped")
--     .Select(o => new OrderSummaryDto(o.OrderId, o.OrderDate, o.Amount)).ToList() --
   Rows returned: 1856

Generated SQL:
info: 20-08-2026 10:49:38.678 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (5ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[OrderDate], [o].[Amount]
      FROM [Orders] AS [o]
      WHERE [o].[Status] = N'Shipped'

=== Side-by-side SQL comparison (Part A vs Part B) ===
-- Whole-entity query: 5 columns (OrderId, CustomerId, OrderDate, Status, Amount) --
info: 20-08-2026 10:49:38.561 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (7ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[Amount], [o].[CustomerId], [o].[OrderDate], [o].[Status]
      FROM [Orders] AS [o]
      WHERE [o].[Status] = N'Shipped'

-- Projected query: 3 columns (OrderId, OrderDate, Amount only) --
info: 20-08-2026 10:49:38.678 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (5ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[OrderDate], [o].[Amount]
      FROM [Orders] AS [o]
      WHERE [o].[Status] = N'Shipped'

=== Part C: Catching a client-side evaluation ===
-- BROKEN: context.Orders.Where(o => IsShippedStatus(o.Status)).ToList() --
   (IsShippedStatus is a custom C# method call - EF has no translator for it)
   Threw InvalidOperationException, as expected:
   The LINQ expression 'DbSet<Order>()
    .Where(o => Task2_PartC_ClientEval.IsShippedStatus(o.Status))' could not be translated. Additional information: Translation of method 'ChangeTrackerDemo.Task2_PartC_ClientEval.IsShippedStatus' failed. If this method can be mapped to your custom function, see https://go.microsoft.com/fwlink/?linkid=2132413 for more information. Either rewrite the query in a form that can be translated, or switch to client evaluation explicitly by inserting a call to 'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'. See https://go.microsoft.com/fwlink/?linkid=2101038 for more information.

-- FIXED: context.Orders.Where(o => EF.Functions.Like(o.Status, "%hip%")).ToList() --
   (EF.Functions.Like has a built-in translator -> SQL LIKE, pushed down to the database)
   Rows returned: 1856

Generated SQL:
info: 20-08-2026 10:49:38.749 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (8ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[Amount], [o].[CustomerId], [o].[OrderDate], [o].[Status]
      FROM [Orders] AS [o]
      WHERE [o].[Status] LIKE N'%hip%'

Done.
```
