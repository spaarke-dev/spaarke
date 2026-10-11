using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Task 035 review fix round (F2): <see cref="ExternalDataService.CreateServiceRequestAsync"/> reports a Dataverse that could
/// not take the create (transport failure, HttpClient timeout, 429, 5xx) as <see cref="ExternalDataService.DataverseUnavailableException"/>
/// so the route can answer a retryable 503; any other failure stays an <see cref="InvalidOperationException"/> (a 500), and a
/// caller who walked away still sees the cancellation.
/// </summary>
[Trait("category", "external-access")]
public class ExternalDataServiceServiceRequestCreateTests
{
    private static readonly Guid Contact = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    [Fact]
    public async Task TransportFailure_IsDataverseUnavailable()
    {
        var service = NewService(_ => throw new HttpRequestException("connection reset"));

        var act = () => service.CreateServiceRequestAsync(Contact, 100000000, "t", "{}");

        (await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>())
            .And.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task HttpClientTimeout_NotTheCallerCancelling_IsDataverseUnavailable()
    {
        var service = NewService(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var act = () => service.CreateServiceRequestAsync(Contact, 100000000, "t", "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();
    }

    [Fact]
    public async Task TheCallerCancelling_StaysACancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = NewService(_ => throw new TaskCanceledException("caller walked away"));

        var act = () => service.CreateServiceRequestAsync(Contact, 100000000, "t", "{}", cts.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>())
            .And.Should().NotBeOfType<ExternalDataService.DataverseUnavailableException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ThrottleOrServerError_IsDataverseUnavailable(HttpStatusCode status)
    {
        var service = NewService(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });

        var act = () => service.CreateServiceRequestAsync(Contact, 100000000, "t", "{}");

        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task OtherClientErrors_StayAPlainFailure(HttpStatusCode status)
    {
        var service = NewService(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });

        var act = () => service.CreateServiceRequestAsync(Contact, 100000000, "t", "{}");

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.And.Should().NotBeOfType<ExternalDataService.DataverseUnavailableException>();
    }

    [Fact]
    public async Task ASuccessfulCreate_ReturnsTheNewId()
    {
        var id = Guid.NewGuid();
        var service = NewService(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent($"{{\"sprk_servicerequestid\":\"{id:D}\"}}", Encoding.UTF8, "application/json"),
        });

        (await service.CreateServiceRequestAsync(Contact, 100000000, "t", "{}")).Should().Be(id);
    }

    private static ExternalDataService NewService(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build();
        return new ExternalDataService(
            new HttpClient(new StubHandler(respond)), config, new StubCredential(), NullLogger<ExternalDataService>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(new AccessToken("test-token", DateTimeOffset.MaxValue));
    }
}
