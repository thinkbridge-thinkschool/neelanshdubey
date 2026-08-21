using QuotesApi.Models;

namespace QuotesApi.Data;

/// <summary>
/// Seeds 200 authors with ~50 quotes each (10,000 quotes total) for the
/// Day 11 Task 1 N+1 / missing-index profiling exercise. Uses a fixed
/// random seed so the generated data is reproducible across runs.
/// </summary>
public static class AuthorQuoteSeeder
{
    private const int AuthorCount = 200;
    private const int MinQuotesPerAuthor = 45;
    private const int MaxQuotesPerAuthor = 55;
    private const int RandomSeed = 20261101;

    private static readonly string[] Words =
    [
        "wisdom", "courage", "patience", "truth", "journey", "silence", "hope",
        "change", "growth", "failure", "success", "doubt", "clarity", "fear",
        "freedom", "purpose", "kindness", "chaos", "order", "time", "memory",
        "dream", "effort", "discipline", "curiosity", "gratitude", "loss",
        "beginning", "end", "light", "shadow", "balance", "trust", "choice",
        "action", "reflection", "solitude", "connection", "resilience", "peace"
    ];

    public static void Seed(AppDbContext dbContext)
    {
        if (dbContext.Authors.Any())
            return;

        var random = new Random(RandomSeed);

        for (var authorIndex = 1; authorIndex <= AuthorCount; authorIndex++)
        {
            var author = new Author { Name = $"Author {authorIndex:D3}" };

            var quoteCount = random.Next(MinQuotesPerAuthor, MaxQuotesPerAuthor + 1);

            for (var quoteIndex = 0; quoteIndex < quoteCount; quoteIndex++)
            {
                author.Quotes.Add(new Quote
                {
                    Text = GenerateQuoteText(random),
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            dbContext.Authors.Add(author);
        }

        dbContext.SaveChanges();
    }

    private static string GenerateQuoteText(Random random)
    {
        // Randomize length so seeded rows aren't uniform in size.
        var wordCount = random.Next(6, 41);

        var sentence = new List<string>(wordCount);
        for (var i = 0; i < wordCount; i++)
        {
            sentence.Add(Words[random.Next(Words.Length)]);
        }

        var text = string.Join(' ', sentence);
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }
}
