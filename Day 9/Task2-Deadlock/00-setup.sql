-- 00-setup.sql
-- Run once (either session) before any of the deadlock scripts.
-- Reuses the existing Day9Isolation.Accounts table and adds a minimal
-- Orders table so the deadlock demo has two distinct resources to lock.

USE Day9Isolation;
GO

IF OBJECT_ID('dbo.Orders', 'U') IS NOT NULL
    DROP TABLE dbo.Orders;
GO

-- Reset Accounts to known values (create it if it somehow doesn't exist yet).
IF OBJECT_ID('dbo.Accounts', 'U') IS NULL
BEGIN
    CREATE TABLE Accounts (
        AccountId INT PRIMARY KEY,
        Balance   INT NOT NULL
    );
END
GO

DELETE FROM Accounts WHERE AccountId IN (1, 2);
INSERT INTO Accounts (AccountId, Balance) VALUES (1, 1000), (2, 500);
GO

CREATE TABLE Orders (
    OrderId   INT PRIMARY KEY,
    AccountId INT NOT NULL,
    Amount    INT NOT NULL
);
GO

INSERT INTO Orders (OrderId, AccountId, Amount) VALUES
    (1, 1, 100),
    (2, 2, 50);
GO

SELECT * FROM Accounts;
SELECT * FROM Orders;
GO
