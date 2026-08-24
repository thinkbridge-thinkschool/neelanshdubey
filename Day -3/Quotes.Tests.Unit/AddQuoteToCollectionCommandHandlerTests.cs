using FluentAssertions;
using NSubstitute;
using QuotesApi.Commands;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace Quotes.Tests.Unit;

public class AddQuoteToCollectionCommandHandlerTests
{
    private static IClock FixedClock(DateTimeOffset now)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_AppendsItemAndPersists()
    {
        // Arrange
        var collection = new Collection("Stoic Favorites", ownerId: 1);
        var repository = Substitute.For<ICollectionRepository>();
        repository.GetByIdAsync(collection.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Collection?>(collection));

        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var handler = new AddQuoteToCollectionCommandHandler(repository, FixedClock(now));

        // Act
        var result = await handler.HandleAsync(
            new AddQuoteToCollectionCommand(collection.Id, QuoteId: 101),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle();
        result.Items[0].QuoteId.Should().Be(101);
        result.Items[0].AddedAt.Should().Be(now);

        await repository.Received(1).UpdateAsync(collection, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CollectionDoesNotExist_ReturnsNullWithoutCallingUpdate()
    {
        // Arrange
        var repository = Substitute.For<ICollectionRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Collection?>(null));

        var handler = new AddQuoteToCollectionCommandHandler(repository, FixedClock(DateTimeOffset.UtcNow));

        // Act
        var result = await handler.HandleAsync(
            new AddQuoteToCollectionCommand(Guid.NewGuid(), QuoteId: 101),
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
        await repository.DidNotReceive().UpdateAsync(Arg.Any<Collection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DuplicateQuoteId_ThrowsDomainExceptionAndDoesNotPersist()
    {
        // Arrange: the aggregate's own invariant (no duplicate QuoteIds) is
        // still enforced through the handler, not bypassed by it.
        var collection = new Collection("Stoic Favorites", ownerId: 1);
        var clock = FixedClock(DateTimeOffset.UtcNow);
        collection.AddItem(101, clock);

        var repository = Substitute.For<ICollectionRepository>();
        repository.GetByIdAsync(collection.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Collection?>(collection));

        var handler = new AddQuoteToCollectionCommandHandler(repository, clock);

        // Act
        var act = () => handler.HandleAsync(
            new AddQuoteToCollectionCommand(collection.Id, QuoteId: 101),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("This quote is already in the collection.");

        await repository.DidNotReceive().UpdateAsync(Arg.Any<Collection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_51stItem_ThrowsDomainExceptionAndDoesNotPersist()
    {
        // Arrange
        var collection = new Collection("Stoic Favorites", ownerId: 1);
        var clock = FixedClock(DateTimeOffset.UtcNow);

        for (var i = 0; i < 50; i++)
        {
            collection.AddItem(i, clock);
        }

        var repository = Substitute.For<ICollectionRepository>();
        repository.GetByIdAsync(collection.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Collection?>(collection));

        var handler = new AddQuoteToCollectionCommandHandler(repository, clock);

        // Act
        var act = () => handler.HandleAsync(
            new AddQuoteToCollectionCommand(collection.Id, QuoteId: 999),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("A collection cannot contain more than 50 items.");

        await repository.DidNotReceive().UpdateAsync(Arg.Any<Collection>(), Arg.Any<CancellationToken>());
    }
}
