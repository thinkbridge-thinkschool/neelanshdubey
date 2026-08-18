-- Day 8, Task 1: Clustered vs. non-clustered indexes -- heap baseline,
-- clustered PK, then two non-clustered indexes, with read AND write cost
-- comparisons at each stage.
--
-- Connection: day8-sql (localhost,1433 / sa) via the mssql VS Code extension.
-- Database:   Day8Indexing (created below if it doesn't exist yet).
--
-- Run each numbered block below independently -- they're separated by GO so
-- you can execute one at a time and toggle "Enable Actual Plan" per query.

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
-- 1. Orders table -- HEAP (no clustered index yet)
-- =====================================================================
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders
    (
        OrderId    INT IDENTITY(1,1) NOT NULL,
        CustomerId INT           NOT NULL,
        OrderDate  DATETIME2     NOT NULL,
        Status     VARCHAR(20)   NOT NULL,
        Amount     DECIMAL(10,2) NOT NULL,
        Notes      VARCHAR(500)  NOT NULL
    );
END
GO

-- =====================================================================
-- 2. Populate ~100,000 rows -- set-based tally table, no WHILE loop
-- =====================================================================
-- Cascading CROSS JOINs double the row count at each level (L0=2, L1=4,
-- L2=16, L3=256, L4=65536, L5=~4.3 billion); TOP (100000) over the final
-- ROW_NUMBER() acts as a row goal so SQL Server stops generating numbers
-- once it has enough, instead of materializing the full cross join.
IF NOT EXISTS (SELECT 1 FROM dbo.Orders)
BEGIN
    ;WITH
    L0 AS (SELECT 1 AS c UNION ALL SELECT 1),
    L1 AS (SELECT 1 AS c FROM L0 A CROSS JOIN L0 B),
    L2 AS (SELECT 1 AS c FROM L1 A CROSS JOIN L1 B),
    L3 AS (SELECT 1 AS c FROM L2 A CROSS JOIN L2 B),
    L4 AS (SELECT 1 AS c FROM L3 A CROSS JOIN L3 B),
    L5 AS (SELECT 1 AS c FROM L4 A CROSS JOIN L4 B),
    Tally AS (
        SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
        FROM L5
    )
    INSERT INTO dbo.Orders (CustomerId, OrderDate, Status, Amount, Notes)
    SELECT
        1 + ABS(CHECKSUM(NEWID())) % 5000 AS CustomerId,
        DATEADD(SECOND, -1 * (ABS(CHECKSUM(NEWID())) % (2 * 365 * 24 * 60 * 60)), SYSDATETIME()) AS OrderDate,
        CASE ABS(CHECKSUM(NEWID())) % 4
            WHEN 0 THEN 'Pending'
            WHEN 1 THEN 'Shipped'
            WHEN 2 THEN 'Delivered'
            ELSE 'Cancelled'
        END AS Status,
        CAST(5 + (ABS(CHECKSUM(NEWID())) % 99500) / 100.0 AS DECIMAL(10,2)) AS Amount,
        LEFT(REPLICATE('Sample order note text used to pad row size for IO comparisons. ', 10), 500) AS Notes
    FROM Tally;
END
GO

-- =====================================================================
-- -- BEFORE: heap baseline (no indexes at all) --
-- =====================================================================
-- Toggle "Enable Actual Plan" before running these. Expect Table Scans on
-- the heap for both queries -- record the STATISTICS IO logical reads for
-- "Table 'Orders'." on each SELECT below before moving on.
SET STATISTICS IO ON;
GO

SELECT * FROM Orders WHERE CustomerId = 2500;
GO

SELECT OrderId, OrderDate, Amount FROM Orders
WHERE OrderDate >= '2025-01-01' AND OrderDate < '2025-02-01';
GO

-- =====================================================================
-- -- AFTER: clustered index on OrderId (PK) --
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'PK_Orders' AND parent_object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    ALTER TABLE Orders ADD CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId);
END
GO

-- Rerun query 1 (CustomerId lookup) -- expect a Clustered Index Scan now
-- (still no index on CustomerId), so compare logical reads against the
-- heap's Table Scan above rather than expecting a seek yet.
SET STATISTICS IO ON;
GO

SELECT * FROM Orders WHERE CustomerId = 2500;
GO

-- =====================================================================
-- -- WRITE COST 8a: clustered PK only, 0 non-clustered indexes --
-- =====================================================================
-- Baseline write cost with just the clustered index to maintain. Compare
-- these STATISTICS IO / STATISTICS TIME numbers against the 8b block at the
-- end of this file, once two non-clustered indexes also exist.
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

