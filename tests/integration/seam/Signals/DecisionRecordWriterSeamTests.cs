using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Real-Dataverse seam for <see cref="DecisionRecordWriter"/> (task 040; ADR-038 pairing for the unit tests' column, option-value
/// and lookup assertions). Creates rows in <c>spaarkedev1</c> AS THE WRITER principal (impersonated by <c>MSCRMCallerID</c>, the
/// same fixture and convention as <see cref="SignalWriterSeamTests"/>), reads them back with the operator client, then deletes
/// them with the operator client (the writer holds no Delete, by design).
/// </summary>
/// <remarks>
/// Opt-in exactly like <see cref="SignalWriterSeamTests"/>: unless <see cref="SignalPredicateTests.UrlEnvVar"/> is set every test
/// returns at once. Set <see cref="SignalPredicateTests.CallerIdEnvVar"/> to the writer (3121bf1b-9fbf-f111-aaaf-0022482913fc).
/// <b>Test rows:</b> the record name is derived from the catalog, so the <c>zz-040-</c> prefix is carried in <c>sprk_reason</c>
/// (every record here sets it) and <see cref="DisposeAsync"/> sweeps by it, pass or fail. Run
/// <c>SELECT COUNT(*) FROM sprk_decisionrecord WHERE sprk_reason LIKE 'zz-040-%'</c> afterwards to confirm 0 left.
/// </remarks>
[Trait("status", "repaired")]
[Trait("Category", "Live")]
public sealed class DecisionRecordWriterSeamTests : IClassFixture<SignalWriterSeamTests.LiveDataverse>, IAsyncLifetime
{
    private const string Marker = "zz-040-seam";

    private static readonly Guid PolicyVersionId = Guid.Parse("42b3e716-61bf-f111-aaaf-0022482913fc");
    private static readonly Guid PositiveMatter = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid MatterTypeRef = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");   // sprk_recordtype_ref: sprk_matter
    private static readonly Guid TestUser1 = Guid.Parse("8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c");
    private static readonly Guid Signal = Guid.NewGuid();

    private readonly SignalWriterSeamTests.LiveDataverse _dv;

    public DecisionRecordWriterSeamTests(SignalWriterSeamTests.LiveDataverse dv) => _dv = dv;

    private DecisionRecordWriter BuildWriter()
    {
        var client = new OntologyWriterDataverseClient(() => _dv.WriterClient, NullLogger<OntologyWriterDataverseClient>.Instance);
        var sysadmin = new OperatorAdapter(_dv.OperatorClient);
        var ownership = new RecordOwnershipResolver(sysadmin, new ConfigurationBuilder().Build(), NullLogger<RecordOwnershipResolver>.Instance);
        // Task 142: uac-r2's REAL core-ancestor resolver (every stamp column exists on sprk_decisionrecord live, task 140) and the
        // shared regarding builder typing the pair from the LIVE sprk_recordtype_ref catalog (the production query's filter).
        var coreAncestors = new CoreAncestorResolver(
            sysadmin,
            (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase)),
            NullLogger<CoreAncestorResolver>.Instance);
        var recordTypes = new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(async (name, ct) =>
        {
            var query = new QueryExpression("sprk_recordtype_ref")
            {
                ColumnSet = new ColumnSet("sprk_recordtype_refid", "sprk_recorddisplayname"),
                TopCount = 1,
            };
            query.Criteria.AddCondition("sprk_recordlogicalname", ConditionOperator.Equal, name);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return (await _dv.OperatorClient.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault();
        });
        return new DecisionRecordWriter(
            client, ownership, coreAncestors, sysadmin, recordTypes, TimeProvider.System, NullLogger<DecisionRecordWriter>.Instance,
            new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
    }

    /// <summary>Name prefix of the To Do the no-core test creates as its subject; <see cref="DisposeAsync"/> sweeps by it.</summary>
    private const string TodoMarker = "zz-043-nocore-subject";

    private readonly List<Guid> _todos = [];

    private static DecisionRecordRequest Request(DecisionRecordCore? core, DecisionRecordRef? subject) => new(
        Guid.NewGuid(), PolicyVersionId, core, subject, TestUser1, null,
        [
            new("send-budget-inquiry", true, new Dictionary<string, object?> { ["to"] = "zz-040@example.invalid" }, "Sent"),
            new("revise-budget", false, null, null),
        ],
        [new("add-todo", "sprk_todo", Guid.NewGuid())],
        "tier-1", [Signal],
        new Dictionary<string, object?> { ["variance"] = 12.5m, ["period"] = "2026-09" },
        "Partner", "zz-040 because", Marker);

    private static readonly string[] Columns =
    [
        "sprk_recordclass", "sprk_decisionoutcome", "sprk_actioncode", "sprk_action", "sprk_proposedaction", "sprk_policyversion",
        "sprk_confirmedby", "sprk_decidedon", "sprk_gatetier", "sprk_factsnapshot", "sprk_steps", "sprk_followons", "sprk_matter",
        "sprk_project", "sprk_workassignment", "sprk_corerecordtype", "sprk_corerecordid", "sprk_privilegeflagged", "sprk_reason",
        "ownerid", "owninguser", "owningteam",
    ];

    [Fact]
    public async Task WriteAsync_MatterCore_AsTheWriter_PersistsEveryColumn_AndCannotBeUpdatedByTheWriter()
    {
        if (!_dv.IsLive) return;

        var result = await BuildWriter().WriteAsync(Request(new DecisionRecordCore("sprk_matter", PositiveMatter, MatterTypeRef), null));
        var row = await _dv.OperatorClient.RetrieveAsync("sprk_decisionrecord", result.RecordId, new ColumnSet(Columns));
        result.RecordClass.Should().Be(RecordedDecisionClass.Judgement);
        row.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be(100000000);
        row.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value.Should().Be(100000000);
        row.GetAttributeValue<string>("sprk_actioncode").Should().Be("send-budget-inquiry");
        row.GetAttributeValue<EntityReference>("sprk_action").Should().BeNull();
        row.GetAttributeValue<string>("sprk_proposedaction").Should().Be("Send budget inquiry; Revise budget");
        row.GetAttributeValue<EntityReference>("sprk_policyversion").Id.Should().Be(PolicyVersionId);
        row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(TestUser1);
        row.GetAttributeValue<string>("sprk_gatetier").Should().Be("tier-1");
        row.GetAttributeValue<string>("sprk_factsnapshot").Should().Contain("2026-09").And.Contain("Partner");
        row.GetAttributeValue<string>("sprk_steps").Should().Contain("send-budget-inquiry").And.Contain("Skipped");
        row.GetAttributeValue<string>("sprk_followons").Should().Contain("add-todo");
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(PositiveMatter);
        row.GetAttributeValue<EntityReference>("sprk_project").Should().BeNull();
        row.GetAttributeValue<EntityReference>("sprk_workassignment").Should().BeNull();
        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(MatterTypeRef);
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(PositiveMatter.ToString("D"));
        row.GetAttributeValue<bool>("sprk_privilegeflagged").Should().BeFalse();
        row.GetAttributeValue<EntityReference>("owningteam").Should().NotBeNull("D-129: the record is owned by the team the resolver names for the matter, set in the create");
        row.GetAttributeValue<EntityReference>("owninguser").Should().BeNull("records are owned by teams, never a person");

        // Append-only by privilege: the writer principal has no Write on the table.
        var update = async () => await _dv.WriterClient.UpdateAsync(
            new Entity("sprk_decisionrecord", result.RecordId) { ["sprk_reason"] = "zz-040-seam tampered" });
        await update.Should().ThrowAsync<Exception>("the writer holds no Write on sprk_decisionrecord");
    }

    [Fact]
    public async Task WriteAsync_NoCore_AsTheWriter_IsOwnedByTheResolversTeam_WithNullCoreColumns()
    {
        if (!_dv.IsLive) return;

        // The subject is created here, with the operator client, so the test always runs when the fixture is live: it is the
        // no-core item's To Do, attached to the positive matter so the resolver has a parent to answer for. Swept in DisposeAsync.
        var todo = await _dv.OperatorClient.CreateAsync(new Entity("sprk_todo")
        {
            ["sprk_name"] = $"{TodoMarker} {Guid.NewGuid():N}",
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", PositiveMatter),
        });
        _todos.Add(todo);

        var req = Request(null, new DecisionRecordRef("sprk_todo", todo)) with
        {
            Steps = [new("mark-complete", true, new Dictionary<string, object?>(), "Done")],
            FollowOns = [],
        };
        var result = await BuildWriter().WriteAsync(req);

        var row = await _dv.OperatorClient.RetrieveAsync("sprk_decisionrecord", result.RecordId, new ColumnSet(Columns));
        result.RecordClass.Should().Be(RecordedDecisionClass.Routine);
        row.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be(100000001);
        row.GetAttributeValue<EntityReference>("owningteam").Should().NotBeNull("D-39 / D-129: owned by the team the resolver names for the subject, set in the create");
        row.GetAttributeValue<EntityReference>("owninguser").Should().BeNull("records are owned by teams, never a person");
        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Should().BeNull();
        row.GetAttributeValue<string>("sprk_corerecordid").Should().BeNull();
        row.GetAttributeValue<EntityReference>("sprk_matter").Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_Dismissal_PersistsClassDismissalAndOutcomeDismissed()
    {
        if (!_dv.IsLive) return;

        var req = Request(new DecisionRecordCore("sprk_matter", PositiveMatter, MatterTypeRef), null) with
        {
            Steps = [new("mark-complete", false, null, null)],
            FollowOns = [],
            GateTier = null,
            Reason = Marker + " - not-mine - detail",
        };
        var result = await BuildWriter().WriteAsync(req);

        var row = await _dv.OperatorClient.RetrieveAsync("sprk_decisionrecord", result.RecordId, new ColumnSet(Columns));
        row.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be(100000002);
        row.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value.Should().Be(100000002);
        row.GetAttributeValue<string>("sprk_actioncode").Should().BeNull();
        row.GetAttributeValue<string>("sprk_reason").Should().StartWith(Marker);
    }

    // ── Task 042 (FR-23): the communications gate's decision, authorize and deny, against the LIVE schema (opt-in like the rest) ──

    private const string GateMarker = "zz-042-seam";

    /// <summary>A real <c>sprk_communication</c> on spaarkedev1 filed under <see cref="PositiveMatter"/> (read-only reference: the test never writes it).</summary>
    private static readonly Guid FiledCommunication = Guid.Parse("f00b6389-8fbf-f111-aaaf-0022482913fc");

    private static GateDecisionRecordRequest GateRequest(bool authorize) => new(
        FiledCommunication, Guid.NewGuid(), authorize, $"{GateMarker} - {(authorize ? "rule-matched-confidence-met" : "rule-matched-confidence-below-threshold")}",
        "communication-ri-action", "Create a review task and notify the communication's owner",
        new Dictionary<string, object?> { ["communicationId"] = FiledCommunication, ["confidence"] = authorize ? 0.9 : 0.4, ["threshold"] = 0.7 },
        DirectFiledUnderId: PositiveMatter);

    [Fact]
    public async Task WriteGateDecisionAsync_BothOutcomes_AsTheWriter_PersistWithNullConfirmerAndPolicyVersion_AndAreQueryablePerMatter()
    {
        if (!_dv.IsLive) return;

        var writer = BuildWriter();
        var authorized = await writer.WriteGateDecisionAsync(GateRequest(authorize: true));
        var denied = await writer.WriteGateDecisionAsync(GateRequest(authorize: false));

        var columns = new ColumnSet(Columns.Concat(new[] { "sprk_gatesessionid", "sprk_regardingcommunication", "sprk_regardingmatter", "sprk_assignedtointernal" }).ToArray());
        var a = await _dv.OperatorClient.RetrieveAsync("sprk_decisionrecord", authorized.RecordId, columns);
        var d = await _dv.OperatorClient.RetrieveAsync("sprk_decisionrecord", denied.RecordId, columns);

        foreach (var row in new[] { a, d })
        {
            row.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be(100000000, "Judgement on both paths");
            row.GetAttributeValue<EntityReference>("sprk_confirmedby").Should().BeNull("no human decided; the column is nullable live");
            row.GetAttributeValue<EntityReference>("sprk_policyversion").Should().BeNull("the gate's rules are sprk_communicationrule rows");
            row.GetAttributeValue<string>("sprk_gatesessionid").Should().BeNull();
            row.GetAttributeValue<EntityReference>("sprk_action").Should().BeNull();
            row.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(FiledCommunication);
            row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(PositiveMatter);
            row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(PositiveMatter);
            row.GetAttributeValue<EntityReference>("owningteam").Should().NotBeNull("D-129: owned by a team");
            row.GetAttributeValue<EntityReference>("owninguser").Should().BeNull();
        }

        a.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value.Should().Be(100000000);
        a.GetAttributeValue<string>("sprk_actioncode").Should().Be("communication-ri-action");
        d.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value.Should().Be(100000001);
        d.GetAttributeValue<string>("sprk_actioncode").Should().BeNull();

        // Success criterion 3: FetchXML per matter over both outcomes returns both.
        var fetch = $@"<fetch><entity name='sprk_decisionrecord'><attribute name='sprk_decisionoutcome' />
            <filter type='and'><condition attribute='sprk_regardingmatter' operator='eq' value='{PositiveMatter}' />
            <condition attribute='sprk_reason' operator='begins-with' value='{GateMarker}' /></filter></entity></fetch>";
        var found = (await _dv.OperatorClient.RetrieveMultipleAsync(new FetchExpression(fetch))).Entities;
        found.Select(e => e.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value).Should().BeEquivalentTo(new[] { 100000000, 100000001 });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Sweeps by the <c>zz-040-</c> marker in <c>sprk_reason</c>, with the operator client (the writer cannot delete).</summary>
    public async Task DisposeAsync()
    {
        if (!_dv.IsLive) return;

        try
        {
            foreach (var prefix in new[] { "zz-040-", "zz-042-" })
            {
                var query = new QueryExpression("sprk_decisionrecord") { ColumnSet = new ColumnSet("sprk_decisionrecordid") };
                query.Criteria.AddCondition("sprk_reason", ConditionOperator.BeginsWith, prefix);
                foreach (var row in (await _dv.OperatorClient.RetrieveMultipleAsync(query)).Entities)
                {
                    try { await _dv.OperatorClient.DeleteAsync("sprk_decisionrecord", row.Id); }
                    catch { /* best effort: must not mask the assertion failure */ }
                }
            }
        }
        catch
        {
            // best effort
        }

        // The no-core test's To Do subjects (created by id above; the name prefix catches any that an aborted run left).
        foreach (var id in _todos)
        {
            try { await _dv.OperatorClient.DeleteAsync("sprk_todo", id); }
            catch { /* best effort */ }
        }

        try
        {
            var todos = new QueryExpression("sprk_todo") { ColumnSet = new ColumnSet("sprk_todoid") };
            todos.Criteria.AddCondition("sprk_name", ConditionOperator.BeginsWith, TodoMarker);
            foreach (var row in (await _dv.OperatorClient.RetrieveMultipleAsync(todos)).Entities)
            {
                try { await _dv.OperatorClient.DeleteAsync("sprk_todo", row.Id); }
                catch { /* best effort */ }
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>The reads the ownership resolver performs, over the NON-impersonated operator connection (as the BFF's sysadmin seam).</summary>
    private sealed class OperatorAdapter(ServiceClient op) : IGenericEntityService
    {
        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default) =>
            op.RetrieveAsync(entityLogicalName, id, new ColumnSet(columns), ct);
        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default) => op.RetrieveMultipleAsync(query, ct);
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default) => op.RetrieveMultipleAsync(fetch, ct);

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateIfRowVersionMatchesAsync(string entityLogicalName, Guid id, string rowVersion, Dictionary<string, object> fields, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default) => throw new NotImplementedException();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
