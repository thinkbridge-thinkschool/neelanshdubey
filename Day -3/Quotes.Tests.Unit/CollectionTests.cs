using FluentAssertions;
using NSubstitute;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Quotes.Tests.Unit;

public class CollectionTests
{
    private static IClock FixedClock(DateTimeOffset now)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    [Fact]
    public void Constructor_ValidNameAndOwnerId_ReturnsCollectionWithExpectedValues()
    {
        // Arrange
        var name = "Stoic Favorites";
        var ownerId = 42;

        // Act
        var collection = new Collection(name, ownerId);

        // Assert
        collection.Name.Should().Be(name);
        collection.OwnerId.Should().Be(ownerId);
        collection.Id.Should().NotBe(Guid.Empty);
        collection.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidName_ThrowsDomainException(string? name)
    {
        // Arrange
        // Act
        var act = () => new Collection(name!, 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Name is required.");
    }

    [Fact]
    public void Constructor_NameShorterThan3Characters_ThrowsDomainException()
    {
        // Arrange
        // Act
        var act = () => new Collection("ab", 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Name must be at least 3 characters.");
    }

    [Fact]
    public void Constructor_NameExactly3Characters_DoesNotThrow()
    {
        // Arrange
        // Act
        var act = () => new Collection("abc", 1);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NameLongerThan80Characters_ThrowsDomainException()
    {
        // Arrange
        var name = new string('A', 81);

        // Act
        var act = () => new Collection(name, 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Name must be 80 characters or fewer.");
    }

    [Fact]
    public void Constructor_NameExactly80Characters_DoesNotThrow()
    {
        // Arrange
        var name = new string('A', 80);

        // Act
        var act = () => new Collection(name, 1);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AddItem_ValidQuoteId_AppendsItemWithClockTimestamp()
    {
        // Arrange
        var collection = new Collection("My Collection", 1);
        var quoteId = 101;
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = FixedClock(now);

        // Act
        collection.AddItem(quoteId, clock);

        // Assert
        collection.Items.Should().ContainSingle();
        collection.Items[0].QuoteId.Should().Be(quoteId);
        collection.Items[0].AddedAt.Should().Be(now);
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("My Collection", 1);
        var quoteId = 101;
        var clock = FixedClock(DateTimeOffset.UtcNow);
        collection.AddItem(quoteId, clock);

        // Act
        var act = () => collection.AddItem(quoteId, clock);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("This quote is already in the collection.");
        collection.Items.Should().ContainSingle();
    }

    [Fact]
    public void AddItem_51stItem_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("My Collection", 1);
        var clock = FixedClock(DateTimeOffset.UtcNow);

        for (var i = 0; i < 50; i++)
        {
            collection.AddItem(i, clock);
        }

        // Act
        var act = () => collection.AddItem(50, clock);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("A collection cannot contain more than 50 items.");
        collection.Items.Should().HaveCount(50);
    }

    [Fact]
    public void RemoveItem_ExistingQuoteId_RemovesMatchingItem()
    {
        // Arrange
        var collection = new Collection("My Collection", 1);
        var quoteId = 101;
        var clock = FixedClock(DateTimeOffset.UtcNow);
        collection.AddItem(quoteId, clock);

        // Act
        collection.RemoveItem(quoteId);

        // Assert
        collection.Items.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_QuoteIdNotPresent_ThrowsDomainException()
    {
        // Arrange
        var collection = new Collection("My Collection", 1);

        // Act
        var act = () => collection.RemoveItem(999);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("This quote is not in the collection.");
    }
}