;WITH
L0 AS (SELECT 1 AS c UNION ALL SELECT 1),
L1 AS (SELECT 1 AS c FROM L0 A CROSS JOIN L0 B),
L2 AS (SELECT 1 AS c FROM L1 A CROSS JOIN L1 B),
L3 AS (SELECT 1 AS c FROM L2 A CROSS JOIN L2 B),
L4 AS (SELECT 1 AS c FROM L3 A CROSS JOIN L3 B),
Tally5k AS (
    SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM L4
)
INSERT INTO Orders (CustomerId, OrderDate, Status, Amount, Notes)
SELECT
    1 + ABS(CHECKSUM(NEWID())) % 5000 AS CustomerId,
    DATEADD(SECOND, -1 * (ABS(CHECKSUM(NEWID())) % (2 * 365 * 24 * 60 * 60)), SYSDATETIME()) AS OrderDate,
    CASE ABS(CHECKSUM(NEWID())) % 4
        WHEN 0 THEN 'Pending'
        WHEN 1 THEN 'Shipped'
        WHEN 2 THEN 'Delivered'
        ELSE 'Cancelled'
    END AS Status,
    CAST(5 + (ABS(CHECKSUM(NEWID())) % 99500) / 100.0 AS DECIMAL(10,2)) AS Amount,
    LEFT(REPLICATE('Sample order note text used to pad row size for IO comparisons. ', 10), 500) AS Notes
FROM Tally5k;
GO

-- =====================================================================
-- -- AFTER: non-clustered index on CustomerId --
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_CustomerId' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders (CustomerId);
END
GO

-- Rerun the CustomerId query -- expect an Index Seek on IX_Orders_CustomerId
-- feeding a Key Lookup into the clustered index (SELECT * pulls columns not
-- in this narrow index), and lower logical reads than the clustered scan.
SET STATISTICS IO ON;
GO

SELECT * FROM Orders WHERE CustomerId = 2500;
GO

-- =====================================================================
-- -- AFTER: covering non-clustered index on OrderDate INCLUDE (Amount) --
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_OrderDate_Covering' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_OrderDate_Covering ON Orders (OrderDate) INCLUDE (Amount);
END
GO

-- Rerun the date-range query -- expect a single Index Seek on
-- IX_Orders_OrderDate_Covering with no Key Lookup, since OrderId, OrderDate,
-- and Amount are all present in the index leaf.
SET STATISTICS IO ON;
GO

SELECT OrderId, OrderDate, Amount FROM Orders
WHERE OrderDate >= '2025-01-01' AND OrderDate < '2025-02-01';
GO

-- =====================================================================
-- -- WRITE COST 8b: clustered PK + 2 non-clustered indexes --
-- =====================================================================
-- Same shape of insert as 8a, but now every row has to update the clustered
-- index AND both non-clustered indexes (IX_Orders_CustomerId,
-- IX_Orders_OrderDate_Covering). Compare STATISTICS IO / STATISTICS TIME
-- here against 8a to see the write-cost impact of the two extra indexes.
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

;WITH
L0 AS (SELECT 1 AS c UNION ALL SELECT 1),
L1 AS (SELECT 1 AS c FROM L0 A CROSS JOIN L0 B),
L2 AS (SELECT 1 AS c FROM L1 A CROSS JOIN L1 B),
L3 AS (SELECT 1 AS c FROM L2 A CROSS JOIN L2 B),
L4 AS (SELECT 1 AS c FROM L3 A CROSS JOIN L3 B),
Tally5k AS (
    SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM L4
)
INSERT INTO Orders (CustomerId, OrderDate, Status, Amount, Notes)
SELECT
    1 + ABS(CHECKSUM(NEWID())) % 5000 AS CustomerId,
    DATEADD(SECOND, -1 * (ABS(CHECKSUM(NEWID())) % (2 * 365 * 24 * 60 * 60)), SYSDATETIME()) AS OrderDate,
    CASE ABS(CHECKSUM(NEWID())) % 4
        WHEN 0 THEN 'Pending'
        WHEN 1 THEN 'Shipped'
        WHEN 2 THEN 'Delivered'
        ELSE 'Cancelled'
    END AS Status,
    CAST(5 + (ABS(CHECKSUM(NEWID())) % 99500) / 100.0 AS DECIMAL(10,2)) AS Amount,
    LEFT(REPLICATE('Sample order note text used to pad row size for IO comparisons. ', 10), 500) AS Notes
FROM Tally5k;
GO
