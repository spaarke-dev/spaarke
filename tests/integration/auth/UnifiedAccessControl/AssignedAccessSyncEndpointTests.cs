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
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 142, criterion 12 — <c>POST /api/v1/external-access/assigned-access/sync</c> (and the
/// list / dismiss routes) through the REAL <c>/api/v1/external-access</c> filter pipeline (authentication, then
/// <see cref="DelegationRuleFilter"/>), never by calling the handler: a caller WITH Write on the record reaches the
/// handler and the materialization happens — which proves the filter's new case resolves the sync request type (its
/// default branch denies everyone) — while a caller without Write and an unknown record id are the filter's identical 403.
/// </summary>
/// <remarks>
/// The delegation probe answers Write only for records the test lists, None for every other id — Dataverse's own answer
/// for a record that does not exist. The production <see cref="AssignedAccessMaterializer"/> and task 143's production
/// <see cref="NoAccessShareEnforcer"/> are composed by the host's own DI; only their module boundaries are substituted.
/// KEEP path: <c>tests/integration/auth/**</c>.
/// </remarks>
public class AssignedAccessSyncEndpointTests : IClassFixture<AssignedAccessSyncTestFixture>
{
    private const string SyncRoute = "/api/v1/external-access/assigned-access/sync";
    private const string ListRoute = "/api/v1/external-access/assigned-access";
    private const string DismissRoute = "/api/v1/external-access/assigned-access/dismiss";
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string Paralegal1 = "sprk_assignedparalegal1";

    private readonly AssignedAccessSyncTestFixture _fixture;
    private readonly Guid _matter = Guid.NewGuid();

    private AssignedAccessTestDoubles.Harness H => _fixture.Harness;

    public AssignedAccessSyncEndpointTests(AssignedAccessSyncTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        H.Store.Root(ExternalGrantRootType.Matter, _matter);
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Sync_ByACallerWithWriteOnTheRecord_ReachesTheHandler_AndMaterializesTheGrant()
    {
        var contact = H.Contact();
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, contact);
        _fixture.WritableRecords[_matter] = true;

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("assignedAccess").GetProperty("status").GetString().Should().Be(AssignedAccessStatus.Evaluated);
        body.GetProperty("noAccess").ValueKind.Should().Be(JsonValueKind.Array);
        H.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle()
            .Which.AccessLevel.Should().Be((int)ExternalAccessLevel.Collaborate);
        _fixture.ProbedTargets.Should().Contain(("sprk_matters", _matter), "the filter checked the caller's Write on the RECORD");
    }

    [Fact]
    public async Task Sync_Unauthenticated_Is401_WithNoWrites()
    {
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, H.Contact());
        _fixture.WritableRecords[_matter] = true;

        var response = await _fixture.CreateUnauthenticatedClient()
            .PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        H.TotalWrites.Should().Be(0);
    }

    [Fact]
    public async Task Sync_WithoutWrite_AndForAnUnknownRecord_AreTheSameFilter403_WithNoWrites()
    {
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, H.Contact());
        var client = _fixture.CreateAuthenticatedClient();

        var denied = await client.PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });
        var unknown = await client.PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = Guid.NewGuid() });

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unknown.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var d = await BodyOf(denied);
        var u = await BodyOf(unknown);
        d.GetProperty("reasonCode").GetString().Should().Be(DelegationRuleFilter.DenyWriteRequired);
        u.GetProperty("reasonCode").GetString().Should().Be(DelegationRuleFilter.DenyWriteRequired);
        d.GetProperty("detail").GetString().Should().Be(u.GetProperty("detail").GetString(),
            "an unknown record is indistinguishable from one the caller cannot write (enumeration-safe)");
        H.TotalWrites.Should().Be(0);
    }

    [Fact]
    public async Task Sync_IgnoresSubjectIdsInTheBody_AndAnswersFromTheRecordsOwnColumns()
    {
        var assigned = H.Contact();
        var smuggled = H.Contact();
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, assigned);
        _fixture.WritableRecords[_matter] = true;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(SyncRoute, new
        {
            recordType = "matter",
            recordId = _matter,
            contactId = smuggled,
            subjects = new[] { smuggled },
            sprk_assignedattorney1 = smuggled,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        H.Grants.ActiveRowsOf(_matter, assigned).Should().ContainSingle();
        H.Grants.ActiveRowsOf(_matter, smuggled).Should().BeEmpty("a client-supplied subject is never trusted");
    }

    [Fact]
    public async Task Sync_WhenTheLedgerCannotBeRead_IsProblemDetailsWithAReasonAndAMessage_NeverABare500()
    {
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, H.Contact());
        H.Store.FailLedgerRead = true;
        _fixture.WritableRecords[_matter] = true;

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be(AssignedAccessSyncEndpoint.SyncFailedReasonCode);
        body.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        body.TryGetProperty("traceId", out _).Should().BeTrue();
        H.TotalWrites.Should().Be(0);
    }

    [Fact]
    public async Task Sync_WhenTheFlagsCannotBeRead_Is503WithAReason_AndWritesNothing()
    {
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, H.Contact());
        H.Participations.Flags[_matter] = RootRecordFlags.Unreadable;
        _fixture.WritableRecords[_matter] = true;

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyOf(response)).GetProperty("reasonCode").GetString()
            .Should().Be(AssignedAccessSyncEndpoint.FlagsUnreadableReasonCode);
        H.TotalWrites.Should().Be(0);
    }

    [Fact]
    public async Task Sync_WhenAShareCannotBeConfirmed_IsIncompleteProblemDetails_NamingTheUser()
    {
        var (contact, user) = H.LinkedContact();
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, contact);
        H.Shares.IgnoreWrites = true;
        _fixture.WritableRecords[_matter] = true;

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be(AssignedAccessSyncEndpoint.SyncIncompleteReasonCode);
        body.GetProperty("detail").GetString().Should().Contain(user.ToString());
    }

    [Fact]
    public async Task Sync_OfALinkedUser_ClearsTheirRootSetUnderTheCallersTenant()
    {
        var (contact, user) = H.LinkedContact();
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, contact);
        _fixture.WritableRecords[_matter] = true;

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        H.Cache.Removed.Should().Contain(r => r.Tenant == WorkspaceTestConstants.TestTenantId
                                              && r.Id == ImpersonatedRootSetSource.CacheId(user, "sprk_matter"));
    }

    [Fact]
    public async Task List_ShowsASecureRecordsSuggestion_NamingItsSourceField()
    {
        H.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var contact = H.Contact();
        H.Store.Names[contact] = "Pat Paralegal";
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Paralegal1, contact);
        _fixture.WritableRecords[_matter] = true;
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });

        var response = await client.GetAsync($"{ListRoute}?recordType=matter&recordId={_matter}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var entry = (await BodyOf(response)).GetProperty("entries").EnumerateArray().Should().ContainSingle().Subject;
        entry.GetProperty("state").GetString().Should().Be(nameof(AssignedAccessState.PendingConfirmation));
        entry.GetProperty("sourceFieldLabel").GetString().Should().Be("Assigned Paralegal 1");
        entry.GetProperty("subjectName").GetString().Should().Be("Pat Paralegal");
    }

    [Fact]
    public async Task List_WithoutWrite_IsTheFilters403()
    {
        var response = await _fixture.CreateAuthenticatedClient()
            .GetAsync($"{ListRoute}?recordType=matter&recordId={_matter}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dismiss_ThroughTheFilter_DeclinesTheSuggestion_AndAPendingCheckRefusesASecondDismiss()
    {
        H.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var contact = H.Contact();
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, contact);
        _fixture.WritableRecords[_matter] = true;
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(SyncRoute, new { recordType = "matter", recordId = _matter });
        var entryId = H.Store.RowsOf(_matter, contact).Single().Id;

        var first = await client.PostAsJsonAsync(DismissRoute, new { recordType = "matter", recordId = _matter, entryId });
        var second = await client.PostAsJsonAsync(DismissRoute, new { recordType = "matter", recordId = _matter, entryId });

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        H.Store.RowsOf(_matter, contact).Single().State.Should().Be(AssignedAccessState.Declined);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyOf(second)).GetProperty("reasonCode").GetString()
            .Should().Be(AssignedAccessSyncEndpoint.EntryNotPendingReasonCode);
    }

    [Fact]
    public async Task Dismiss_WithoutWrite_IsTheFilters403_AndDeclinesNothing()
    {
        H.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var contact = H.Contact();
        H.Store.Assign(ExternalGrantRootType.Matter, _matter, Attorney1, contact);
        await H.SyncAsync(ExternalGrantRootType.Matter, _matter);
        var entryId = H.Store.RowsOf(_matter, contact).Single().Id;

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(DismissRoute, new { recordType = "matter", recordId = _matter, entryId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        H.Store.RowsOf(_matter, contact).Single().State.Should().Be(AssignedAccessState.PendingConfirmation);
    }
}

/// <summary>
/// Test host for the Assigned-To routes: the real pipeline, a delegation probe that answers for the records the test
/// lists, and the production materializer and No Access enforcer over <see cref="AssignedAccessTestDoubles.Harness"/>.
/// </summary>
public sealed class AssignedAccessSyncTestFixture : WorkspaceTestFixture
{
    internal AssignedAccessTestDoubles.Harness Harness { get; private set; } = new();

    internal NoAccessEnforcementTestDoubles.FakeEnforcementStore EnforcementStore { get; private set; } = new();

    /// <summary>Records the caller holds Write on. Any other id answers None — Dataverse's answer for an absent row.</summary>
    public ConcurrentDictionary<Guid, bool> WritableRecords { get; } = new();

    public ConcurrentBag<(string EntitySet, Guid RecordId)> ProbedTargets { get; } = new();

    public void Reset()
    {
        Harness = new AssignedAccessTestDoubles.Harness();
        EnforcementStore = new NoAccessEnforcementTestDoubles.FakeEnforcementStore();
        WritableRecords.Clear();
        ProbedTargets.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new RecordProbe(this));

            // The materializer and the enforcer are the PRODUCTION registrations, composed by the host's own DI; only
            // their module boundaries are substituted — each resolved per request, so Reset's new harness takes effect.
            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton<DataverseWebApiClient>(new AssignedAccessTestDoubles.ForwardingClient(() => Harness.Grants));
            services.RemoveAll<AssignedAccessStore>();
            services.AddScoped<AssignedAccessStore>(_ => Harness.Store);
            services.RemoveAll<ExternalParticipationService>();
            services.AddScoped<ExternalParticipationService>(_ => Harness.Participations);
            services.RemoveAll<IContactIdentityStore>();
            services.AddScoped<IContactIdentityStore>(_ => Harness.Identities);
            services.RemoveAll<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>();
            services.AddScoped<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>(_ => Harness.Shares);
            services.RemoveAll<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
            services.AddScoped(_ => Harness.Cache.Mock.Object);
            services.RemoveAll<ISubjectStandingGrantReader>();
            services.AddScoped<ISubjectStandingGrantReader>(_ => Harness.Standing);
            services.RemoveAll<NoAccessListReader>();
            services.RemoveAll<INoAccessListReader>();
            services.AddScoped<INoAccessListReader>(_ => Harness.DenyList);
            services.RemoveAll<IAccessibleRecordSetService>();
            services.AddScoped<IAccessibleRecordSetService>(_ => Harness.AccessibleRecords);
            services.RemoveAll<NoAccessEnforcementStore>();
            services.AddScoped<NoAccessEnforcementStore>(_ => EnforcementStore);
        });
    }

    public new HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return client;
    }

    private sealed class RecordProbe : CallerRecordAccessProbe
    {
        private readonly AssignedAccessSyncTestFixture _fixture;

        public RecordProbe(AssignedAccessSyncTestFixture fixture)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _fixture = fixture;

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            _fixture.ProbedTargets.Add((entitySet, recordId));
            return Task.FromResult(_fixture.WritableRecords.ContainsKey(recordId)
                ? AccessRights.Read | AccessRights.Write
                : AccessRights.None);
        }
    }
}
