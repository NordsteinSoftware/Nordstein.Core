using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Tests;

/// <summary>
/// Covers <see cref="Content"/> construction via the two factories, the <see cref="Content.Kind"/>
/// derivation, the validation branches (empty text, image without a media type), ToString rendering,
/// and equality/hash edge cases not already covered by the serialization round-trip tests.
/// </summary>
[TestClass]
public sealed class ContentTests
{
    [TestMethod]
    public void FromText_SetsTextAndTextKind()
    {
        var content = Content.FromText("hello");

        content.Kind.Should().Be(ContentKind.Text);
        content.Text.Should().Be("hello");
        content.Data.Should().BeNull();
    }

    [TestMethod]
    public void FromImage_SetsDataAndImageKind()
    {
        var content = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));

        content.Kind.Should().Be(ContentKind.Image);
        content.Text.Should().BeNull();
        content.Data?.MediaType.Should().Be("image/png");
        content.Data?.ToArray().Should().Equal([1, 2, 3]);
    }

    [TestMethod]
    public void Validate_TextContent_HasNoFailures()
    {
        var content = Content.FromText("hello");

        Failures(content).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_EmptyText_ReportsMustHaveTextOrData()
    {
        var content = Content.FromText("");

        Failures(content).Should().Contain(result =>
            result.ErrorMessage != null && result.ErrorMessage.Contains("must have either text or data"));
    }

    [TestMethod]
    public void Validate_WhitespaceText_ReportsMustHaveTextOrData()
    {
        var content = Content.FromText("   ");

        Failures(content).Should().Contain(result =>
            result.ErrorMessage != null && result.ErrorMessage.Contains("must have either text or data"));
    }

    [TestMethod]
    public void Validate_ImageContent_HasNoFailures()
    {
        var content = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));

        Failures(content).Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_ImageWithoutMediaType_ReportsFailure()
    {
        // BinaryData.FromBytes without a media type yields a null MediaType, which image content
        // validation rejects.
        var content = Content.FromImage(BinaryData.FromBytes([1, 2, 3]));

        Failures(content).Should().NotBeEmpty();
    }

    [TestMethod]
    public void ToString_TextContent_ReturnsText()
    {
        Content.FromText("hello").ToString().Should().Be("hello");
    }

    [TestMethod]
    public void ToString_ImageContent_DescribesMediaTypeAndSize()
    {
        var content = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));

        content.ToString().Should().Be("Image: MediaType='image/png', Size=3 bytes");
    }

    [TestMethod]
    public void Equals_SameText_AreEqual()
    {
        var a = Content.FromText("hello");
        var b = Content.FromText("hello");

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentText_AreNotEqual()
    {
        Content.FromText("hello").Equals(Content.FromText("world")).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_TextVsImage_AreNotEqual()
    {
        var text = Content.FromText("hello");
        var image = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));

        text.Equals(image).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_SameImageBytesAndMediaType_AreEqual()
    {
        var a = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));
        var b = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentImageBytes_AreNotEqual()
    {
        var a = Content.FromImage(BinaryData.FromBytes([1, 2, 3], "image/png"));
        var b = Content.FromImage(BinaryData.FromBytes([9, 9, 9], "image/png"));

        a.Equals(b).Should().BeFalse();
    }

    [TestMethod]
    public void Equals_Null_IsFalse()
    {
        Content.FromText("hello").Equals(null).Should().BeFalse();
    }

    private static IReadOnlyList<ValidationResult> Failures(Content content)
        => content
            .Validate(new ValidationContext(content))
            .Where(result => result != ValidationResult.Success)
            .ToList();
}
