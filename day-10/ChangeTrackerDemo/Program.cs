using ChangeTrackerDemo;

Console.WriteLine("Day 10, Task 1: EF Core Change Tracker + AsNoTracking");
Console.WriteLine("======================================================");
Console.WriteLine();

DbInitializer.EnsureSeeded();
Console.WriteLine();

PartA_IdentityResolution.Run(targetId: 1);

PartB_TrackedVsUntracked.Run(trackedMutateId: 2, noTrackMutateId: 3);

PartC_Benchmark.Run();

Console.WriteLine("Day 10, Task 2: Query Translation + Projections");
Console.WriteLine("======================================================");
Console.WriteLine();

var wholeEntitySql = Task2_PartA_WholeEntityQuery.Run();
var projectedSql = Task2_PartB_Projection.Run();
Task2_PartB_Projection.PrintComparison(wholeEntitySql, projectedSql);

Task2_PartC_ClientEval.Run();

Console.WriteLine("Done.");
