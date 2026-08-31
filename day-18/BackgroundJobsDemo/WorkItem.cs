namespace BackgroundJobsDemo;

public sealed record WorkItem(int Id, string Name, TimeSpan Duration);
