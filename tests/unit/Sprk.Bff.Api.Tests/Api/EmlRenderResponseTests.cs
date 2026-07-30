using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using MimeKit;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

/// <summary>
/// Behavior tests for the HTTP-shaping of the eml-render endpoint
/// (<see cref="FileAccessEndpoints.BuildEmlRenderResponseAsync"/>) — task 010 / FR-07 / NFR-01 / NFR-03.
///
/// The document-resolution + fail-closed authorization legs of the endpoint are covered structurally:
/// unauthorized callers are rejected by <c>DocumentAuthorizationFilter</c> and unauthenticated callers by the
/// group's <c>RequireAuthorization()</c> BEFORE the handler runs (see EndpointGroupingTests), and a missing
/// document resolves to 404 before any HTML is produced. This class covers the response-builder behavior that
/// does NOT require mocking SpeFileStore/Graph: (1) a missing .eml (null stream) yields 404 with NO HTML body,
/// and (2) a valid .eml yields 200 + sanitized HTML + the long-lived immutable cache header (NFR-01).
/// </summary>
public class EmlRenderResponseTests
{
    private readonly EmlToHtmlRenderer _renderer = new();

    private static Stream BuildEmlStream(string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alice", "alice@example.com"));
        message.To.Add(new MailboxAddress("Bob", "bob@example.com"));
        message.Subject = "Test";
        message.Body = new TextPart("html") { Text = htmlBody };

        var ms = new MemoryStream();
        message.WriteTo(ms);
        ms.Position = 0;
        return ms;
    }

    private static async Task<(int status, string? cacheControl, string? contentType, string body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        await result.ExecuteAsync(context);

        bodyStream.Position = 0;
        var body = await new StreamReader(bodyStream, Encoding.UTF8).ReadToEndAsync();
        return (
            context.Response.StatusCode,
            context.Response.Headers.CacheControl.ToString(),
            context.Response.ContentType,
            body);
    }

    [Fact]
    public async Task BuildEmlRenderResponseAsync_WithNullStream_Throws404AndLeaksNoHtml()
    {
        // Arrange — a document with no .eml in SPE (SpeFileStore.DownloadFileAsync returned null)
        Stream? nullStream = null;

        // Act
        var act = () => FileAccessEndpoints.BuildEmlRenderResponseAsync(
            nullStream, _renderer, Guid.NewGuid().ToString(), CancellationToken.None);

        // Assert — 404 (client degrades to sprk_body), and NO HTML body is produced (fail-closed on missing file)
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("file_not_found");
    }

    [Fact]
    public async Task BuildEmlRenderResponseAsync_WithValidEml_Returns200SanitizedHtml()
    {
        // Arrange
        using var stream = BuildEmlStream("<div>Hello<script>alert(1)</script> body</div>");

        // Act
        var result = await FileAccessEndpoints.BuildEmlRenderResponseAsync(
            stream, _renderer, Guid.NewGuid().ToString(), CancellationToken.None);
        var (status, _, contentType, body) = await ExecuteAsync(result);

        // Assert — 200, text/html, sanitized (no script), content preserved
        status.Should().Be(StatusCodes.Status200OK);
        contentType.Should().StartWith("text/html");
        body.Should().Contain("Hello");
        body.Should().NotContain("<script");
    }

    [Fact]
    public async Task BuildEmlRenderResponseAsync_WithValidEml_SetsImmutableLongLivedCacheHeader()
    {
        // Arrange
        using var stream = BuildEmlStream("<div>Immutable render</div>");

        // Act
        var result = await FileAccessEndpoints.BuildEmlRenderResponseAsync(
            stream, _renderer, Guid.NewGuid().ToString(), CancellationToken.None);
        var (_, cacheControl, _, _) = await ExecuteAsync(result);

        // Assert — the archived .eml is immutable, so the response is long-lived + immutable-cacheable (NFR-01)
        cacheControl.Should().Contain("public");
        cacheControl.Should().Contain("immutable");
        cacheControl.Should().Contain("max-age=31536000");
    }
}
