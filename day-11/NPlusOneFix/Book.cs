namespace NPlusOneFix;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int AuthorId { get; set; }
    public int PublishedYear { get; set; }

    public virtual Author Author { get; set; } = null!;
}
