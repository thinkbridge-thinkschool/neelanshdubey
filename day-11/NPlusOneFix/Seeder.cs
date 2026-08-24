namespace NPlusOneFix;

public static class Seeder
{
    private const int AuthorCount = 500;
    private const int MinBooksPerAuthor = 15;
    private const int MaxBooksPerAuthor = 25;

    private static readonly string[] FirstNames =
    {
        "James", "Mary", "Robert", "Patricia", "John", "Jennifer", "Michael", "Linda",
        "William", "Elizabeth", "David", "Barbara", "Richard", "Susan", "Joseph", "Jessica",
        "Thomas", "Sarah", "Charles", "Karen", "Christopher", "Nancy", "Daniel", "Margaret",
        "Matthew", "Lisa", "Anthony", "Betty", "Mark", "Sandra", "Donald", "Ashley",
        "Steven", "Kimberly", "Paul", "Emily", "Andrew", "Donna", "Joshua", "Michelle",
        "Kenneth", "Carol", "Kevin", "Amanda", "Brian", "Melissa", "George", "Deborah",
        "Edward", "Stephanie"
    };

    private static readonly string[] LastNames =
    {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
        "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson",
        "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez", "Thompson",
        "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson", "Walker",
        "Young", "Allen", "King", "Wright", "Scott", "Torres", "Nguyen", "Hill", "Flores",
        "Green", "Adams", "Nelson", "Baker", "Hall", "Rivera", "Campbell", "Mitchell",
        "Carter", "Roberts"
    };

    private static readonly string[] TitleAdjectives =
    {
        "Silent", "Hidden", "Last", "Broken", "Golden", "Forgotten", "Distant", "Crimson",
        "Endless", "Quiet", "Frozen", "Sacred", "Lost", "Burning", "Ancient", "Restless",
        "Shattered", "Whispering", "Fading", "Wandering"
    };

    private static readonly string[] TitleNouns =
    {
        "River", "Kingdom", "Shadow", "Garden", "Storm", "Journey", "Mirror", "Harbor",
        "Empire", "Promise", "Horizon", "Labyrinth", "Orchard", "Tide", "Summit", "Echo",
        "Path", "Flame", "Legacy", "Voyage"
    };

    public static void Seed(AppDbContext context)
    {
        if (context.Authors.Any())
        {
            Console.WriteLine("Database already seeded, skipping.");
            return;
        }

        var random = new Random(42);
        var authors = new List<Author>(AuthorCount);
        var totalBooks = 0;

        for (var i = 0; i < AuthorCount; i++)
        {
            var name = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}";
            var author = new Author { Name = name };

            var bookCount = random.Next(MinBooksPerAuthor, MaxBooksPerAuthor + 1);
            for (var j = 0; j < bookCount; j++)
            {
                var title = $"The {TitleAdjectives[random.Next(TitleAdjectives.Length)]} " +
                            $"{TitleNouns[random.Next(TitleNouns.Length)]}";
                author.Books.Add(new Book
                {
                    Title = title,
                    PublishedYear = random.Next(1950, 2025)
                });
            }

            totalBooks += bookCount;
            authors.Add(author);
        }

        context.Authors.AddRange(authors);
        context.SaveChanges();

        Console.WriteLine($"Seeded {authors.Count} authors and {totalBooks} books.");
    }
}
