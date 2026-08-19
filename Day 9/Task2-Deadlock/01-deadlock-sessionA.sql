-- 01-deadlock-sessionA.sql
-- Classic two-resource deadlock demo (Session A).
-- Locks Accounts row 1 first, holds it, then requests Orders row 2 --
-- the resource Session B grabs first. Run alongside
-- 01-deadlock-sessionB.sql (which locks the two resources in the opposite
-- order) so the two transactions circular-wait on each other and SQL
-- Server's deadlock monitor picks a victim (error 1205).

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
