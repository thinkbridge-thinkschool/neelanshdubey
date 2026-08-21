USE NPlusOneFixDb;
GO
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
SET STATISTICS XML ON;
GO
SELECT [b].[Id], [b].[Title], [b].[PublishedYear]
FROM [Books] AS [b]
WHERE [b].[AuthorId] = 250;
GO
