/*
    Day 8 - Task 1: Clustered vs Non-Clustered Index Exercise
    ==========================================================
    Run each GO-separated block yourself in VS Code (mssql extension).
    Toggle "Enable Actual Plan" per query as needed.

    NOTE ON ORDERING vs the original request list:
    The "write-cost" comparison (0 non-clustered indexes vs 2 non-clustered
    indexes) needs its "0 indexes" measurement taken BEFORE the two
    non-clustered indexes exist. So that insert is placed right after the
    clustered index step (Section 5) and before the CustomerId non-clustered
    index (Section 6), rather than at the very end. The "2 indexes" insert
    stays at the end, after both non-clustered indexes exist. Everything
    else follows the requested order.
*/

-- ============================================================
-- 1. CREATE DATABASE
-- ============================================================
CREATE DATABASE Day8Indexing;
GO

USE Day8Indexing;
GO

-- ============================================================
-- 2. Orders table as a HEAP (no clustered index yet)
-- ============================================================
CREATE TABLE dbo.Orders
(
    OrderId     INT IDENTITY(1,1) NOT NULL,
    CustomerId  INT           NOT NULL,
    OrderDate   DATETIME2(3)  NOT NULL,
    Status      VARCHAR(20)   NOT NULL,
    Amount      DECIMAL(10,2) NOT NULL,
    Notes       VARCHAR(500)  NOT NULL
);
GO

-- ============================================================
-- 3. Populate ~100,000 rows (set-based, no WHILE loop)
--    Cascading CROSS JOINs on a base-10 tally CTE: 10 -> 100 -> 10,000 -> 1,000,000
-- ============================================================
;WITH
E1(N)   AS (SELECT N FROM (VALUES (1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) v(N)),      -- 10 rows
E2(N)   AS (SELECT 1 FROM E1 a CROSS JOIN E1 b),                                        -- 100 rows
E3(N)   AS (SELECT 1 FROM E2 a CROSS JOIN E2 b),                                        -- 10,000 rows
E4(N)   AS (SELECT 1 FROM E3 a CROSS JOIN E2 b),                                        -- 1,000,000 rows
Tally   AS (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N FROM E4)
INSERT INTO dbo.Orders (CustomerId, OrderDate, Status, Amount, Notes)
SELECT TOP (100000)
    CustomerId = ABS(CHECKSUM(NEWID())) % 5000 + 1,                                     -- 1..5000
    OrderDate  = DATEADD(SECOND, -(ABS(CHECKSUM(NEWID())) % (730 * 24 * 3600)), SYSDATETIME()), -- last 2 years
    Status     = CASE ABS(CHECKSUM(NEWID())) % 4
                     WHEN 0 THEN 'Pending'
                     WHEN 1 THEN 'Shipped'
                     WHEN 2 THEN 'Delivered'
                     ELSE 'Cancelled'
                 END,
    Amount     = CAST(ABS(CHECKSUM(NEWID())) % 1000000 / 100.0 AS DECIMAL(10,2)),       -- 0.00..9999.99
    Notes      = LEFT(REPLICATE('FillerData-BulkRow-', 30), 480)
FROM Tally;
GO

-- Sanity check row count
SELECT COUNT(*) AS RowCount FROM dbo.Orders;
GO

-- ============================================================
-- 4. -- BEFORE any index: HEAP baseline
-- ============================================================
SET STATISTICS IO ON;
GO

-- BEFORE (heap) - point lookup on CustomerId
SELECT * FROM dbo.Orders WHERE CustomerId = 2500;
GO

-- BEFORE (heap) - date-range scan
SELECT OrderId, OrderDate, Amount FROM dbo.Orders
WHERE OrderDate >= '2025-01-01' AND OrderDate < '2025-02-01';
GO

SET STATISTICS IO OFF;
GO

-- ============================================================
-- 5. Add CLUSTERED index on OrderId, rerun CustomerId query
-- ============================================================
ALTER TABLE dbo.Orders
ADD CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId);
GO

SET STATISTICS IO ON;
GO

-- AFTER (clustered on OrderId) - same point lookup on CustomerId
SELECT * FROM dbo.Orders WHERE CustomerId = 2500;
GO

SET STATISTICS IO OFF;
GO

