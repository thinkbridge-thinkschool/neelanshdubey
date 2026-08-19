-- 02-fix-consistent-ordering-sessionB.sql
-- Fixed version of 01-deadlock-sessionB.sql. The transaction still does
-- the same work (bump Orders row 2, adjust Accounts row 1), but now
-- acquires locks in the same order as Session A -- Accounts before
-- Orders -- instead of the reverse order that caused the deadlock. Run
-- alongside 02-fix-consistent-ordering-sessionA.sql: this session will
-- simply block on Session A's Accounts lock until A commits, then
-- proceed and commit itself. No deadlock.

USE Day9Isolation;
GO

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - requesting Accounts row (AccountId = 1)';
    UPDATE Accounts SET Balance = Balance + 100 WHERE AccountId = 1;

    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - acquired Accounts lock, waiting 5s before requesting Orders row (OrderId = 2)';
    WAITFOR DELAY '00:00:05';

    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - requesting Orders row (OrderId = 2)';
    UPDATE Orders SET Amount = Amount + 5 WHERE OrderId = 2;

    COMMIT TRANSACTION;
    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - COMMITTED';
END TRY
BEGIN CATCH
    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - ERROR ' + CAST(ERROR_NUMBER() AS VARCHAR) + ': ' + ERROR_MESSAGE();
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
END CATCH
GO
