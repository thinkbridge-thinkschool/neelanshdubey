-- 02-nonrepeatable-read-sessionA.sql
-- SESSION A -- run in query tab #1 (localhost,1434). Run 00-setup.sql first.

USE Day9Isolation;
GO

------------------------------------------------------------------------------
-- PART 1 -- anomaly ALLOWED
-- READ COMMITTED (default) only holds shared locks for the instant of each
-- read, so a second read inside the same transaction can see a value
-- committed by another session in between. Run this batch, then switch to
-- Session B and run its PART 1 while this session is inside WAITFOR DELAY.
------------------------------------------------------------------------------
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;

SELECT Balance FROM Accounts WHERE AccountId = 2; -- first read: 500

WAITFOR DELAY '00:00:10'; -- <- run Session B PART 1 now (updates + commits)

SELECT Balance FROM Accounts WHERE AccountId = 2; -- second read: 550 -- same row, different value

COMMIT TRANSACTION;
GO

------------------------------------------------------------------------------
-- PART 2 -- anomaly PREVENTED
-- REPEATABLE READ holds shared locks for the life of the transaction, so
-- Session B's UPDATE (PART 2) will block until this transaction ends. Run
-- this batch, then switch to Session B and run its PART 2 while this
-- session is inside WAITFOR DELAY.
------------------------------------------------------------------------------
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;

SELECT Balance FROM Accounts WHERE AccountId = 2; -- first read: 550 (from PART 1)

WAITFOR DELAY '00:00:10'; -- <- run Session B PART 2 now; it will block

SELECT Balance FROM Accounts WHERE AccountId = 2; -- second read: identical to first read

COMMIT TRANSACTION; -- Session B's blocked UPDATE then proceeds
GO
