using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Registration;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// #1363: the two reads demo expiry depends on, over an HTTP double of the admin Dataverse.
/// <list type="bullet">
/// <item>The target-environment read reports "absent" only for Dataverse's "record not found" (0x80040217). Any other
/// 404 is a fault.</item>
/// <item>The Provisioned list is read whole, across pages.</item>
/// </list>
/// </summary>
public class DemoExpirationReadTests
{
    private const string AdminDataverse = "https://admin.crm.dynamics.com";
    private static readonly Guid EnvironmentId = Guid.Parse("13630000-0000-4000-8000-0000000000f2");

    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["DATAVERSE_URL"] = AdminDataverse })
        .Build();

    // ── Target environment: only "record not found" is absent ───────────────────────────────────────────────────

    [Fact(DisplayName = "#1363: a 404 with Dataverse's record-not-found code (0x80040217) reads as ABSENT")]
    public async Task RecordNotFound404_IsAbsent()
    {
        var registry = Registry(_ => Respond(HttpStatusCode.NotFound,
            "{\"error\":{\"code\":\"0x80040217\",\"message\":\"sprk_dataverseenvironment With Id = ... Does Not Exist\"}}"));

        var environment = await registry.GetByIdAsync(EnvironmentId);

        environment.Should().BeNull();
    }

    /// <summary>MUTATION: reading every 404 as "absent" lets a routing fault or a missing entity set expire a request
    /// whose team membership and SPE grant may still stand.</summary>
    [Theory(DisplayName = "#1363: any OTHER 404 (missing entity set, routing, no body) is a fault, never absent")]
    [InlineData("{\"error\":{\"code\":\"0x80060888\",\"message\":\"Resource not found for the segment 'sprk_dataverseenvironments'.\"}}")]
    [InlineData("<html><body>404 Not Found</body></html>")]
    [InlineData("")]
    public async Task Other404_Throws(string body)
    {
        var registry = Registry(_ => Respond(HttpStatusCode.NotFound, body));

        var act = () => registry.GetByIdAsync(EnvironmentId);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── The Provisioned list is read across pages ───────────────────────────────────────────────────────────────

    /// <summary>MUTATION: ignoring <c>@odata.nextLink</c> stops at the first page (5,000 rows): every Provisioned request
    /// past it is never expired or warned.</summary>
    [Fact(DisplayName = "#1363: the Provisioned list follows @odata.nextLink and returns every page")]
    public async Task ProvisionedList_FollowsNextLink()
    {
        var next = $"{AdminDataverse}/api/data/v9.2/sprk_registrationrequests?$skiptoken=page2";
        var requested = new List<string>();
        var service = Requests(request =>
        {
            requested.Add(request.RequestUri!.ToString());
            return request.RequestUri!.Query.Contains("skiptoken=page2", StringComparison.Ordinal)
                ? Respond(HttpStatusCode.OK, Page(next: null, Guid.Parse("13630000-0000-4000-8000-0000000000c3")))
                : Respond(HttpStatusCode.OK, Page(next,
                    Guid.Parse("13630000-0000-4000-8000-0000000000c1"), Guid.Parse("13630000-0000-4000-8000-0000000000c2")));
        });

        var records = await service.GetRequestsByStatusAsync(RegistrationStatus.Provisioned);

        records.Select(r => r.Id.ToString()).Should().Equal(
            "13630000-0000-4000-8000-0000000000c1", "13630000-0000-4000-8000-0000000000c2", "13630000-0000-4000-8000-0000000000c3");
        requested.Should().HaveCount(2);
        requested[1].Should().Be(next);
    }

    /// <summary>MUTATION: comparing the host only lets <c>http://</c> (the token in cleartext) or another port through;
    /// a relative link is refused rather than resolved.</summary>
    [Theory(DisplayName = "#1363: a next-page link to another host, scheme or port (or a relative one) is refused; the bearer token is not sent there")]
    [InlineData("https://elsewhere.example/api/data/v9.2/sprk_registrationrequests?$skiptoken=x")]
    [InlineData("http://admin.crm.dynamics.com/api/data/v9.2/sprk_registrationrequests?$skiptoken=x")]
    [InlineData("https://admin.crm.dynamics.com:8443/api/data/v9.2/sprk_registrationrequests?$skiptoken=x")]
    [InlineData("/api/data/v9.2/sprk_registrationrequests?$skiptoken=x")]
    public async Task ProvisionedList_RefusesAForeignNextLink(string next)
    {
        var requested = new List<string>();
        var service = Requests(request =>
        {
            requested.Add(request.RequestUri!.ToString());
            if (requested.Count > 5)
                throw new TimeoutException("looped: the refused link was followed");
            return Respond(HttpStatusCode.OK, Page(next, Guid.NewGuid()));
        });

        var act = () => service.GetRequestsByStatusAsync(RegistrationStatus.Provisioned);

        await act.Should().ThrowAsync<InvalidOperationException>();
        requested.Should().ContainSingle().Which.Should().StartWith(AdminDataverse + "/");
    }

    /// <summary>MUTATION: with no <c>top</c> the loop ends only when the server omits the link, so a repeated link spins
    /// forever and grows the list without bound.</summary>
    [Fact(DisplayName = "#1363: a next-page link that repeats is refused instead of looping")]
    public async Task ProvisionedList_RefusesARepeatedNextLink()
    {
        var next = $"{AdminDataverse}/api/data/v9.2/sprk_registrationrequests?$skiptoken=again";
        var requests = 0;
        var service = Requests(_ =>
        {
            if (Interlocked.Increment(ref requests) > 50)
                throw new TimeoutException("looped: the link was followed again and again");
            return Respond(HttpStatusCode.OK, Page(next, Guid.NewGuid()));
        });

        var act = () => service.GetRequestsByStatusAsync(RegistrationStatus.Provisioned);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("repeated");
        requests.Should().Be(2, "the first page and the one page the link names; the repeat is never followed");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string Page(string? next, params Guid[] ids)
    {
        var rows = string.Join(",", ids.Select(id =>
            $"{{\"sprk_registrationrequestid\":\"{id}\",\"sprk_status\":{(int)RegistrationStatus.Provisioned}}}"));
        var link = next is null ? string.Empty : $",\"@odata.nextLink\":\"{next}\"";
        return $"{{\"value\":[{rows}]{link}}}";
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    private static DataverseEnvironmentService Registry(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new Handler(respond);
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(DataverseEnvironmentService.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return new DataverseEnvironmentService(
            Configuration, new StaticTokenCredential(), http.Object, NullLogger<DataverseEnvironmentService>.Instance);
    }

    private static RegistrationDataverseService Requests(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new Handler(respond);
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        return new RegistrationDataverseService(
            Configuration, new TrackingIdGenerator(), new StaticTokenCredential(), http.Object,
            NullLogger<RegistrationDataverseService>.Instance,
            new ContactIdentityBinderFactory(
                Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now)),
            new NullMembershipCacheInvalidator(NullLogger<NullMembershipCacheInvalidator>.Instance));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
