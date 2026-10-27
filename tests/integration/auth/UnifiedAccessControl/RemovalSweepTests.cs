using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// C4: the removal sweep of <see cref="IdentityLinkReconciliationJob"/>. Through the PRODUCTION job, the sweep, the shared
/// removal (<see cref="ExternalPersonRemovalService.RemoveAsync"/>) and the shared revoke, over the task-142 harness. The
/// rule: only an inactive, unmarked (on a proven read) contact holding a binding or an active grant is marked; a marked contact
/// runs again only while it still holds an active grant row; writes need both switches; a run acts on at most the cap; the
/// statecode and the binding never change. KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class RemovalSweepTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string Email = "person@customer.example";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();
    private readonly BindingLogCapture<IdentityLinkReconciliationJob> _log = new();
    private readonly AuditLogCapture _audit = new();

    public RemovalSweepTests()
    {
        _h.Store.Root(Matter, _matter);
        _h.Grants.RootOwners[_matter] = Guid.NewGuid();
    }

    // ── Wiring ──────────────────────────────────────────────────────────────────────────────────────────────────

    private RemovalSweepWorld World { get; set; } = null!;

    private async Task<JobRunResult> RunAsync(bool? linkWrites, bool? sweepWrites, int? cap = null, bool looseScan = false)
    {
        var settings = new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://own.crm.dynamics.com" };
        if (linkWrites is { } l) settings["IdentityLink:Reconciliation:WritesEnabled"] = l ? "true" : "false";
        if (sweepWrites is { } s) settings[ExternalAccessRemovalSweep.RemovalSweepWritesEnabledConfigKey] = s ? "true" : "false";
        if (cap is { } c) settings[ExternalAccessRemovalSweep.MaxContactsPerRunConfigKey] = c.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        World = new RemovalSweepWorld(_h.Grants, _h.Identities) { LooseCandidateScan = looseScan };
        var services = new ServiceCollection();
        services.AddSingleton(IdentityBindingTestKit.Binder(_h.Identities));
        services.AddSingleton<DataverseWebApiClient>(World);
        services.AddSingleton<ExternalParticipationService>(_h.Participations);
        services.AddSingleton(new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance));
        services.AddSingleton(_h.Materializer);
        services.AddSingleton(TestRecordContainerResolver.ForNonSecureRecord("sprk_matter", _matter));
        services.AddSingleton(Mock.Of<IMembershipCacheInvalidator>());
        services.AddSingleton(_audit.Factory);
        using var provider = services.BuildServiceProvider();

        var job = new IdentityLinkReconciliationJob(provider.GetRequiredService<IServiceScopeFactory>(), _h.Time, configuration, _log);
        return await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-c4", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
            CancellationToken.None);
    }

    /// <summary>A person signed in through CIAM (bound oid), with an invite stamp and baselines, at <paramref name="stateCode"/>.</summary>
    private (Guid ContactId, string Oid) Person(int stateCode, bool bound = true)
    {
        var id = Guid.NewGuid();
        var oid = Guid.NewGuid().ToString("D");
        var c = _h.Identities.AddContact(id, email: Email, oid: bound ? oid : null,
            plane: bound ? IdentityPlaneMarker.External : null, stateCode: stateCode, inviteCodeHash: "code-hash");
        c.AccessPermissionGrant = (int)ExternalAccessLevel.ViewOnly;
        c.StandingGrant = (int)ExternalAccessLevel.ViewOnly;
        return (id, oid);
    }

    private ExternalGrantRow GrantOnMatter(Guid contactId)
        => _h.Grants.Seed(Matter, _matter, contactId, null, (int)ExternalAccessLevel.Collaborate, null);

    private static JsonElement Sweep(JobRunResult result)
        => JsonDocument.Parse(result.ResultJson!).RootElement.GetProperty("removalSweep");

    private IEnumerable<(string Category, EventId EventId, LogLevel Level, string Message)> SweepAudit
        => _audit.Records.Where(r => r.EventId.Id == ExternalAccessRemovalSweep.SweepRemovalAuditEvent.Id)
            .Select(r => (r.Category, r.EventId, r.Level, r.Message));

    // ── Gates (C4 condition 6) ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(null, true)]
    [InlineData(null, null)]
    public async Task WithEitherSwitchOff_TheSweepIsReportOnly_AndWritesNothing(bool? linkWrites, bool? sweepWrites)
    {
        var (contact, _) = Person(stateCode: 1);
        var grant = GrantOnMatter(contact);

        var result = await RunAsync(linkWrites, sweepWrites);

        _h.Identities.Writes.Should().BeEmpty();
        _h.Grants.WriteCount.Should().Be(0);
        _h.Grants.Rows.Single(r => r.Id == grant.Id).IsActive.Should().BeTrue();
        _h.Identities.Contacts[contact].RemovedOn.Should().BeNull();
        SweepAudit.Should().BeEmpty();
        var sweep = Sweep(result);
        sweep.GetProperty("mode").GetString().Should().Be("report-only");
        sweep.GetProperty("contacts").EnumerateArray().Should().ContainSingle(c =>
            c.GetProperty("contactId").GetString() == contact.ToString("D")
            && c.GetProperty("action").GetString() == nameof(RemovalSweepAction.MarkAndRevoke));
        _log.Messages.Should().Contain(m => m.Contains("removal-sweep mode=report-only", StringComparison.Ordinal)
                                            && m.Contains(contact.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void BothSwitches_AreNeeded_AndTheSweepSwitchDefaultsOff()
    {
        static IConfiguration Config(params (string Key, string Value)[] pairs) => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build();

        ExternalAccessRemovalSweep.WritesEnabled(Config(("IdentityLink:Reconciliation:WritesEnabled", "true"))).Should().BeFalse();
        ExternalAccessRemovalSweep.WritesEnabled(Config(
            ("IdentityLink:Reconciliation:WritesEnabled", "true"),
            (ExternalAccessRemovalSweep.RemovalSweepWritesEnabledConfigKey, "yes"))).Should().BeFalse("unparseable = off");
        ExternalAccessRemovalSweep.WritesEnabled(Config(
            ("IdentityLink:Reconciliation:WritesEnabled", "true"),
            (ExternalAccessRemovalSweep.RemovalSweepWritesEnabledConfigKey, "true"))).Should().BeTrue();
        ExternalAccessRemovalSweep.RemovalSweepWritesEnabledConfigKey
            .Should().Be("IdentityLink:Reconciliation:RemovalSweepWritesEnabled");
    }

    // ── Must fire: mark, then revoke through the shared removal ────────────────────────────────────────────────

    [Fact]
    public async Task AnInactiveBoundUnmarkedContact_IsMarkedFirst_ThenRevoked_ThroughTheSharedRemoval_AndAudited()
    {
        var (contact, oid) = Person(stateCode: 1);
        var grant = GrantOnMatter(contact);

        var result = await RunAsync(linkWrites: true, sweepWrites: true);

        var row = _h.Identities.Contacts[contact];
        row.RemovedOn.Should().Be(_h.Time.Now);
        _h.Grants.Rows.Single(r => r.Id == grant.Id).IsActive.Should().BeFalse();
        _h.Identities.Writes.Select(w => w.Op).Should().Equal(
            "removed-marker", "removed-person-access-state-clear", "standing-grant-clear");
        row.InviteCodeHash.Should().BeNull("the shared removal clears the invite state");
        row.StandingGrant.Should().BeNull("the shared removal clears the standing grant");

        // The statecode and the binding are unchanged.
        row.StateCode.Should().Be(1);
        row.Oid.Should().Be(oid);
        row.Plane.Should().Be((int)IdentityPlaneMarker.External);
        row.KeyMirror.Should().Be(oid);

        var sweep = Sweep(result);
        sweep.GetProperty("mode").GetString().Should().Be("write");
        sweep.GetProperty("completed").GetInt32().Should().Be(1);
        result.ProcessedItems.Should().Be(1);

        var audit = SweepAudit.Should().ContainSingle().Subject;
        audit.Category.Should().Be(ExternalAccessAudit.Category);
        audit.Level.Should().Be(LogLevel.Information);
        audit.Message.Should().StartWith("[EXT-PERSON-AUDIT] sweep-remove:")
            .And.Contain(ExternalAccessRemovalSweep.SweepActor)
            .And.Contain(contact.ToString())
            .And.Contain("corr-c4")
            .And.NotContain(Email);
    }

    [Fact]
    public async Task TheMarker_IsWrittenBeforeAnyGrantIsRevoked()
    {
        var (contact, _) = Person(stateCode: 1);
        GrantOnMatter(contact);

        await RunAsync(linkWrites: true, sweepWrites: true);

        var markerWrite = _h.Identities.Writes.FindIndex(w => w.Op == "removed-marker");
        markerWrite.Should().Be(0, "the marker is the first identity write");
        World.GrantUpdatesBeforeMarker.Should().Be(0, "no grant row is deactivated before the marker is written");
    }

    [Fact]
    public async Task AnInactiveContactHoldingOnlyAGrant_NoBinding_IsMarkedAndRevoked()
    {
        var (contact, _) = Person(stateCode: 1, bound: false);
        GrantOnMatter(contact);

        await RunAsync(linkWrites: true, sweepWrites: true);

        _h.Identities.Contacts[contact].RemovedOn.Should().NotBeNull();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
    }

    // ── Must not fire: the closed selection ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The candidate queries are hints only: with a scan that returns EVERY contact and every active grant (as a filter that
    /// field security changes might), the live decision still marks nobody active.
    /// </summary>
    [Fact]
    public async Task AnActiveContact_IsNeverMarked_EvenWhenTheScanReturnsIt()
    {
        var (contact, _) = Person(stateCode: 0);
        var grant = GrantOnMatter(contact);

        var result = await RunAsync(linkWrites: true, sweepWrites: true, looseScan: true);

        Sweep(result).GetProperty("candidates").GetInt32().Should().Be(1, "control: the loose scan returned the contact");
        _h.Identities.Contacts[contact].RemovedOn.Should().BeNull();
        _h.Identities.Writes.Should().BeEmpty();
        _h.Grants.Rows.Single(r => r.Id == grant.Id).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AnInactiveContact_WithNoBindingAndNoGrant_IsUntouched_EvenWhenTheScanReturnsIt()
    {
        var (contact, _) = Person(stateCode: 1, bound: false);

        var result = await RunAsync(linkWrites: true, sweepWrites: true, looseScan: true);

        Sweep(result).GetProperty("candidates").GetInt32().Should().Be(1, "control: the loose scan returned the contact");
        _h.Identities.Contacts[contact].RemovedOn.Should().BeNull();
        _h.Identities.Writes.Should().BeEmpty();
        Sweep(result).GetProperty("clean").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AnUnprovenMarkerRead_IsSkippedAndLogged_NeverMarked()
    {
        var (contact, _) = Person(stateCode: 1);
        var grant = GrantOnMatter(contact);
        _h.Identities.RemovedMarkerProof = ColumnReadProofOutcome.Unproven;

        var result = await RunAsync(linkWrites: true, sweepWrites: true);

        _h.Identities.Contacts[contact].RemovedOn.Should().BeNull();
        _h.Identities.Writes.Should().BeEmpty();
        _h.Grants.Rows.Single(r => r.Id == grant.Id).IsActive.Should().BeTrue();
        Sweep(result).GetProperty("markerUnproven").GetInt32().Should().Be(1);
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning
                                           && e.Message.Contains(contact.ToString(), StringComparison.Ordinal)
                                           && e.Message.Contains("not proven", StringComparison.Ordinal));
        result.Success.Should().BeFalse("a skipped contact makes the run partial, never a silent ok");
    }

    // ── Incomplete cleanup, from live state ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AMarkedContact_StillHoldingAnActiveGrantRow_HasItsRemovalRunAgain()
    {
        var (contact, _) = Person(stateCode: 0);
        var marked = _h.Time.Now.AddDays(-1);
        _h.Identities.Contacts[contact].RemovedOn = marked;
        var grant = GrantOnMatter(contact);

        var result = await RunAsync(linkWrites: true, sweepWrites: true);

        _h.Grants.Rows.Single(r => r.Id == grant.Id).IsActive.Should().BeFalse("the removal ran again and revoked it");
        _h.Identities.Contacts[contact].RemovedOn.Should().Be(marked, "the existing marker is kept");
        _h.Identities.Contacts[contact].StateCode.Should().Be(0, "the sweep never changes the statecode");
        Sweep(result).GetProperty("contacts").EnumerateArray().Single().GetProperty("action").GetString()
            .Should().Be(nameof(RemovalSweepAction.RerunRemoval));
    }

    [Fact]
    public async Task AMarkedContact_ThatIsClean_IsUntouched_EvenWhenTheScanReturnsIt()
    {
        var (contact, _) = Person(stateCode: 1);
        _h.Identities.Contacts[contact].RemovedOn = _h.Time.Now.AddDays(-1);
        var revoked = GrantOnMatter(contact);
        _h.Grants.DeactivateOutOfBand(revoked.Id);

        var result = await RunAsync(linkWrites: true, sweepWrites: true, looseScan: true);

        Sweep(result).GetProperty("candidates").GetInt32().Should().Be(1, "control: the loose scan returned the contact");
        _h.Identities.Writes.Should().BeEmpty();
        _h.Grants.WriteCount.Should().Be(0);
        SweepAudit.Should().BeEmpty();
    }

    // ── Reinstate option (b): only an active contact is reinstated, so a sweep does not undo it ─────────────────────

    private Task<Microsoft.AspNetCore.Http.IResult> Reinstate(Guid contactId)
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { TraceIdentifier = "trace-c4-reinstate" };
        Sprk.Bff.Api.Api.Filters.ExternalAccessAdministratorAuthorizationFilter.SetDecision(http,
            new AdministratorAuthorityDecision(Guid.NewGuid(), AdministratorAuthorityLeg.Direct, AdministratorAuthorityReason.Granted));
        return PersonAccessAdministrationEndpoints.ReinstatePersonAsync(
            new ExternalPersonRequest(contactId), _h.Grants, _h.Participations, _h.Identities, Mock.Of<IMembershipCacheInvalidator>(),
            _h.Time, http, NullLogger<Program>.Instance, _audit.Factory, CancellationToken.None);
    }

    [Fact]
    public async Task ASweepAfterAnActiveReinstatement_LeavesTheContactUnmarked()
    {
        var (contact, _) = Person(stateCode: 0);
        _h.Identities.Contacts[contact].RemovedOn = _h.Time.Now.AddDays(-1);
        var revoked = GrantOnMatter(contact);
        _h.Grants.DeactivateOutOfBand(revoked.Id);

        (await Reinstate(contact)).Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ReinstatePersonResponse>>();
        _h.Identities.Contacts[contact].RemovedOn.Should().BeNull("control: the reinstatement cleared the marker");
        var writesAfterReinstate = _h.Identities.Writes.Count;

        var result = await RunAsync(linkWrites: true, sweepWrites: true, looseScan: true);

        Sweep(result).GetProperty("candidates").GetInt32().Should().Be(1, "control: the loose scan returned the contact");
        _h.Identities.Contacts[contact].RemovedOn.Should().BeNull("an active reinstated contact is not marked again");
        _h.Identities.Writes.Count.Should().Be(writesAfterReinstate);
        SweepAudit.Should().BeEmpty();
    }

    /// <summary>
    /// The documented residual: a contact deactivated AFTER its reinstatement and before the next sweep is marked again by that
    /// sweep. Intended: that is a fresh removal, not a defect.
    /// </summary>
    [Fact]
    public async Task AContactDeactivatedAfterItsReinstatement_IsMarkedAgainByTheNextSweep_AsAFreshRemoval()
    {
        var (contact, _) = Person(stateCode: 0);
        _h.Identities.Contacts[contact].RemovedOn = _h.Time.Now.AddDays(-1);
        (await Reinstate(contact)).Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ReinstatePersonResponse>>();
        _h.Identities.Contacts[contact].StateCode = 1;

        await RunAsync(linkWrites: true, sweepWrites: true);

        _h.Identities.Contacts[contact].RemovedOn.Should().Be(_h.Time.Now, "the sweep's fresh removal marks it again");
        SweepAudit.Should().ContainSingle();
    }

    // ── The cap ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCap_IsHonoured_AndTheRestWaitForTheNextRun()
    {
        var people = Enumerable.Range(0, 3).Select(_ => Person(stateCode: 1).ContactId).ToList();

        var first = await RunAsync(linkWrites: true, sweepWrites: true, cap: 2);

        people.Count(p => _h.Identities.Contacts[p].RemovedOn is not null).Should().Be(2);
        Sweep(first).GetProperty("deferredByCap").GetInt32().Should().Be(1);
        Sweep(first).GetProperty("maxContactsPerRun").GetInt32().Should().Be(2);

        await RunAsync(linkWrites: true, sweepWrites: true, cap: 2);

        people.Should().OnlyContain(p => _h.Identities.Contacts[p].RemovedOn != null);
    }

    [Theory]
    [InlineData(null, ExternalAccessRemovalSweep.DefaultMaxContactsPerRun)]
    [InlineData("0", ExternalAccessRemovalSweep.DefaultMaxContactsPerRun)]
    [InlineData("-3", ExternalAccessRemovalSweep.DefaultMaxContactsPerRun)]
    [InlineData("many", ExternalAccessRemovalSweep.DefaultMaxContactsPerRun)]
    [InlineData("7", 7)]
    [InlineData("100000", ExternalAccessRemovalSweep.MaxCandidatesPerQuery)]
    public void TheCap_DefaultsTo25_AndIsBounded(string? value, int expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [ExternalAccessRemovalSweep.MaxContactsPerRunConfigKey] = value }).Build();

        ExternalAccessRemovalSweep.MaxContactsPerRun(configuration).Should().Be(expected);
        ExternalAccessRemovalSweep.DefaultMaxContactsPerRun.Should().Be(25);
    }

    // ── Pure: the decision ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, true, false, RemovedMarkerRead.NotRemoved, nameof(RemovalSweepAction.MarkAndRevoke))]
    [InlineData(1, false, true, RemovedMarkerRead.NotRemoved, nameof(RemovalSweepAction.MarkAndRevoke))]
    [InlineData(1, false, false, RemovedMarkerRead.NotRemoved, nameof(RemovalSweepAction.None))]
    [InlineData(0, true, true, RemovedMarkerRead.NotRemoved, nameof(RemovalSweepAction.None))]
    [InlineData(null, true, true, RemovedMarkerRead.NotRemoved, nameof(RemovalSweepAction.None))]
    [InlineData(1, true, true, RemovedMarkerRead.Unreadable, nameof(RemovalSweepAction.SkipMarkerUnproven))]
    [InlineData(0, true, true, RemovedMarkerRead.Removed, nameof(RemovalSweepAction.RerunRemoval))]
    [InlineData(1, true, false, RemovedMarkerRead.Removed, nameof(RemovalSweepAction.None))]
    public void TheDecision_IsTheClosedSelection(
        int? stateCode, bool binding, bool grant, RemovedMarkerRead marker, string expected)
        => ExternalAccessRemovalSweep.Decide(stateCode, binding, grant, marker).ToString().Should().Be(expected);

    [Fact]
    public void TheCandidateQueries_SelectInactiveBoundUnmarked_AndActiveGrantsOfInactiveOrMarkedContacts()
    {
        ExternalAccessRemovalSweep.InactiveBoundUnmarkedFilter.Should().Be(
            "statecode eq 1 and sprk_externalobjectid ne null and sprk_externalaccessremovedon eq null");
        ExternalAccessRemovalSweep.ActiveGrantsOfInactiveOrMarkedContactsFilter.Should().Be(
            "statecode eq 0 and _sprk_contact_value ne null and "
            + "(sprk_Contact/statecode eq 1 or sprk_Contact/sprk_externalaccessremovedon ne null)");
    }
}

