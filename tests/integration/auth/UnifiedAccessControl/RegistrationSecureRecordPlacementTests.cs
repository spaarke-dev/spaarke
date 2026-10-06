using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 144 (C10 part 1, #967) — registration never places a user in the Secure Record
/// business unit, nor in a team inside it.
/// </summary>
/// <remarks>
/// <para><b>The hole.</b> Registration creates demo systemusers in a business unit, and adds them to a team, both named
/// by operator-configured <c>sprk_dataverseenvironment</c> rows. Configuring the Secure Record BU (or its named owner
/// team) would make every registered user read every secure record — by depth, or by ownership. The guard compares the
/// resolved business unit by ID with the Secure Record BU resolved in the SAME target environment, before anything is
/// written, and refuses when that comparison cannot be made.</para>
///
/// <para><b>ADR-038 ban B1 — a path-A exception (root CLAUDE.md §6.5), stated rather than argued around</b>, after the
/// precedent in <c>DataverseRecordShareWireTests</c>. B1 bans <c>Mock&lt;HttpMessageHandler&gt;</c> because a
/// transport-level double encodes the wire format. <see cref="ScriptedDataverse"/> is hand-written and asserts only
/// the property that IS this contract and is observable nowhere else: whether the creating POST was SENT. The
/// registration service talks raw HTTP to an environment chosen at runtime — it has no narrower seam to substitute,
/// and "no systemuser row is created" is a statement about the requests that left it. The double answers by entity set
/// and does not assert request bodies.</para>
/// </remarks>
public class RegistrationSecureRecordPlacementTests
{
    private const string AdminUrl = "https://admin.crm.dynamics.com";
    private const string TargetUrl = "https://demo.crm.dynamics.com";

    private static readonly Guid SecureBuId = Guid.Parse("00000000-0000-0000-0000-00000000b0b0");
    private static readonly Guid OrdinaryBuId = Guid.Parse("00000000-0000-0000-0000-00000000c0c0");
    private static readonly Guid CreatedUserId = Guid.Parse("00000000-0000-0000-0000-00000000d0d0");

    private readonly ScriptedDataverse _dataverse = new();

    public RegistrationSecureRecordPlacementTests()
    {
        _dataverse.BusinessUnit("Demo Users", OrdinaryBuId);
        _dataverse.BusinessUnit("Secure Record", SecureBuId);
        _dataverse.Team("Demo Team", OrdinaryBuId);
        _dataverse.Team("Secure Record Owners", SecureBuId);
    }

    // ── Systemuser creation ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSystemUser_InAnOrdinaryBusinessUnit_CreatesTheUser()
    {
        var id = await Service().CreateSystemUserAsync(
            "oid", "Ann", "Lee", "ann@demo.com", "Demo Users", CancellationToken.None, TargetUrl);

        id.Should().Be(CreatedUserId);
        _dataverse.Posts.Should().ContainSingle(p => p.EndsWith("/systemusers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateSystemUser_InTheSecureRecordBusinessUnit_IsRefused_AndNoSystemUserIsCreated()
    {
        var act = () => Service().CreateSystemUserAsync(
            "oid", "Ann", "Lee", "ann@demo.com", "Secure Record", CancellationToken.None, TargetUrl);

        await act.Should().ThrowAsync<SecureRecordPlacementRefusedException>();
        _dataverse.Posts.Should().BeEmpty("the refusal must come BEFORE the create — no systemuser row");
        _dataverse.SecureLookupHosts.Should().OnlyContain(h => h == new Uri(TargetUrl).Host,
            "the Secure Record BU is resolved in the SAME environment the user would be created in");
    }

    /// <summary>
    /// Compared by ID, not by name: a configured name that differs textually but resolves to the Secure Record BU is
    /// still refused.
    /// </summary>
    [Fact]
    public async Task CreateSystemUser_WhenADifferentlySpelledNameResolvesToTheSecureRecordBusinessUnit_IsRefused()
    {
        _dataverse.BusinessUnit("SECURE-RECORD (alias)", SecureBuId);

        var act = () => Service().CreateSystemUserAsync(
            "oid", "Ann", "Lee", "ann@demo.com", "SECURE-RECORD (alias)", CancellationToken.None, TargetUrl);

        await act.Should().ThrowAsync<SecureRecordPlacementRefusedException>();
        _dataverse.Posts.Should().BeEmpty();
    }

    /// <summary>
    /// An unreadable Secure Record lookup refuses (fail closed). The failure carries a REAL Dataverse error body
    /// (<c>{"error":{...}}</c>), not an empty one: an empty body makes the JSON read throw on its own, so the refusal
    /// would happen by accident and the non-success-status guard would be unpinned. A Dataverse-shaped body
    /// deserializes cleanly to "no <c>value</c> collection" — zero matches, i.e. "this environment has no Secure Record
    /// business unit" — so only the status guard stands between that body and creating the user in the Secure BU
    /// (verifier finding, task 144 round 2). Both an outage (503) and a permission failure on the lookup (403) refuse.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task CreateSystemUser_WhenTheSecureRecordLookupFails_IsRefused_FailClosed(HttpStatusCode failure)
    {
        _dataverse.SecureLookupFailure = failure;

        var act = () => Service().CreateSystemUserAsync(
            "oid", "Ann", "Lee", "ann@demo.com", "Demo Users", CancellationToken.None, TargetUrl);

        await act.Should().ThrowAsync<SecureRecordPlacementRefusedException>();
        _dataverse.Posts.Should().BeEmpty("an unreadable Secure Record lookup cannot rule the business unit out");
    }

    /// <summary>The team-membership guard fails closed on the same realistic error bodies.</summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AddUserToTeam_WhenTheSecureRecordLookupFails_IsRefused_FailClosed(HttpStatusCode failure)
    {
        _dataverse.SecureLookupFailure = failure;

        var act = () => Service().AddUserToTeamAsync("Demo Team", CreatedUserId, CancellationToken.None, TargetUrl);

        await act.Should().ThrowAsync<SecureRecordPlacementRefusedException>();
        _dataverse.Posts.Should().BeEmpty("an unreadable Secure Record lookup cannot rule the team's business unit out");
    }

    [Fact]
    public async Task CreateSystemUser_WhenTheSecureRecordNameIsAmbiguous_IsRefused()
    {
        _dataverse.BusinessUnit("Secure Record", Guid.NewGuid()); // a second BU with the configured name

        var act = () => Service().CreateSystemUserAsync(
            "oid", "Ann", "Lee", "ann@demo.com", "Demo Users", CancellationToken.None, TargetUrl);

        await act.Should().ThrowAsync<SecureRecordPlacementRefusedException>();
        _dataverse.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSystemUser_InAnEnvironmentWithNoSecureRecordBusinessUnit_CreatesTheUser()
    {
        _dataverse.RemoveBusinessUnit("Secure Record");

        var id = await Service().CreateSystemUserAsync(
            "oid", "Ann", "Lee", "ann@demo.com", "Demo Users", CancellationToken.None, TargetUrl);

        id.Should().Be(CreatedUserId, "nothing can be placed in a business unit the environment does not have");
    }

    // ── Team membership ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddUserToTeam_ForATeamInTheSecureRecordBusinessUnit_IsRefused_AndNoMembershipIsCreated()
    {
        var act = () => Service().AddUserToTeamAsync("Secure Record Owners", CreatedUserId, CancellationToken.None, TargetUrl);

        await act.Should().ThrowAsync<SecureRecordPlacementRefusedException>();
        _dataverse.Posts.Should().BeEmpty("a member of the named owner team reads every secure record it owns");
    }

    [Fact]
    public async Task AddUserToTeam_ForAnOrdinaryTeam_AddsTheMembership()
    {
        await Service().AddUserToTeamAsync("Demo Team", CreatedUserId, CancellationToken.None, TargetUrl);

        _dataverse.Posts.Should().ContainSingle(p => p.Contains("teammembership_association", StringComparison.Ordinal));
    }

    // ── Harness ────────────────────────────────────────────────────────────────────────────────────

    private RegistrationDataverseService Service() =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATAVERSE_URL"] = AdminUrl,
                ["SecureRecord:BusinessUnitName"] = "Secure Record",
            }).Build(),
            new TrackingIdGenerator(),
            new StaticToken(),
            new SingleHandlerFactory(_dataverse),
            NullLogger<RegistrationDataverseService>.Instance,
            // Task 141's contact link runs only after a systemuser is created; nothing here creates one.
            new ContactIdentityBinderFactory(
                new SingleHandlerFactory(_dataverse), NullLoggerFactory.Instance,
                IdentityBinding.IdentityBindingTestKit.Tenants(IdentityBinding.IdentityBindingTestKit.CustomerTenant),
                TimeProvider.System),
            // Task 132: the team/BU eviction hook — inert here; its behaviour is pinned in AccessCacheFaultCachingTests.
            new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
                NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance));

    private sealed class StaticToken : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// Answers business-unit and team lookups by name, records every write's path, and fails the Secure Record lookup
    /// on demand. Request bodies are not asserted (see the class remarks).
    /// </summary>
    private sealed class ScriptedDataverse : HttpMessageHandler
    {
        private readonly List<(string Name, Guid Id)> _businessUnits = new();
        private readonly List<(string Name, Guid BusinessUnitId)> _teams = new();

        public List<string> Posts { get; } = new();

        public List<string> SecureLookupHosts { get; } = new();

        /// <summary>When set, the Secure Record lookup answers with this status and a Dataverse-shaped error body.</summary>
        public HttpStatusCode? SecureLookupFailure { get; set; }

        public void BusinessUnit(string name, Guid id) => _businessUnits.Add((name, id));

        public void RemoveBusinessUnit(string name) => _businessUnits.RemoveAll(b => b.Name == name);

        public void Team(string name, Guid businessUnitId) => _teams.Add((name, businessUnitId));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            var query = Uri.UnescapeDataString(uri.Query);

            if (request.Method == HttpMethod.Post)
            {
                Posts.Add(path);
                var created = new HttpResponseMessage(HttpStatusCode.NoContent);
                created.Headers.Add("OData-EntityId", $"{uri.GetLeftPart(UriPartial.Authority)}/api/data/v9.2/systemusers({CreatedUserId})");
                return Task.FromResult(created);
            }

            if (path.EndsWith("/businessunits", StringComparison.Ordinal))
            {
                var name = Quoted(query);
                var isSecureLookup = query.Contains("$top=2", StringComparison.Ordinal);
                if (isSecureLookup)
                {
                    SecureLookupHosts.Add(uri.Host);
                    if (SecureLookupFailure is { } failure)
                        return Task.FromResult(DataverseError(failure));
                }

                var rows = _businessUnits.Where(b => b.Name == name).Select(b => $"{{\"businessunitid\":\"{b.Id}\"}}");
                return Task.FromResult(Json($"{{\"value\":[{string.Join(",", rows)}]}}"));
            }

            if (path.EndsWith("/teams", StringComparison.Ordinal))
            {
                var name = Quoted(query);
                var rows = _teams.Where(t => t.Name == name)
                    .Select(t => $"{{\"teamid\":\"{Guid.NewGuid()}\",\"_businessunitid_value\":\"{t.BusinessUnitId}\"}}");
                return Task.FromResult(Json($"{{\"value\":[{string.Join(",", rows)}]}}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string Quoted(string query)
        {
            var start = query.IndexOf("name eq '", StringComparison.Ordinal) + "name eq '".Length;
            var end = query.IndexOf('\'', start);
            return query[start..end].Replace("''", "'");
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        /// <summary>
        /// The error shape the Dataverse Web API actually returns — a JSON object with an <c>error</c> member and no
        /// <c>value</c> collection. It parses as JSON, so nothing but the caller's status check marks it a failure.
        /// </summary>
        private static HttpResponseMessage DataverseError(HttpStatusCode status)
        {
            var (code, message) = status == HttpStatusCode.Forbidden
                ? ("0x80040220", "Principal user is missing prvReadBusinessUnit privilege.")
                : ("0x80072322", "The service is temporarily unavailable.");

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{message}\"}}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
