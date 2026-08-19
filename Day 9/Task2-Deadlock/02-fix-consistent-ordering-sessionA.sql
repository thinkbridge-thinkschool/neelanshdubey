-- 02-fix-consistent-ordering-sessionA.sql
-- Same transaction as 01-deadlock-sessionA.sql, unchanged: it already
-- acquires Accounts before Orders. Run alongside
-- 02-fix-consistent-ordering-sessionB.sql, which has been fixed to use
-- the same lock order (Accounts before Orders) instead of the reverse
-- order it used in the deadlock repro. With both sessions agreeing on
-- resource order, Session B simply blocks behind Session A -- no
-- circular wait, no deadlock.

USE Day9Isolation;
GO

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT 'Session A: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - locking Accounts row (AccountId = 1)';
    UPDATE Accounts SET Balance = Balance - 100 WHERE AccountId = 1;

    PRINT 'Session A: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - holding lock, waiting 5s before requesting Orders row (OrderId = 2)';
    WAITFOR DELAY '00:00:05';

    PRINT 'Session A: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - requesting Orders row (OrderId = 2)';
    UPDATE Orders SET Amount = Amount + 10 WHERE OrderId = 2;

    COMMIT TRANSACTION;
    PRINT 'Session A: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - COMMITTED';
END TRY
BEGIN CATCH
    PRINT 'Session A: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - ERROR ' + CAST(ERROR_NUMBER() AS VARCHAR) + ': ' + ERROR_MESSAGE();
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
END CATCH
GO
