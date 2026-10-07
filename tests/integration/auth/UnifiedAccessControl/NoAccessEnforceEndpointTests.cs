using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.NoAccessEnforcementTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 143, criterion 8 — <c>POST /api/v1/external-access/no-access/enforce</c> through the
/// REAL <c>/api/v1/external-access</c> filter pipeline (authentication, then <see cref="DelegationRuleFilter"/>), not by
/// calling the handler: a caller who can write the ACTIVE entry reaches the enforcement — which proves the filter's new
/// case resolves the enforce request (its default branch denies everyone) — while an absent entry and an entry the
/// caller cannot write are the filter's identical 403.
/// </summary>
/// <remarks>
/// The delegation probe answers for the NO ACCESS ENTRY: Write for an entry the test says the caller can write, None for
/// any other id — exactly what Dataverse's own answer is for an entry that does not exist. Placement:
/// <c>tests/integration/auth/**</c> (the security-auth KEEP path).
/// </remarks>
public class NoAccessEnforceEndpointTests : IClassFixture<NoAccessEnforceTestFixture>
{
    private const string Route = "/api/v1/external-access/no-access/enforce";
    private const string Project = "sprk_project";
    private const int CollaborateMask = 262167;

    private static readonly Guid SecureProject = Guid.Parse("14314314-0000-4000-8000-0000000000a1");
    private static readonly Guid Walled = Guid.Parse("14314314-0000-4000-8000-0000000000b1");
    private static readonly Guid Colleague = Guid.Parse("14314314-0000-4000-8000-0000000000b2");
    private static readonly Guid Author = Guid.Parse("14314314-0000-4000-8000-0000000000b3");

    private readonly NoAccessEnforceTestFixture _fixture;
    private Harness H => _fixture.Harness;

