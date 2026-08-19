-- 01-deadlock-sessionB.sql
-- Classic two-resource deadlock demo (Session B).
-- Locks Orders row 2 first, holds it, then requests Accounts row 1 --
-- the reverse order of 01-deadlock-sessionA.sql. Run alongside that
-- script so the two transactions circular-wait on each other and SQL
-- Server's deadlock monitor picks a victim (error 1205).

USE Day9Isolation;
GO

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - locking Orders row (OrderId = 2)';
    UPDATE Orders SET Amount = Amount + 5 WHERE OrderId = 2;

    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - holding lock, waiting 5s before requesting Accounts row (AccountId = 1)';
    WAITFOR DELAY '00:00:05';

    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - requesting Accounts row (AccountId = 1)';
    UPDATE Accounts SET Balance = Balance + 100 WHERE AccountId = 1;

    COMMIT TRANSACTION;
    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - COMMITTED';
END TRY
BEGIN CATCH
    PRINT 'Session B: ' + CONVERT(VARCHAR, GETDATE(), 108) + ' - ERROR ' + CAST(ERROR_NUMBER() AS VARCHAR) + ': ' + ERROR_MESSAGE();
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
END CATCH
GO
