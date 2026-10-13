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
    private static readonly Guid Team = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    [Fact]
    public async Task TransportFailure_IsDataverseUnavailable()
    {
        var service = NewService(_ => throw new HttpRequestException("connection reset"));

        var act = () => service.CreateServiceRequestAsync(Contact, Team, 100000000, "t", "{}");

        (await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>())
            .And.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task HttpClientTimeout_NotTheCallerCancelling_IsDataverseUnavailable()
    {
        var service = NewService(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var act = () => service.CreateServiceRequestAsync(Contact, Team, 100000000, "t", "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();
    }

    [Fact]
    public async Task TheCallerCancelling_StaysACancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = NewService(_ => throw new TaskCanceledException("caller walked away"));

        var act = () => service.CreateServiceRequestAsync(Contact, Team, 100000000, "t", "{}", cts.Token);

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

        var act = () => service.CreateServiceRequestAsync(Contact, Team, 100000000, "t", "{}");

        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task OtherClientErrors_StayAPlainFailure(HttpStatusCode status)
    {
        var service = NewService(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });

        var act = () => service.CreateServiceRequestAsync(Contact, Team, 100000000, "t", "{}");

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

        (await service.CreateServiceRequestAsync(Contact, Team, 100000000, "t", "{}")).Should().Be(id);
    }

    // ---------------------------------------------------------------------------------------------
    // Owner decision 2026-10-11 (I-6): the payload binds the owning team, and an empty team refuses
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Payload_CarriesTheOwnerBind_AlongsideTheFiveIntakeColumns()
    {
        var payload = ExternalDataService.BuildServiceRequestCreatePayload(Contact, Team, 100000001, "Idea", "{\"a\":1}");

        payload.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_name"] = "Idea",
            ["sprk_requesttype"] = 100000001,
            ["sprk_intakedata"] = "{\"a\":1}",
            ["sprk_direction"] = ExternalDataService.ServiceRequestDirectionInbound,
            [ExternalDataService.RequestedByBindKey] = $"/contacts({Contact:D})",
            [ExternalDataService.OwnerBindKey] = $"/teams({Team})",
        });
    }

    [Fact]
    public void Payload_WithNoOwningTeam_Refuses_NeverBuildsAnApplicationOwnedRow()
    {
        var act = () => ExternalDataService.BuildServiceRequestCreatePayload(Contact, Guid.Empty, 100000000, "t", "{}");

        act.Should().Throw<InvalidOperationException>().WithMessage("*refusing to create it app-owned*");
    }

    [Fact]
    public async Task Create_WithNoOwningTeam_Refuses_AndNothingIsSent()
    {
        var sent = 0;
        var service = NewService(_ =>
        {
            sent++;
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        var act = () => service.CreateServiceRequestAsync(Contact, Guid.Empty, 100000000, "t", "{}");

        await act.Should().ThrowAsync<InvalidOperationException>();
        sent.Should().Be(0);
    }

    [Fact]
    public async Task Create_SendsTheOwnerBindOnTheWire()
    {
        string? body = null;
        var service = NewService(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent($"{{\"sprk_servicerequestid\":\"{Guid.NewGuid():D}\"}}", Encoding.UTF8, "application/json"),
            };
        });

        await service.CreateServiceRequestAsync(Contact, Team, 100000000, "t", "{}");

        body.Should().Contain($"\"ownerid@odata.bind\":\"/teams({Team})\"");
    }

    // ---------------------------------------------------------------------------------------------
    // Owner decision 2026-10-12: the requester's department is the contact record's standard `department` column
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ContactDepartment_IsReadFromTheContactsDepartmentColumn_Trimmed()
    {
        Uri? requested = null;
        var service = NewService(request =>
        {
            requested = request.RequestUri;
            return Json(HttpStatusCode.OK, "{\"department\":\"  Research & Development \",\"contactid\":\"" + Contact + "\"}");
        });

        (await service.GetContactDepartmentAsync(Contact)).Should().Be("Research & Development");
        requested!.AbsolutePath.Should().EndWith($"/contacts({Contact:D})");
        requested.Query.Should().Be("?$select=department", "one app-only read that selects only the department column");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"department\":null}")]
    [InlineData("{\"department\":\"\"}")]
    [InlineData("{\"department\":\"   \"}")]
    public async Task ContactDepartment_MissingOrBlank_IsNull(string body)
    {
        var service = NewService(_ => Json(HttpStatusCode.OK, body));

        (await service.GetContactDepartmentAsync(Contact)).Should().BeNull();
    }

    [Fact]
    public async Task ContactDepartment_ContactNotFound_IsNull()
    {
        var service = NewService(_ => Json(HttpStatusCode.NotFound, "{}"));

        (await service.GetContactDepartmentAsync(Contact)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ContactDepartment_AReadThatFails_IsDataverseUnavailable_NeverNull(HttpStatusCode status)
    {
        var service = NewService(_ => Json(status, "{}"));

        var act = () => service.GetContactDepartmentAsync(Contact);

        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();
    }

    [Fact]
    public async Task ContactDepartment_NoContactId_Refuses()
    {
        var service = NewService(_ => Json(HttpStatusCode.OK, "{}"));

        var act = () => service.GetContactDepartmentAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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
