-- 03-phantom-read-sessionA.sql
-- SESSION A -- run in query tab #1 (localhost,1434). Run 00-setup.sql first.

USE Day9Isolation;
GO

------------------------------------------------------------------------------
-- PART 1 -- anomaly ALLOWED
-- REPEATABLE READ locks rows it has read but not the range/predicate, so a
-- new row matching the WHERE clause can be inserted and committed by another
-- session in between reads. Run this batch, then switch to Session B and
-- run its PART 1 while this session is inside WAITFOR DELAY.
------------------------------------------------------------------------------
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;

SELECT AccountId, Balance FROM Accounts WHERE Balance > 0; -- baseline: AccountId 1, 2

WAITFOR DELAY '00:00:10'; -- <- run Session B PART 1 now (inserts AccountId 3, commits)

SELECT AccountId, Balance FROM Accounts WHERE Balance > 0; -- AccountId 1, 2, 3 -- phantom row appears

COMMIT TRANSACTION;
GO

------------------------------------------------------------------------------
-- PART 2 -- anomaly PREVENTED
-- SERIALIZABLE takes a range lock covering the WHERE Balance > 0 predicate,
-- so Session B's INSERT (PART 2) will block until this transaction ends.
-- Run this batch, then switch to Session B and run its PART 2 while this
-- session is inside WAITFOR DELAY.
------------------------------------------------------------------------------
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRANSACTION;

SELECT AccountId, Balance FROM Accounts WHERE Balance > 0; -- baseline: AccountId 1, 2, 3

WAITFOR DELAY '00:00:10'; -- <- run Session B PART 2 now; it will block

SELECT AccountId, Balance FROM Accounts WHERE Balance > 0; -- identical to first read, no phantom

COMMIT TRANSACTION; -- Session B's blocked INSERT then proceeds
GO
