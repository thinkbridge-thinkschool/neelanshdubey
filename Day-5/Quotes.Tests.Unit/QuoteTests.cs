using FluentAssertions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class QuoteTests
{
    [Fact]
    public void Create_ValidAuthorAndText_ReturnsQuoteWithExpectedValues()
    {
        // Arrange
        var author = "Marcus Aurelius";
        var text = "The impediment to action advances action.";
        var ownerId = 42;

        // Act
        var quote = Quote.Create(author, text, ownerId);

        // Assert
        quote.Author.Should().Be(author);
        quote.Text.Should().Be(text);
        quote.OwnerId.Should().Be(ownerId);
        quote.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_ValidAuthorAndText_SetsCreatedAtToUtcNow()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var quote = Quote.Create("Seneca", "Luck is what happens when preparation meets opportunity.", 1);

        // Assert
        var after = DateTimeOffset.UtcNow;
        quote.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void Create_AuthorAndTextWithSurroundingWhitespace_TrimsValues()
    {
        // Arrange
        var author = "  Epictetus  ";
        var text = "  It's not what happens to you, but how you react.  ";

        // Act
        var quote = Quote.Create(author, text, 1);

        // Assert
        quote.Author.Should().Be("Epictetus");
        quote.Text.Should().Be("It's not what happens to you, but how you react.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_InvalidAuthor_ThrowsDomainException(string? author)
    {
        // Arrange
        // Act
        var act = () => Quote.Create(author!, "A valid piece of quote text.", 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Author is required.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_InvalidText_ThrowsDomainException(string? text)
    {
        // Arrange
        // Act
        var act = () => Quote.Create("Marcus Aurelius", text!, 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Text is required.");
    }

    [Fact]
    public void Create_AuthorLongerThan200Characters_ThrowsDomainException()
    {
        // Arrange
        var author = new string('A', 201);

        // Act
        var act = () => Quote.Create(author, "A valid piece of quote text.", 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Author must be 200 characters or fewer.");
    }

    [Fact]
    public void Create_AuthorExactly200Characters_DoesNotThrow()
    {
        // Arrange
        var author = new string('A', 200);

        // Act
        var act = () => Quote.Create(author, "A valid piece of quote text.", 1);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_TextLongerThan1000Characters_ThrowsDomainException()
    {
        // Arrange
        var text = new string('B', 1001);

        // Act
        var act = () => Quote.Create("Marcus Aurelius", text, 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Text must be 1000 characters or fewer.");
    }

    [Fact]
    public void Create_TextExactly1000Characters_DoesNotThrow()
    {
        // Arrange
        var text = new string('B', 1000);

        // Act
        var act = () => Quote.Create("Marcus Aurelius", text, 1);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Update_ValidAuthorAndText_UpdatesAuthorAndText()
    {
        // Arrange
        var quote = Quote.Create("Marcus Aurelius", "Original text.", 1);

        // Act
        quote.Update("Seneca", "Updated text.");

        // Assert
        quote.Author.Should().Be("Seneca");
        quote.Text.Should().Be("Updated text.");
    }

    [Fact]
    public void Update_InvalidAuthor_ThrowsDomainExceptionAndLeavesQuoteUnchanged()
    {
        // Arrange
        var quote = Quote.Create("Marcus Aurelius", "Original text.", 1);

        // Act
        var act = () => quote.Update("", "New text.");

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Author is required.");
        quote.Author.Should().Be("Marcus Aurelius");
        quote.Text.Should().Be("Original text.");
    }

    [Fact]
    public void SoftDelete_MarksQuoteAsDeleted()
    {
        // Arrange
        var quote = Quote.Create("Marcus Aurelius", "Original text.", 1);

        // Act
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }
}
