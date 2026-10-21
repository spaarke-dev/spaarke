using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Errors;
using Xunit;

namespace Sprk.Bff.Api.Tests;

public class ProblemDetailsHelperTests
{
    private static async Task<(int status, string body)> ExecuteResultAsync(IResult result)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        var ms = new MemoryStream();
        ctx.Response.Body = ms;
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        return (ctx.Response.StatusCode, json);
    }

    [Fact]
    public async Task Maps_Authorization_RequestDenied_As_AppConfigIssue()
    {
        // Create ODataError for Graph SDK v5 - message should contain Authorization_RequestDenied
        var ex = new ODataError
        {
            Error = new MainError
            {
                Code = "Authorization_RequestDenied",
                Message = "Access denied"
            },
            ResponseStatusCode = 403
        };

        var result = ProblemDetailsHelper.FromGraphException(ex.ResponseStatusCode, ex.Error?.Code, ex.Error?.Message);
        var (status, json) = await ExecuteResultAsync(result);

        status.Should().Be(403);
        json.Should().Contain("Authorization_RequestDenied");
    }

    [Fact]
    public async Task Maps_Forbidden_Without_Code_As_UserDenied()
    {
        // Create ODataError without Authorization_RequestDenied in message
        var ex = new ODataError
        {
            Error = new MainError
            {
                Code = "accessDenied",
                Message = "Forbidden"
            },
            ResponseStatusCode = 403
        };

        var result = ProblemDetailsHelper.FromGraphException(ex.ResponseStatusCode, ex.Error?.Code, ex.Error?.Message);
        var (status, json) = await ExecuteResultAsync(result);

        status.Should().Be(403);
        json.Should().NotContain("Authorization_RequestDenied");
    }

    [Theory]
    [InlineData(null, "traceId")]
    [InlineData("correlationId", "correlationId")]
    public async Task RecordOwnerUnsettled_CarriesTheIdUnderTheNamedExtension_TraceIdByDefault(
        string? extension, string expectedKey)
    {
        // #2143 round 3 (K3): every route but Finance keeps traceId; Finance names correlationId, as its other problems do.
        var result = extension is null
            ? ProblemDetailsHelper.RecordOwnerUnsettled("document", "id-1")
            : ProblemDetailsHelper.RecordOwnerUnsettled("document", "id-1", traceIdExtension: extension);
        var (status, json) = await ExecuteResultAsync(result);

        status.Should().Be(409);
        using var body = System.Text.Json.JsonDocument.Parse(json);
        body.RootElement.GetProperty(expectedKey).GetString().Should().Be("id-1");
        var otherKey = expectedKey == "traceId" ? "correlationId" : "traceId";
        body.RootElement.TryGetProperty(otherKey, out _).Should().BeFalse();
    }
}
