-- 03-phantom-read-sessionB.sql
-- SESSION B -- run in query tab #2 (localhost,1434). Run 00-setup.sql first.

USE Day9Isolation;
GO

------------------------------------------------------------------------------
-- PART 1 -- anomaly ALLOWED
-- Run this WHILE Session A is inside its PART 1 WAITFOR DELAY. No explicit
-- transaction, so this INSERT commits immediately.
------------------------------------------------------------------------------
INSERT INTO Accounts (AccountId, Balance) VALUES (3, 250); -- matches Session A's WHERE Balance > 0
GO

------------------------------------------------------------------------------
-- PART 2 -- anomaly PREVENTED
-- Run this WHILE Session A is inside its PART 2 WAITFOR DELAY. Session A's
-- SERIALIZABLE transaction holds a range lock on Balance > 0, so this
-- INSERT blocks until Session A commits.
------------------------------------------------------------------------------
INSERT INTO Accounts (AccountId, Balance) VALUES (4, 300); -- blocks until Session A's transaction ends
GO
