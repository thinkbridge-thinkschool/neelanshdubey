using ChangeTrackerDemo;

Console.WriteLine("Day 10, Task 1: EF Core Change Tracker + AsNoTracking");
Console.WriteLine("======================================================");
Console.WriteLine();

DbInitializer.EnsureSeeded();
Console.WriteLine();

PartA_IdentityResolution.Run(targetId: 1);

PartB_TrackedVsUntracked.Run(trackedMutateId: 2, noTrackMutateId: 3);

PartC_Benchmark.Run();

Console.WriteLine("Done.");