-- ============================================================
-- 8a. -- BEFORE non-clustered indexes exist: write-cost baseline
--     (0 non-clustered indexes present at this point - only the clustered PK)
-- ============================================================
;WITH
E1(N)   AS (SELECT N FROM (VALUES (1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) v(N)),
E2(N)   AS (SELECT 1 FROM E1 a CROSS JOIN E1 b),
E3(N)   AS (SELECT 1 FROM E2 a CROSS JOIN E2 b),
Tally   AS (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N FROM E3)
SELECT TOP (5000)
    CustomerId = ABS(CHECKSUM(NEWID())) % 5000 + 1,
    OrderDate  = DATEADD(SECOND, -(ABS(CHECKSUM(NEWID())) % (730 * 24 * 3600)), SYSDATETIME()),
    Status     = CASE ABS(CHECKSUM(NEWID())) % 4
                     WHEN 0 THEN 'Pending'
                     WHEN 1 THEN 'Shipped'
                     WHEN 2 THEN 'Delivered'
                     ELSE 'Cancelled'
                 END,
    Amount     = CAST(ABS(CHECKSUM(NEWID())) % 1000000 / 100.0 AS DECIMAL(10,2)),
    Notes      = LEFT(REPLICATE('FillerData-BulkRow-', 30), 480)
INTO #StageBefore
FROM Tally;
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

-- BEFORE (0 non-clustered indexes) - insert 5,000 rows in one batch
INSERT INTO dbo.Orders (CustomerId, OrderDate, Status, Amount, Notes)
SELECT CustomerId, OrderDate, Status, Amount, Notes FROM #StageBefore;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO

DROP TABLE #StageBefore;
GO

-- ============================================================
-- 6. Add NON-CLUSTERED index on CustomerId, rerun CustomerId query
-- ============================================================
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId);
GO

SET STATISTICS IO ON;
GO

-- AFTER (non-clustered index on CustomerId) - same point lookup
SELECT * FROM dbo.Orders WHERE CustomerId = 2500;
GO

SET STATISTICS IO OFF;
GO

-- ============================================================
-- 7. Add NON-CLUSTERED covering index on OrderDate INCLUDE (Amount),
--    rerun the date-range query
-- ============================================================
CREATE NONCLUSTERED INDEX IX_Orders_OrderDate_Include_Amount
    ON dbo.Orders (OrderDate) INCLUDE (Amount);
GO

SET STATISTICS IO ON;
GO

-- AFTER (covering non-clustered index on OrderDate) - same date-range query
SELECT OrderId, OrderDate, Amount FROM dbo.Orders
WHERE OrderDate >= '2025-01-01' AND OrderDate < '2025-02-01';
GO

SET STATISTICS IO OFF;
GO

-- ============================================================
-- 8b. -- AFTER both non-clustered indexes exist: write-cost comparison
--     (2 non-clustered indexes present: IX_Orders_CustomerId,
--      IX_Orders_OrderDate_Include_Amount)
-- ============================================================
;WITH
E1(N)   AS (SELECT N FROM (VALUES (1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) v(N)),
E2(N)   AS (SELECT 1 FROM E1 a CROSS JOIN E1 b),
E3(N)   AS (SELECT 1 FROM E2 a CROSS JOIN E2 b),
Tally   AS (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N FROM E3)
SELECT TOP (5000)
    CustomerId = ABS(CHECKSUM(NEWID())) % 5000 + 1,
    OrderDate  = DATEADD(SECOND, -(ABS(CHECKSUM(NEWID())) % (730 * 24 * 3600)), SYSDATETIME()),
    Status     = CASE ABS(CHECKSUM(NEWID())) % 4
                     WHEN 0 THEN 'Pending'
                     WHEN 1 THEN 'Shipped'
                     WHEN 2 THEN 'Delivered'
                     ELSE 'Cancelled'
                 END,
    Amount     = CAST(ABS(CHECKSUM(NEWID())) % 1000000 / 100.0 AS DECIMAL(10,2)),
    Notes      = LEFT(REPLICATE('FillerData-BulkRow-', 30), 480)
INTO #StageAfter
FROM Tally;
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

-- AFTER (2 non-clustered indexes) - insert 5,000 rows in one batch
INSERT INTO dbo.Orders (CustomerId, OrderDate, Status, Amount, Notes)
SELECT CustomerId, OrderDate, Status, Amount, Notes FROM #StageAfter;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO

DROP TABLE #StageAfter;
GO
