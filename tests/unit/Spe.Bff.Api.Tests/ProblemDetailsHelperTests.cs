using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Graph;
using Spe.Bff.Api.Infrastructure.Errors;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class ProblemDetailsHelperTests
{
    private static async Task<(int status, string body)> ExecuteResultAsync(IResult result)
    {
        var ctx = new DefaultHttpContext();
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
        // Create ServiceException for Graph SDK v5 - message should contain Authorization_RequestDenied
        var ex = new ServiceException("Authorization_RequestDenied: Access denied")
        {
            ResponseStatusCode = 403
        };

        var result = ProblemDetailsHelper.FromGraphException(ex);
        var (status, json) = await ExecuteResultAsync(result);

        status.Should().Be(403);
        json.Should().Contain("Authorization_RequestDenied");
    }

    [Fact]
    public async Task Maps_Forbidden_Without_Code_As_UserDenied()
    {
        // Create ServiceException without Authorization_RequestDenied in message
        var ex = new ServiceException("Forbidden")
        {
            ResponseStatusCode = 403
        };

        var result = ProblemDetailsHelper.FromGraphException(ex);
        var (status, json) = await ExecuteResultAsync(result);

        status.Should().Be(403);
        json.Should().NotContain("Authorization_RequestDenied");
    }
}