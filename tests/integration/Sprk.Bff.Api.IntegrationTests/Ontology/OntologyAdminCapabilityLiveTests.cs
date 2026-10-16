// spaarke-ontology-platform-r1 task 104 — LIVE, READ-ONLY proof of GET /api/v1/ontology/admin/capabilities as REAL users against a
// real Dataverse environment: the owner's Spaarke Ontology Administrator account answers true, a Spaarke Console User false.
//
// Opt-in only (skip-via-attribute, the repo's convention): a plain `dotnet test` touches no network. Enable with
//   SPAARKE_LIVE_ONTOLOGY_DATAVERSE_URL=https://<env>.crm.dynamics.com            (spaarkedev1)
//   SPAARKE_LIVE_ONTOLOGY_ADMIN_ID=<systemuserid of a user holding Spaarke Ontology Administrator>
//   SPAARKE_LIVE_ONTOLOGY_CONSOLE_USER_ID=<systemuserid of a non-admin Spaarke Console User>
// and an `az login` session for an identity that may impersonate (System Administrator).
//
// NOTHING IS WRITTEN: the route and the harness only read.
//
// WHAT IS SUBSTITUTED, AND WHY (the SignalReadRouteLiveTests method, task 038):
//   (1) the inbound TOKEN — a fake scheme carrying the acting user's REAL Entra oid (a workstation cannot mint a user token for
//       the BFF audience);
//   (2) IDataverseUserClient's OBO exchange — the operator's `az login` Dataverse token with MSCRMCallerID = the acting user, so
//       Dataverse runs RetrieveUserPrivilegeByPrivilegeName AS THAT USER and enforces their privileges on the call itself; the error
//       mapping is the production one (DataverseUserClient.MapErrorCode). WhoAmI ignores MSCRMCallerID, so it is answered with the
//       acting user (what the user's own token answers).
using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.IntegrationTests.Ontology;

public sealed class OntologyAdminCapabilityLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_ONTOLOGY_DATAVERSE_URL";
    private const string Route = "/api/v1/ontology/admin/capabilities";

    private sealed class LiveOntologyFactAttribute : FactAttribute
    {
        public LiveOntologyFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVar)))
                Skip = $"Live leg: set {UrlVar}, SPAARKE_LIVE_ONTOLOGY_ADMIN_ID and SPAARKE_LIVE_ONTOLOGY_CONSOLE_USER_ID to run it (read-only).";
        }
    }

    private readonly ITestOutputHelper _out;

    public OntologyAdminCapabilityLiveTests(ITestOutputHelper output) => _out = output;

    [LiveOntologyFact]
    public async Task LiveMode_TheAdministratorGetsTrue_AConsoleUserGetsFalse()
    {
        var url = Environment.GetEnvironmentVariable(UrlVar)!;
        var admin = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_ONTOLOGY_ADMIN_ID")!);
        var consoleUser = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_ONTOLOGY_CONSOLE_USER_ID")!);
        await using var factory = new LiveOntologyFactory(url, new AzureCliCredential(), _out);
        await factory.InitAsync();

        foreach (var (user, expected) in new[] { (admin, true), (consoleUser, false) })
        {
            await factory.ActAsAsync(user);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-user");

            var response = await client.GetAsync(Route);
            var text = await response.Content.ReadAsStringAsync();
            _out.WriteLine($"GET {Route} as {user} -> {(int)response.StatusCode} {text}");

            response.StatusCode.Should().Be(HttpStatusCode.OK, text);
            using var doc = JsonDocument.Parse(text);
            doc.RootElement.GetProperty("canAuthorPolicies").GetBoolean().Should().Be(expected, $"systemuser {user}");
            factory.PrivilegeCalls.Should().Contain(c => c.StartsWith($"systemusers({user:D})/", StringComparison.Ordinal) && c.EndsWith("200"),
                "the privilege function ran as that user and Dataverse accepted the call");
        }
    }

    private sealed class LiveOntologyFactory : DataverseIntegrationTestFixture
    {
        private static readonly HttpClient Http = new();
        private readonly TokenCredential _credential;
        private readonly ITestOutputHelper _out;

        public LiveOntologyFactory(string dataverseUrl, TokenCredential credential, ITestOutputHelper output)
        {
            _credential = credential;
            _out = output;
            DataverseUrl = dataverseUrl.TrimEnd('/');
        }

        public string DataverseUrl { get; }
        public Guid? OperatorSystemUserId { get; private set; }
        public Guid? CallerSystemUserId { get; private set; }
        public string? CallerObjectId { get; private set; }
        public List<string> PrivilegeCalls { get; } = new();

        public async Task InitAsync()
        {
            using var who = await SendAsync("WhoAmI()", null);
            OperatorSystemUserId = JsonDocument.Parse(await who.Content.ReadAsStringAsync()).RootElement.GetProperty("UserId").GetGuid();
        }

        public async Task ActAsAsync(Guid systemUserId)
        {
            using var user = await SendAsync($"systemusers({systemUserId})?$select=azureactivedirectoryobjectid", null);
            CallerObjectId = JsonDocument.Parse(await user.Content.ReadAsStringAsync()).RootElement.GetProperty("azureactivedirectoryobjectid").GetString();
            CallerSystemUserId = systemUserId;
            _out.WriteLine($"acting as systemuser {systemUserId} (oid {CallerObjectId})");
        }

        internal async Task<HttpResponseMessage> SendAsync(string relativePath, Guid? asUser)
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { $"{DataverseUrl}/.default" }), default);
            var request = new HttpRequestMessage(HttpMethod.Get, $"{DataverseUrl}/api/data/v9.2/{relativePath.TrimStart('/')}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (asUser is { } u && u != OperatorSystemUserId)
                request.Headers.Add("MSCRMCallerID", u.ToString("D"));
            return await Http.SendAsync(request);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataverseUserClient>();
                services.AddScoped<IDataverseUserClient>(_ => new LiveUserClient(this));
                services.AddSingleton(this);
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, LiveUserAuthHandler>(LiveUserAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = LiveUserAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = LiveUserAuthHandler.SchemeName;
                });
            });
        }
    }

    /// <summary>The caller's Dataverse with the OBO exchange substituted (MSCRMCallerID); GET only — the route never writes.</summary>
    private sealed class LiveUserClient(LiveOntologyFactory factory) : IDataverseUserClient
    {
        public async Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            var path = relativePath.TrimStart('/');
            if (path == "WhoAmI()")
            {
                return factory.CallerSystemUserId is { } caller
                    ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { UserId = caller.ToString("D") }))
                    : DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.AccessDenied, "No Dataverse user for this token.");
            }

            using var response = await factory.SendAsync(path, factory.CallerSystemUserId);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            factory.PrivilegeCalls.Add($"{path} -> {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                return DataverseUserResponse.Fail((int)response.StatusCode, DataverseUserClient.MapErrorCode((int)response.StatusCode), text);
            }

            using var doc = JsonDocument.Parse(text);
            return DataverseUserResponse.Ok((int)response.StatusCode, doc.RootElement.Clone());
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("read-only");

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("read-only");

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("read-only");

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("read-only");
    }

    private sealed class LiveUserAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        LiveOntologyFactory factory)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "LiveUser";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(factory.CallerObjectId is { } oid
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim("oid", oid), new System.Security.Claims.Claim("tid", "live") }, SchemeName)),
                    SchemeName))
                : AuthenticateResult.Fail("no acting user"));
    }
}
