-- 00-setup.sql
-- Run once (either session) before any of the anomaly scripts.

IF DB_ID('Day9Isolation') IS NOT NULL
BEGIN
    ALTER DATABASE Day9Isolation SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE Day9Isolation;
END
GO

CREATE DATABASE Day9Isolation;
GO

USE Day9Isolation;
GO

CREATE TABLE Accounts (
    AccountId INT PRIMARY KEY,
    Balance   INT NOT NULL
);
GO

INSERT INTO Accounts (AccountId, Balance) VALUES
    (1, 1000),
    (2, 500);
GO

SELECT * FROM Accounts;
GO
