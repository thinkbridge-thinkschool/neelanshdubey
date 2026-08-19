-- 01-dirty-read-sessionA.sql
-- SESSION A -- run in query tab #1 (localhost,1434). Run 00-setup.sql first.

USE Day9Isolation;
GO

------------------------------------------------------------------------------
-- PART 1 -- anomaly ALLOWED
-- Session B (PART 1) uses READ UNCOMMITTED, so it can see this uncommitted
-- change. Run this batch, then switch to Session B and run its PART 1 while
-- this session is inside the WAITFOR DELAY below.
------------------------------------------------------------------------------
BEGIN TRANSACTION;

UPDATE Accounts SET Balance = Balance - 100 WHERE AccountId = 1; -- 1000 -> 900, uncommitted

WAITFOR DELAY '00:00:10'; -- <- run Session B PART 1 now

ROLLBACK TRANSACTION; -- the -100 never actually happened; anything B read was "dirty"
GO

------------------------------------------------------------------------------
-- PART 2 -- anomaly PREVENTED
-- Session B (PART 2) uses READ COMMITTED (SQL Server default). Its SELECT
-- will block on this transaction's exclusive lock instead of reading the
-- uncommitted value. Run this batch, then switch to Session B and run its
-- PART 2 while this session is inside the WAITFOR DELAY below.
------------------------------------------------------------------------------
BEGIN TRANSACTION;

UPDATE Accounts SET Balance = Balance - 100 WHERE AccountId = 1; -- 1000 -> 900, uncommitted

WAITFOR DELAY '00:00:10'; -- <- run Session B PART 2 now; it will block until this ends

ROLLBACK TRANSACTION; -- Session B's blocked SELECT then unblocks and returns 1000 (committed value only)
GO
