-- 01-dirty-read-sessionB.sql
-- SESSION B -- run in query tab #2 (localhost,1434). Run 00-setup.sql first.

USE Day9Isolation;
GO

------------------------------------------------------------------------------
-- PART 1 -- anomaly ALLOWED
-- Run this WHILE Session A is inside its PART 1 WAITFOR DELAY (transaction
-- still open, uncommitted UPDATE in place).
------------------------------------------------------------------------------
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT AccountId, Balance
FROM Accounts
WHERE AccountId = 1; -- expected: 900 -- a dirty read of Session A's uncommitted change
GO

------------------------------------------------------------------------------
-- PART 2 -- anomaly PREVENTED
-- Run this WHILE Session A is inside its PART 2 WAITFOR DELAY. Under READ
-- COMMITTED this SELECT blocks on Session A's exclusive lock instead of
-- reading the uncommitted value.
------------------------------------------------------------------------------
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

SELECT AccountId, Balance
FROM Accounts
WHERE AccountId = 1; -- blocks, then returns 1000 once Session A rolls back
GO
