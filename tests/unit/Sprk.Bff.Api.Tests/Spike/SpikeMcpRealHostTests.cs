using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace Sprk.Bff.Api.Tests.Spike;

/// <summary>
/// SPIKE (spaarkeai-mcp-bff-api-r1 task 001, spike branch only): the real BFF host (real AuthorizationModule, real DI graph) maps both
/// spike routes and refuses an anonymous caller with 401. CustomWebAppFactory swaps the
/// authentication scheme for FakeAuthHandler, which sends no WWW-Authenticate header, so the header itself is asserted in
/// SpikeMcpProtocolTests (real JwtBearer) and again live in task 003. Also proves the host still starts with the
/// ModelContextProtocol packages (and their Microsoft.Extensions.AI.Abstractions dependency) loaded.
/// </summary>
public sealed class SpikeMcpRealHostTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    [Theory]
    [InlineData("/mcp/legal-operations")]
    [InlineData("/mcp/spike-area-b")]
    public async Task RealHost_AnonymousRequest_Gets401(string route)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
