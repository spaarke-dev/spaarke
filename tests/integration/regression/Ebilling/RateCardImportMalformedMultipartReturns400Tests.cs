using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// Regression for the ebilling-r1 task 078 review item K1: <c>POST /api/finance/ratecards/import</c> let the multipart parser's
/// <see cref="InvalidDataException"/> (or a <see cref="BadHttpRequestException"/>) from <c>ReadFormAsync</c> surface as a 500. A malformed
/// body is a client error: 400 ProblemDetails, as <c>ChatDocumentEndpoints</c> answers (<see cref="MalformedMultipartUploadReturns400Tests"/>).
/// </summary>
/// <remarks>Uses the real <c>MapRateCardImportEndpoints</c> host of <see cref="RateCardImportEndpointsTests"/>; the service is a strict mock, so reaching it fails the test.</remarks>
public sealed class RateCardImportMalformedMultipartReturns400Tests
{
    private static readonly string ImportUrl = $"{RateCardImportEndpointsTests.Import}?firmId={RateCardImportEndpointsTests.FirmId}&name=Card";

    private static HttpContent SectionWithoutContentDisposition()
    {
        const string boundary = "regression-boundary";
        var raw = $"--{boundary}\r\nContent-Type: text/plain\r\n\r\nno disposition here\r\n--{boundary}--\r\n";
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(raw));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return content;
    }

    [Theory]
    [InlineData("empty-multipart")]
    [InlineData("section-without-content-disposition")]
    public async Task Import_AMalformedMultipartBody_Returns400ProblemDetails_NotA500(string body)
    {
        await using var host = await RateCardImportEndpointsTests.Host.StartAsync();
        using var content = body == "empty-multipart" ? new MultipartFormDataContent() : SectionWithoutContentDisposition();

        using var response = await host.Client.SendAsync(RateCardImportEndpointsTests.Req("POST", ImportUrl, content: content));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        text.Should().Contain("ratecard.import_invalid");
        text.Should().NotContain(nameof(InvalidDataException), "the parser's exception type must not reach the client");
        text.Should().NotContain("Content-Disposition", "the parser's message must not reach the client");
        text.Should().NotContain("   at ", "no stack frame may reach the client");
    }
}