/// <summary>
/// C4: the Dataverse client the removal sweep reads through. It answers the sweep's two candidate queries from the identity
/// store and the grant table (interpreting the production filters), and forwards every other call to the grant table.
/// <see cref="LooseCandidateScan"/> returns every contact and every active person grant instead, so a test can show that the
/// live decision, not the query, decides.
/// </summary>
internal sealed class RemovalSweepWorld : DataverseWebApiClient
{
    private readonly GrantTable _grants;
    private readonly InMemoryContactIdentityStore _identities;

    public RemovalSweepWorld(GrantTable grants, InMemoryContactIdentityStore identities)
        : base(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                .Build(),
            NullLogger<DataverseWebApiClient>.Instance,
            Mock.Of<Azure.Core.TokenCredential>())
    {
        _grants = grants;
        _identities = identities;
    }

    public bool LooseCandidateScan { get; set; }

    /// <summary>Grant-row updates sent before the first removed-marker write (the order the removal must keep).</summary>
    public int GrantUpdatesBeforeMarker { get; private set; }

    public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
        int? top = null, int? skip = null, CancellationToken cancellationToken = default)
    {
        if (entitySetName == "contacts" && filter == ExternalAccessRemovalSweep.InactiveBoundUnmarkedFilter)
        {
            var rows = _identities.Contacts.Values
                .Where(c => LooseCandidateScan || (c.StateCode == 1 && c.Oid is not null && c.RemovedOn is null))
                .Select(c => new Dictionary<string, object?> { ["contactid"] = c.Id })
                .ToList();
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }

        if (entitySetName == ExternalGrantLifecycle.EntitySet && filter == ExternalAccessRemovalSweep.ActiveGrantsOfInactiveOrMarkedContactsFilter)
        {
            var rows = _grants.Rows
                .Where(r => r.IsActive && r.ContactId is { } id && _identities.Contacts.TryGetValue(id, out var c)
                            && (LooseCandidateScan || c.StateCode == 1 || c.RemovedOn is not null))
                .Select(r => new Dictionary<string, object?> { ["sprk_externalrecordaccessid"] = r.Id, ["_sprk_contact_value"] = r.ContactId })
                .ToList();
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }

        return _grants.QueryAsync<T>(entitySetName, filter, select, top, skip, cancellationToken);
    }

    public override Task<T?> RetrieveAsync<T>(string entitySetName, Guid id, string? select = null,
        CancellationToken cancellationToken = default) where T : default
        => _grants.RetrieveAsync<T>(entitySetName, id, select, cancellationToken);

    public override Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
        => _grants.CreateAsync(entitySetName, entity, cancellationToken);

    public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
    {
        if (!_identities.Writes.Any(w => w.Op == "removed-marker"))
            GrantUpdatesBeforeMarker++;
        return _grants.UpdateAsync(entitySetName, id, entity, cancellationToken);
    }
}
