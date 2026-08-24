namespace NPlusOneFix;

public class AuthorBookSummaryDto
{
    public int AuthorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BookCount { get; set; }
    public string? LatestBookTitle { get; set; }
}
