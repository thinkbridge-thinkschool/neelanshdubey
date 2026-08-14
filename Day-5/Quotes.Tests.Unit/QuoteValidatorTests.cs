using FluentAssertions;
using QuotesApi.Services;

namespace Quotes.Tests.Unit;

public class QuoteValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_InvalidAuthor_ReturnsAuthorRequiredError(string? author)
    {
        // Arrange
        var validator = new QuoteValidator();

        // Act
        var errors = validator.Validate(author!, "A valid piece of quote text.");

        // Assert
        errors.Should().ContainKey("author");
        errors["author"].Should().Contain("Author is required.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_InvalidText_ReturnsTextRequiredError(string? text)
    {
        // Arrange
        var validator = new QuoteValidator();

        // Act
        var errors = validator.Validate("Marcus Aurelius", text!);

        // Assert
        errors.Should().ContainKey("text");
        errors["text"].Should().Contain("Text is required.");
    }

    [Fact]
    public void Validate_ValidAuthorAndText_ReturnsNoErrors()
    {
        // Arrange
        var validator = new QuoteValidator();

        // Act
        var errors = validator.Validate("Marcus Aurelius", "The impediment to action advances action.");

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyAuthorAndEmptyText_ReturnsBothErrors()
    {
        // Arrange
        var validator = new QuoteValidator();

        // Act
        var errors = validator.Validate(string.Empty, string.Empty);

        // Assert
        errors.Should().HaveCount(2);
        errors.Should().ContainKeys("author", "text");
    }

    [Fact]
    public void Validate_ValidAuthorAndInvalidText_ReturnsOnlyTextError()
    {
        // Arrange
        var validator = new QuoteValidator();

        // Act
        var errors = validator.Validate("Marcus Aurelius", string.Empty);

        // Assert
        errors.Should().HaveCount(1);
        errors.Should().ContainKey("text");
        errors.Should().NotContainKey("author");
    }
}
