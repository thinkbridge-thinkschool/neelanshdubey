-- 02-nonrepeatable-read-sessionB.sql
-- SESSION B -- run in query tab #2 (localhost,1434). Run 00-setup.sql first.

USE Day9Isolation;
GO

------------------------------------------------------------------------------
-- PART 1 -- anomaly ALLOWED
-- Run this WHILE Session A is inside its PART 1 WAITFOR DELAY. No explicit
-- transaction, so this UPDATE commits immediately.
------------------------------------------------------------------------------
UPDATE Accounts SET Balance = Balance + 50 WHERE AccountId = 2; -- 500 -> 550, commits right away
GO

------------------------------------------------------------------------------
-- PART 2 -- anomaly PREVENTED
-- Run this WHILE Session A is inside its PART 2 WAITFOR DELAY. Session A's
-- REPEATABLE READ transaction still holds a shared lock on this row, so
-- this UPDATE blocks until Session A commits.
------------------------------------------------------------------------------
UPDATE Accounts SET Balance = Balance + 50 WHERE AccountId = 2; -- blocks until Session A's transaction ends
GO