    public NoAccessEnforceEndpointTests(NoAccessEnforceTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        H.Participations.Flags[SecureProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        H.Store.Person(Author);
        H.Store.Person(Walled);
        H.Store.Person(Colleague);
        H.Store.Rights[(Author, SecureProject)] = AccessRights.Read | AccessRights.Write;
        H.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Colleague), CollaborateMask);
        H.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Walled), CollaborateMask);
    }

    private Guid ActiveEntryTheCallerCanWrite(int stateCode = 0)
    {
        var entry = H.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author, stateCode: stateCode);
        _fixture.CallerHoldsEntryWritePrivilege = true;
        return entry;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Enforce_ByACallerWhoCanWriteTheActiveEntry_ReachesTheHandler_AndRemovesTheWalledUsersShare()
    {
        var entry = ActiveEntryTheCallerCanWrite();

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(Route, new { entryId = entry });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("removed").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("systemUserId").GetGuid().Should().Be(Walled);
        body.GetProperty("complete").GetBoolean().Should().BeTrue();
        H.Shares.MaskOf(Project, SecureProject, DataversePrincipalRef.User(Walled)).Should().BeNull();
        _fixture.ProbedPrivileges.Should().Contain(NoAccessEnforceEndpoint.EntryWritePrivilege,
            "the filter checked the caller's Write on the entry through the table privilege (organization-owned table)");
        _fixture.ProbedTargets.Should().NotContain(t => t.EntitySet == NoAccessEnforceEndpoint.EntrySet,
            "Dataverse refuses RetrievePrincipalAccess on an organization-owned table (400 0x80040800), so the entry is never probed per record");
        H.Cache.Removed.Should().Contain(r => r.Tenant == WorkspaceTestConstants.TestTenantId
                                              && r.Id == $"{Walled:D}:{Project}",
            "the walled user's root-set cache is cleared under the caller's tenant — the namespace their reads use");
    }

    [Fact]
    public async Task Enforce_Unauthenticated_Is401()
    {
        var entry = ActiveEntryTheCallerCanWrite();

        var response = await _fixture.CreateUnauthenticatedClient().PostAsJsonAsync(Route, new { entryId = entry });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        H.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_ByACallerWithoutTheEntryWritePrivilege_AnAbsentAndAnExistingEntry_AreTheSameFilter403_WithNoWrites()
    {
        var unwritable = H.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        var client = _fixture.CreateAuthenticatedClient();

        var absent = await client.PostAsJsonAsync(Route, new { entryId = Guid.NewGuid() });
        var denied = await client.PostAsJsonAsync(Route, new { entryId = unwritable });

        absent.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var a = await BodyOf(absent);
        var d = await BodyOf(denied);
        a.GetProperty("reasonCode").GetString().Should().Be(DelegationRuleFilter.DenyWriteRequired);
        d.GetProperty("reasonCode").GetString().Should().Be(DelegationRuleFilter.DenyWriteRequired);
        a.GetProperty("detail").GetString().Should().Be(d.GetProperty("detail").GetString(),
            "an absent entry is indistinguishable from one the caller cannot write (enumeration-safe)");
        H.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_ByACallerWithTheEntryWritePrivilege_AnAbsentEntry_IsTheHandlers404_WithNoWrites()
    {
        // The privilege is table-wide, so its holder may already read the table: the handler's own 404 discloses nothing.
        _fixture.CallerHoldsEntryWritePrivilege = true;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(Route, new { entryId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be(NoAccessEnforceEndpoint.EntryNotFoundReasonCode);
        H.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_AnInactiveEntry_ReachesTheHandler_AndIsRefusedWithAMessage_AndNoWrites()
    {
        var entry = ActiveEntryTheCallerCanWrite(stateCode: 1);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(Route, new { entryId = entry });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be(NoAccessEnforceEndpoint.EntryInactiveReasonCode);
        body.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        H.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforce_IgnoresEveryBodyFieldButTheEntryId()
    {
        var entry = ActiveEntryTheCallerCanWrite();
        var otherRecord = Guid.NewGuid();

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(Route, new
        {
            entryId = entry,
            systemUserId = Colleague,          // NOT walled by this entry
            recordId = otherRecord,
            recordType = "matter",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        H.Shares.MaskOf(Project, SecureProject, DataversePrincipalRef.User(Colleague)).Should().Be(CollaborateMask,
            "the body's user is ignored — only the entry's own subject is enforced");
        H.Shares.MaskOf(Project, SecureProject, DataversePrincipalRef.User(Walled)).Should().BeNull();
    }

    [Fact]
    public async Task Enforce_WhenARevokeIsNotConfirmed_IsProblemDetailsWithAMessage_NeverABare500()
    {
        var entry = ActiveEntryTheCallerCanWrite();
        H.Shares.IgnoreWrites = true;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(Route, new { entryId = entry });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be(NoAccessEnforceEndpoint.IncompleteReasonCode);
        body.GetProperty("detail").GetString().Should().Contain(Walled.ToString()).And.Contain(SecureProject.ToString());
        body.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Enforce_WhenItWouldRemoveTheLastPersonWhoCanOpenTheSecureRecord_Is409NamingTheRecord()
    {
        H.Shares.Reset();
        H.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Walled), CollaborateMask);
        var entry = ActiveEntryTheCallerCanWrite();

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(Route, new { entryId = entry });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be(NoAccessEnforceEndpoint.LastPersonReasonCode);
        body.GetProperty("detail").GetString().Should().Contain(SecureProject.ToString());
        H.Shares.MaskOf(Project, SecureProject, DataversePrincipalRef.User(Walled)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task Enforce_WhenTheEntrysAuthorLacksWriteOnTheRecord_RemovesNothing_AndReportsNotEnforced()
    {
        H.Store.Rights[(Author, SecureProject)] = AccessRights.Read;
        var entry = ActiveEntryTheCallerCanWrite();

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(Route, new { entryId = entry });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var notEnforced = (await BodyOf(response)).GetProperty("notEnforced").EnumerateArray().ToList();
        notEnforced.Should().ContainSingle().Which.GetProperty("reason").GetString()
            .Should().Be(NoAccessEnforcementReason.AuthorLacksWrite);
        H.Shares.Writes.Should().BeEmpty("the caller's Write on the ENTRY is not authority over the record (owner N5)");
    }
}

/// <summary>
/// Test host for the enforce route: the real pipeline, a delegation probe that answers for No Access entries, and the
/// production <see cref="NoAccessShareEnforcer"/> over <see cref="NoAccessEnforcementTestDoubles.Harness"/>.
/// </summary>
public sealed class NoAccessEnforceTestFixture : WorkspaceTestFixture
{
    internal Harness Harness { get; private set; } = new();

    /// <summary>Whether the caller holds the entry table's Write privilege — Write on every row of the organization-owned table.</summary>
    public bool CallerHoldsEntryWritePrivilege { get; set; }

    public ConcurrentBag<string> ProbedPrivileges { get; } = new();

    public ConcurrentBag<(string EntitySet, Guid RecordId)> ProbedTargets { get; } = new();

    public void Reset()
    {
        Harness = new Harness();
        CallerHoldsEntryWritePrivilege = false;
        ProbedPrivileges.Clear();
        ProbedTargets.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new EntryProbe(this));

            var client = new Mock<DataverseWebApiClient>(ClientConfig(), NullLogger<DataverseWebApiClient>.Instance, null!, null!)
            { CallBase = false };
            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton(client.Object);

            // The enforcer itself is the PRODUCTION registration, composed by the host's own DI; only its module
            // boundaries are substituted — each resolved per request, so Reset's new harness takes effect.
            services.RemoveAll<NoAccessEnforcementStore>();
            services.AddScoped<NoAccessEnforcementStore>(_ => Harness.Store);
            services.RemoveAll<ExternalParticipationService>();
            services.AddScoped<ExternalParticipationService>(_ => Harness.Participations);
            services.RemoveAll<IContactIdentityStore>();
            services.AddScoped<IContactIdentityStore>(_ => Harness.Identities);
            services.RemoveAll<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>();
            services.AddScoped<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>(_ => Harness.Shares);
            services.RemoveAll<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
            services.AddScoped(_ => Harness.Cache.Mock.Object);
        });
    }

    public new HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return client;
    }

    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb"
        }).Build();

    private sealed class EntryProbe : CallerRecordAccessProbe
    {
        private readonly NoAccessEnforceTestFixture _fixture;

        public EntryProbe(NoAccessEnforceTestFixture fixture)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _fixture = fixture;

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            _fixture.ProbedTargets.Add((entitySet, recordId));
            return Task.FromResult(AccessRights.None);
        }

        public override Task<bool> CallerHoldsPrivilegeAsync(
            string? callerBearerToken, string privilegeName, CancellationToken ct = default)
        {
            _fixture.ProbedPrivileges.Add(privilegeName);
            return Task.FromResult(privilegeName == NoAccessEnforceEndpoint.EntryWritePrivilege && _fixture.CallerHoldsEntryWritePrivilege);
        }
    }
}
