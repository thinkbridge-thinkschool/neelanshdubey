-- Day 8, Task 2: Covering index vs. non-covering index -- Key Lookup elimination
--
-- Connection: day8-sql (localhost,1433 / sa) via the mssql VS Code extension.
-- Database:   Day8Indexing (created below if it doesn't exist yet).
--
-- Day-8/task1.sql does not exist in this repo yet, so this script (re)creates
-- the Orders table itself, using the same ~100,000-row generation approach
-- task1.sql would use (a tally-table cross join to synthesize rows, no
-- external seed data). If task1.sql is added later and already populates
-- Orders in Day8Indexing, the guards below skip regeneration and reuse that
-- data as-is -- run each numbered block below independently in VS Code.

USE master;
GO

IF DB_ID(N'Day8Indexing') IS NULL
BEGIN
    CREATE DATABASE Day8Indexing;
END
GO

USE Day8Indexing;
GO

-- =====================================================================
-- 0. Orders table -- reuse if already populated, otherwise (re)create
-- =====================================================================
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders
    (
        OrderId    INT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
        CustomerId INT           NOT NULL,
        OrderDate  DATE          NOT NULL,
        Amount     DECIMAL(10,2) NOT NULL,
        Status     VARCHAR(20)   NOT NULL
    );
END
GO

-- Status is deliberately skewed so 'Shipped' is a small slice (~5%) of the
-- ~100,000 rows. At low selectivity the optimizer favors Index Seek + Key
-- Lookup on a narrow index; at high selectivity (e.g. an even 25% split
-- across 4 statuses) it would likely prefer a full Clustered Index Scan
-- instead, and the Key Lookup this exercise is built around would never
-- show up in the BEFORE plan.
IF NOT EXISTS (SELECT 1 FROM dbo.Orders)
BEGIN
    ;WITH Tally AS (
        SELECT TOP (100000)
            ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
        FROM sys.all_objects a
        CROSS JOIN sys.all_objects b
    )
    INSERT INTO dbo.Orders (CustomerId, OrderDate, Amount, Status)
    SELECT
        1 + ABS(CHECKSUM(NEWID())) % 5000 AS CustomerId,
        DATEADD(DAY, -1 * (ABS(CHECKSUM(NEWID())) % 730), CAST(GETDATE() AS DATE)) AS OrderDate,
        CAST(5 + (ABS(CHECKSUM(NEWID())) % 99500) / 100.0 AS DECIMAL(10,2)) AS Amount,
        CASE
            WHEN ABS(CHECKSUM(NEWID())) % 100 < 40 THEN 'Pending'      -- ~40%
            WHEN ABS(CHECKSUM(NEWID())) % 100 < 75 THEN 'Delivered'    -- ~35%
            WHEN ABS(CHECKSUM(NEWID())) % 100 < 95 THEN 'Cancelled'    -- ~20%
            ELSE 'Shipped'                                             -- ~5%
        END AS Status
    FROM Tally;
END
GO

-- =====================================================================
-- 1. Non-covering index -- key column only, no INCLUDE columns
-- =====================================================================
-- Deliberately narrow: seeking this index on Status gets you back to the
-- clustering key (OrderId) but none of CustomerId/OrderDate/Amount that the
-- query below also selects, so the engine has to go fetch those from the
-- clustered index for every matching row -- that round trip is the Key
-- Lookup this block is meant to produce.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Status' AND object_id = OBJECT_ID(N'dbo.Orders'))
    DROP INDEX IX_Orders_Status ON Orders;
GO

CREATE INDEX IX_Orders_Status ON Orders (Status);
GO

-- =====================================================================
-- 2. BEFORE: run with the narrow (non-covering) index
-- =====================================================================
-- Toggle "Enable Actual Plan" (Ctrl+M) before running this block, then in
-- the plan look for:
--   - an Index Seek on IX_Orders_Status feeding a Key Lookup (Clustered
--     Index) operator on Orders, joined by Nested Loops
--   - the Key Lookup's thin connecting arrow / row count -- one lookup per
--     matching 'Shipped' row
-- In the Messages tab, STATISTICS IO reports one aggregated line for
-- "Table 'Orders'." -- logical reads on that line combine BOTH the pages
-- read via the IX_Orders_Status seek AND the pages read via the per-row Key
-- Lookup into the clustered index. Record that total now so it can be
-- compared against the AFTER block, where the Key Lookup contribution goes
-- away entirely.
SET STATISTICS IO ON;
GO

SELECT OrderId, CustomerId, OrderDate, Amount
FROM Orders
WHERE Status = 'Shipped';
GO

-- =====================================================================
-- 3. Replace the non-covering index with a covering version
-- =====================================================================
DROP INDEX IX_Orders_Status ON Orders;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Status_Covering' AND object_id = OBJECT_ID(N'dbo.Orders'))
    DROP INDEX IX_Orders_Status_Covering ON Orders;
GO

CREATE INDEX IX_Orders_Status_Covering ON Orders (Status)
INCLUDE (OrderId, CustomerId, OrderDate, Amount);
GO

-- =====================================================================
-- 4. AFTER: rerun the identical SELECT against the covering index
-- =====================================================================
-- Toggle "Enable Actual Plan" again and rerun. Expected plan: a single
-- Index Seek (NonClustered) on IX_Orders_Status_Covering -- no Key Lookup
-- operator and no Nested Loops join, because Status, OrderId, CustomerId,
-- OrderDate, and Amount are all present in the index's leaf pages, so the
-- engine never needs to visit the clustered index at all.
-- In the Messages tab, the "Table 'Orders'." logical reads line should now
-- be attributable entirely to that one nonclustered index seek -- no
-- separate Key Lookup contribution is being added on top -- so the total
-- should come out noticeably lower than the BEFORE block's total.
SET STATISTICS IO ON;
GO

SELECT OrderId, CustomerId, OrderDate, Amount
FROM Orders
WHERE Status = 'Shipped';
GO

-- =====================================================================
-- 5. Writeup summary -- what to screenshot / note
-- =====================================================================
-- 1. Execution plans: BEFORE shows Index Seek (IX_Orders_Status) + Key
--    Lookup (Clustered Index Scan/Seek) joined via Nested Loops; AFTER
--    shows a single Index Seek (IX_Orders_Status_Covering) with no Key
--    Lookup operator anywhere in the plan.
-- 2. Logical reads: note the "Table 'Orders'." logical reads figure from
--    STATISTICS IO in both the BEFORE and AFTER Messages output and compare
--    the two totals directly -- AFTER should be substantially lower since
--    the per-row Key Lookup cost is gone.
-- 3. Tradeoff: IX_Orders_Status_Covering duplicates Status plus the four
--    INCLUDE columns (OrderId, CustomerId, OrderDate, Amount) for every row
--    in Orders, on top of the base table and the original index. That's
--    real extra storage, and every INSERT/UPDATE/DELETE touching Orders now
--    has one more (wider) index to maintain, so writes get slightly slower
--    in exchange for this query no longer paying for Key Lookups on reads.
