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

## Full run output (this machine, 2026-08-20)

```
Day 10, Task 2: Query Translation + Projections
======================================================

=== Part A: Whole-entity query - generated SQL ===
-- context.Orders.Where(o => o.Status == "Shipped").ToList() --
   Rows returned: 1856

Generated SQL:
info: 20-08-2026 10:46:26.805 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[Amount], [o].[CustomerId], [o].[OrderDate], [o].[Status]
      FROM [Orders] AS [o]
      WHERE [o].[Status] = N'Shipped'

=== Part B: Projected query (OrderSummaryDto) - generated SQL ===
-- context.Orders.Where(o => o.Status == "Shipped")
--     .Select(o => new OrderSummaryDto(o.OrderId, o.OrderDate, o.Amount)).ToList() --
   Rows returned: 1856

Generated SQL:
info: 20-08-2026 10:46:26.920 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[OrderDate], [o].[Amount]
      FROM [Orders] AS [o]
      WHERE [o].[Status] = N'Shipped'

=== Side-by-side SQL comparison (Part A vs Part B) ===
-- Whole-entity query: 5 columns (OrderId, CustomerId, OrderDate, Status, Amount) --
info: 20-08-2026 10:46:26.805 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[Amount], [o].[CustomerId], [o].[OrderDate], [o].[Status]
      FROM [Orders] AS [o]
      WHERE [o].[Status] = N'Shipped'

-- Projected query: 3 columns (OrderId, OrderDate, Amount only) --
info: 20-08-2026 10:46:26.920 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT [o].[OrderId], [o].[OrderDate], [o].[Amount]
      FROM [Orders] AS [o]
      WHERE [o].[Status] = N'Shipped'

Done.
```

Part C (client-side evaluation) is covered separately below, in its own commit.
