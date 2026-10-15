// -----------------------------------------------------------------------------
// DataverseWebApiHealthProbeTests.cs — T228: H5's WhoAmI probe maps each status to the result H5 acts on, over real
// HTTP against a hand-written HttpMessageHandler (ADR-038), with a static test credential (no Entra).
//
//   200 → Reachable · 401 / 403 → AccessDenied (the Worker is not an application user — PRQ-C-09)
//   503 / 404 / 429 → InProgress (still being prepared) · 500 → Unreachable · transport fault → Unreachable
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiHealthProbeTests
{
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";

    [Theory]
    [InlineData(HttpStatusCode.OK, typeof(DataverseHealthProbeResult.Reachable))]
    [InlineData(HttpStatusCode.Unauthorized, typeof(DataverseHealthProbeResult.AccessDenied))]
    [InlineData(HttpStatusCode.Forbidden, typeof(DataverseHealthProbeResult.AccessDenied))]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(DataverseHealthProbeResult.InProgress))]
    [InlineData(HttpStatusCode.NotFound, typeof(DataverseHealthProbeResult.InProgress))]
    [InlineData((HttpStatusCode)429, typeof(DataverseHealthProbeResult.InProgress))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(DataverseHealthProbeResult.Unreachable))]
    public async Task EachStatus_MapsToTheResultH5ActsOn(HttpStatusCode status, Type expected)
    {
        var handler = new Responder(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });

        var result = await NewProbe(handler).CheckHealthAsync(EnvUrl, TenantId, CancellationToken.None);

        result.Should().BeOfType(expected);
        handler.Requests.Should().ContainSingle()
            .Which.Should().Be("https://spaarke-acme.crm.dynamics.com/api/data/v9.2/WhoAmI");
    }

    [Fact]
    public async Task ATransportFault_IsUnreachable()
    {
        var result = await NewProbe(new Responder(_ => throw new HttpRequestException("connection reset")))
            .CheckHealthAsync(EnvUrl, TenantId, CancellationToken.None);

        result.Should().BeOfType<DataverseHealthProbeResult.Unreachable>();
    }

    private static DataverseWebApiHealthProbe NewProbe(HttpMessageHandler handler)
        => new(
            new SingleClientFactory(handler),
            Options.Create(new DataverseEnvAdoptionOptions()),
            NullLogger<DataverseWebApiHealthProbe>.Instance,
            _ => new StaticCredential());

    private sealed class StaticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Responder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }
}
