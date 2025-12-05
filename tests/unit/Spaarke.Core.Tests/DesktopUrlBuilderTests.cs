using FluentAssertions;
using Spaarke.Core.Utilities;
using Xunit;

namespace Spaarke.Core.Tests;

public class DesktopUrlBuilderTests
{
    private const string TestWebUrl = "https://contoso.sharepoint.com/sites/test/documents/report.docx";
    private const string EncodedTestUrl = "https%3A%2F%2Fcontoso.sharepoint.com%2Fsites%2Ftest%2Fdocuments%2Freport.docx";

    #region Word MIME Type Tests

    [Fact]
    public void FromMime_WordOpenXml_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-word:ofe|u|{EncodedTestUrl}");
    }

    [Fact]
    public void FromMime_WordLegacy_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/msword";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-word:ofe|u|{EncodedTestUrl}");
    }

    #endregion

    #region Excel MIME Type Tests

    [Fact]
    public void FromMime_ExcelOpenXml_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-excel:ofe|u|{EncodedTestUrl}");
    }

    [Fact]
    public void FromMime_ExcelLegacy_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/vnd.ms-excel";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-excel:ofe|u|{EncodedTestUrl}");
    }

    #endregion

    #region PowerPoint MIME Type Tests

    [Fact]
    public void FromMime_PowerPointOpenXml_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-powerpoint:ofe|u|{EncodedTestUrl}");
    }

    [Fact]
    public void FromMime_PowerPointLegacy_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/vnd.ms-powerpoint";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-powerpoint:ofe|u|{EncodedTestUrl}");
    }

    #endregion

    #region Unsupported MIME Type Tests

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("video/mp4")]
    public void FromMime_UnsupportedMimeType_ReturnsNull(string mimeType)
    {
        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Null/Empty Input Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromMime_NullOrEmptyWebUrl_ReturnsNull(string? webUrl)
    {
        // Arrange
        const string mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        // Act
        var result = DesktopUrlBuilder.FromMime(webUrl, mimeType);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromMime_NullOrEmptyMimeType_ReturnsNull(string? mimeType)
    {
        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void FromMime_BothInputsNull_ReturnsNull()
    {
        // Act
        var result = DesktopUrlBuilder.FromMime(null, null);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region URL Encoding Tests

    [Fact]
    public void FromMime_UrlWithSpecialCharacters_EncodesCorrectly()
    {
        // Arrange
        const string urlWithSpaces = "https://contoso.sharepoint.com/sites/test/My Documents/report.docx";
        const string mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        // Act
        var result = DesktopUrlBuilder.FromMime(urlWithSpaces, mimeType);

        // Assert
        result.Should().NotBeNull();
        result.Should().StartWith("ms-word:ofe|u|");
        result.Should().Contain("My%20Documents"); // Space should be encoded
    }

    [Fact]
    public void FromMime_UrlWithQueryString_EncodesCorrectly()
    {
        // Arrange
        const string urlWithQuery = "https://contoso.sharepoint.com/file.docx?param=value&other=123";
        const string mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        // Act
        var result = DesktopUrlBuilder.FromMime(urlWithQuery, mimeType);

        // Assert
        result.Should().NotBeNull();
        result.Should().StartWith("ms-word:ofe|u|");
        result.Should().Contain("%3F"); // ? should be encoded
        result.Should().Contain("%26"); // & should be encoded
    }

    #endregion

    #region Case Insensitivity Tests

    [Theory]
    [InlineData("APPLICATION/VND.OPENXMLFORMATS-OFFICEDOCUMENT.WORDPROCESSINGML.DOCUMENT")]
    [InlineData("Application/Vnd.Openxmlformats-Officedocument.Wordprocessingml.Document")]
    public void FromMime_MimeTypeCaseInsensitive_ReturnsCorrectProtocolUrl(string mimeType)
    {
        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-word:ofe|u|{EncodedTestUrl}");
    }

    #endregion

    #region IsSupported Tests

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("application/msword", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", true)]
    [InlineData("application/vnd.ms-excel", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", true)]
    [InlineData("application/vnd.ms-powerpoint", true)]
    [InlineData("application/pdf", false)]
    [InlineData("image/png", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsSupported_ReturnsExpectedResult(string? mimeType, bool expected)
    {
        // Act
        var result = DesktopUrlBuilder.IsSupported(mimeType);

        // Assert
        result.Should().Be(expected);
    }

    #endregion
}
