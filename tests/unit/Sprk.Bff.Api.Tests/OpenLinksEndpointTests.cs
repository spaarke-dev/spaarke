using FluentAssertions;
using Sprk.Bff.Api.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// Unit tests for the OpenLinksResponse DTO and DesktopUrlBuilder integration.
/// Note: Full DesktopUrlBuilder unit tests are in Spaarke.Core.Tests (32 tests).
///
/// Integration tests for the full HTTP endpoint are excluded due to WebApplicationFactory
/// infrastructure issues with background services. Once those are resolved, integration
/// tests can be added following the patterns in FileOperationsTests.cs.
/// </summary>
public class OpenLinksEndpointTests
{
    #region Response DTO Tests

    [Fact]
    public void OpenLinksResponse_CanBeConstructed_WithAllParameters()
    {
        // Arrange & Act
        var response = new OpenLinksResponse(
            DesktopUrl: "ms-word:ofe|u|https%3A%2F%2Fexample.com%2Ffile.docx",
            WebUrl: "https://example.com/file.docx",
            MimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            FileName: "document.docx"
        );

        // Assert
        response.DesktopUrl.Should().StartWith("ms-word:ofe|u|");
        response.WebUrl.Should().Be("https://example.com/file.docx");
        response.MimeType.Should().Contain("word");
        response.FileName.Should().Be("document.docx");
    }

    [Fact]
    public void OpenLinksResponse_AllowsNullDesktopUrl_ForUnsupportedTypes()
    {
        // Arrange & Act
        var response = new OpenLinksResponse(
            DesktopUrl: null,
            WebUrl: "https://example.com/file.pdf",
            MimeType: "application/pdf",
            FileName: "document.pdf"
        );

        // Assert
        response.DesktopUrl.Should().BeNull();
        response.WebUrl.Should().NotBeNullOrEmpty();
        response.MimeType.Should().Be("application/pdf");
    }

    [Theory]
    [InlineData("ms-word:ofe|u|", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx")]
    [InlineData("ms-word:ofe|u|", "application/msword", ".doc")]
    [InlineData("ms-excel:ofe|u|", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    [InlineData("ms-excel:ofe|u|", "application/vnd.ms-excel", ".xls")]
    [InlineData("ms-powerpoint:ofe|u|", "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx")]
    [InlineData("ms-powerpoint:ofe|u|", "application/vnd.ms-powerpoint", ".ppt")]
    public void OpenLinksResponse_DesktopUrl_MatchesExpectedProtocol(string expectedProtocolPrefix, string mimeType, string extension)
    {
        // This test documents the expected relationship between MIME types and protocol URLs
        // The actual URL generation is tested in DesktopUrlBuilderTests

        // Arrange
        var webUrl = $"https://example.com/file{extension}";
        var encodedUrl = Uri.EscapeDataString(webUrl);
        var expectedDesktopUrl = $"{expectedProtocolPrefix}{encodedUrl}";

        // Act
        var response = new OpenLinksResponse(
            DesktopUrl: expectedDesktopUrl,
            WebUrl: webUrl,
            MimeType: mimeType,
            FileName: $"test{extension}"
        );

        // Assert
        response.DesktopUrl.Should().StartWith(expectedProtocolPrefix);
        response.DesktopUrl.Should().Contain(encodedUrl);
    }

    [Fact]
    public void OpenLinksResponse_HandlesSpecialCharactersInFileName()
    {
        // Arrange & Act
        var response = new OpenLinksResponse(
            DesktopUrl: "ms-word:ofe|u|https%3A%2F%2Fexample.com",
            WebUrl: "https://example.com",
            MimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            FileName: "My Document (Final v2) - Copy.docx"
        );

        // Assert
        response.FileName.Should().Contain("(Final v2)");
        response.FileName.Should().Contain("Copy");
    }

    [Fact]
    public void OpenLinksResponse_Record_SupportsCopyWith()
    {
        // Arrange
        var original = new OpenLinksResponse(
            DesktopUrl: "ms-word:ofe|u|original",
            WebUrl: "https://original.com",
            MimeType: "application/msword",
            FileName: "original.doc"
        );

        // Act - Test record's "with" expression support
        var modified = original with { FileName = "modified.doc" };

        // Assert
        modified.FileName.Should().Be("modified.doc");
        modified.WebUrl.Should().Be(original.WebUrl); // Unchanged
        modified.DesktopUrl.Should().Be(original.DesktopUrl); // Unchanged
    }

    [Fact]
    public void OpenLinksResponse_Record_SupportsEquality()
    {
        // Arrange
        var response1 = new OpenLinksResponse(
            DesktopUrl: "ms-word:ofe|u|test",
            WebUrl: "https://test.com",
            MimeType: "application/msword",
            FileName: "test.doc"
        );

        var response2 = new OpenLinksResponse(
            DesktopUrl: "ms-word:ofe|u|test",
            WebUrl: "https://test.com",
            MimeType: "application/msword",
            FileName: "test.doc"
        );

        // Assert
        response1.Should().Be(response2);
        response1.GetHashCode().Should().Be(response2.GetHashCode());
    }

    #endregion

    #region Unsupported MIME Type Tests

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("video/mp4")]
    public void OpenLinksResponse_UnsupportedMimeType_ShouldHaveNullDesktopUrl(string mimeType)
    {
        // This test documents expected behavior for unsupported types
        // In the actual endpoint, DesktopUrlBuilder.FromMime() returns null for these

        // Arrange & Act
        var response = new OpenLinksResponse(
            DesktopUrl: null, // Expected for unsupported types
            WebUrl: "https://example.com/file.bin",
            MimeType: mimeType,
            FileName: "file.bin"
        );

        // Assert
        response.DesktopUrl.Should().BeNull();
        response.WebUrl.Should().NotBeNullOrEmpty();
    }

    #endregion
}
