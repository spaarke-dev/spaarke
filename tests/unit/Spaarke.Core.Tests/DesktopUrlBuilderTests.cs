using FluentAssertions;
using Spaarke.Core.Utilities;
using Xunit;

namespace Spaarke.Core.Tests;

// NOTE (dotnet-10-upgrade-r1 task 030): DesktopUrlBuilder.FromMime intentionally emits the ABBREVIATED
// Office protocol format `ms-{app}:{webUrl}` (raw URL, NO `ofe|u|` prefix, NO URL-encoding) to bypass
// Windows Security Zone / Restricted-Sites blocking of SPE /contentstorage/ URLs — see the production
// XML doc on DesktopUrlBuilder.FromMime. These expectations were updated from the legacy `ofe|u|{encoded}`
// full-format (introduced by the FileViewer Enhancements project, commit bb63d9818, without updating this
// test — the tests were red on master/net8 before the .NET 10 retarget; this is a stale-test correction,
// NOT a net10 behavior change). "Code wins; the test lagged."
public class DesktopUrlBuilderTests
{
    private const string TestWebUrl = "https://contoso.sharepoint.com/sites/test/documents/report.docx";

    #region Word MIME Type Tests

    [Fact]
    public void FromMime_WordOpenXml_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-word:{TestWebUrl}");
    }

    [Fact]
    public void FromMime_WordLegacy_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/msword";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-word:{TestWebUrl}");
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
        result.Should().Be($"ms-excel:{TestWebUrl}");
    }

    [Fact]
    public void FromMime_ExcelLegacy_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/vnd.ms-excel";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-excel:{TestWebUrl}");
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
        result.Should().Be($"ms-powerpoint:{TestWebUrl}");
    }

    [Fact]
    public void FromMime_PowerPointLegacy_ReturnsCorrectProtocolUrl()
    {
        // Arrange
        const string mimeType = "application/vnd.ms-powerpoint";

        // Act
        var result = DesktopUrlBuilder.FromMime(TestWebUrl, mimeType);

        // Assert
        result.Should().Be($"ms-powerpoint:{TestWebUrl}");
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

    #region URL Pass-Through Tests (abbreviated format — see class note)

    [Fact]
    public void FromMime_UrlWithSpecialCharacters_PassesThroughUnencoded()
    {
        // Arrange
        const string urlWithSpaces = "https://contoso.sharepoint.com/sites/test/My Documents/report.docx";
        const string mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        // Act
        var result = DesktopUrlBuilder.FromMime(urlWithSpaces, mimeType);

        // Assert — abbreviated format passes the web URL through verbatim (no ofe|u|, no URL-encoding)
        result.Should().Be($"ms-word:{urlWithSpaces}");
    }

    [Fact]
    public void FromMime_UrlWithQueryString_PassesThroughUnencoded()
    {
        // Arrange
        const string urlWithQuery = "https://contoso.sharepoint.com/file.docx?param=value&other=123";
        const string mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        // Act
        var result = DesktopUrlBuilder.FromMime(urlWithQuery, mimeType);

        // Assert — abbreviated format passes the raw URL (incl. query string) through unchanged
        result.Should().Be($"ms-word:{urlWithQuery}");
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
        result.Should().Be($"ms-word:{TestWebUrl}");
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
