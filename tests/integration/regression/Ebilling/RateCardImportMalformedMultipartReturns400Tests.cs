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

    // ISS-043: the URL above names no procedureVersionId, so the route answers its query 400 before it reads the body; the tests below
    // use a valid query so the body is what is refused.
    private static readonly string ValidImportUrl = $"{RateCardImportEndpointsTests.Import}?procedureVersionId={RateCardImportEndpointsTests.VersionId}&name=Card";

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

    [Fact(DisplayName = "ISS-043 must-fire: a multipart body with no closing boundary is a 400 ratecard.import_invalid, not a 500")]
    public async Task Import_ABodyWithNoClosingBoundary_Returns400ProblemDetails_NotA500()
    {
        await using var host = await RateCardImportEndpointsTests.Host.StartAsync();
        using var content = MalformedMultipartBodies.NoClosingBoundary(fileName: "rates.xlsx");

        using var response = await host.Client.SendAsync(RateCardImportEndpointsTests.Req("POST", ValidImportUrl, content: content));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        text.Should().Contain("ratecard.import_invalid");
        text.Should().NotContain("IOException").And.NotContain("   at ");
    }

    [Fact(DisplayName = "ISS-043 must-fire: a body stream that fails with an IOException (token not cancelled) is a 400")]
    public async Task Import_ABodyStreamThatFails_Returns400()
    {
        await using var host = await RateCardImportEndpointsTests.Host.StartAsync();
        host.OnRequest = MalformedMultipartBodies.Apply(abandoned: false);
        using var content = new MultipartFormDataContent();

        using var response = await host.Client.SendAsync(RateCardImportEndpointsTests.Req("POST", ValidImportUrl, content: content));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "ISS-043 must-not-fire: a body that fails because the client abandoned the request (token cancelled) is not answered as a 400")]
    public async Task Import_AnAbandonedRequest_IsNotA400()
    {
        await using var host = await RateCardImportEndpointsTests.Host.StartAsync();
        host.OnRequest = MalformedMultipartBodies.Apply(abandoned: true);
        using var content = new MultipartFormDataContent();

        var act = async () => await host.Client.SendAsync(RateCardImportEndpointsTests.Req("POST", ValidImportUrl, content: content));

        (await act.Should().ThrowAsync<Exception>()).Which.Should().Match(e => e is IOException || e is OperationCanceledException);
    }
}
