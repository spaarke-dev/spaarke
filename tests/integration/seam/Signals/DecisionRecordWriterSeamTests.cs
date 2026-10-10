using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
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
        return new DecisionRecordWriter(client, ownership, TimeProvider.System, NullLogger<DecisionRecordWriter>.Instance);
    }

    private static DecisionRecordRequest Request(DecisionRecordCore? core, Guid? itemOwner) => new(
        Guid.NewGuid(), PolicyVersionId, core, itemOwner, TestUser1, null,
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
        row.GetAttributeValue<EntityReference>("owningteam").Should().BeNull("an ordinary matter keeps the writer's ownership, not a team");

        // Append-only by privilege: the writer principal has no Write on the table.
        var update = async () => await _dv.WriterClient.UpdateAsync(
            new Entity("sprk_decisionrecord", result.RecordId) { ["sprk_reason"] = "zz-040-seam tampered" });
        await update.Should().ThrowAsync<Exception>("the writer holds no Write on sprk_decisionrecord");
    }

    [Fact]
    public async Task WriteAsync_NoCore_AsTheWriter_IsOwnedByTheItemOwner_WithNullCoreColumns()
    {
        if (!_dv.IsLive) return;

        var req = Request(null, TestUser1) with
        {
            Steps = [new("mark-complete", true, new Dictionary<string, object?>(), "Done")],
            FollowOns = [],
        };
        var result = await BuildWriter().WriteAsync(req);

        var row = await _dv.OperatorClient.RetrieveAsync("sprk_decisionrecord", result.RecordId, new ColumnSet(Columns));
        result.RecordClass.Should().Be(RecordedDecisionClass.Routine);
        row.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be(100000001);
        row.GetAttributeValue<EntityReference>("owninguser").Id.Should().Be(TestUser1, "D-39: owned by the item's owner, set in the create");
        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Should().BeNull();
        row.GetAttributeValue<string>("sprk_corerecordid").Should().BeNull();
        row.GetAttributeValue<EntityReference>("sprk_matter").Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_Dismissal_PersistsClassDismissalAndOutcomeDismissed()
    {
        if (!_dv.IsLive) return;

        var req = Request(null, TestUser1) with
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

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Sweeps by the <c>zz-040-</c> marker in <c>sprk_reason</c>, with the operator client (the writer cannot delete).</summary>
    public async Task DisposeAsync()
    {
        if (!_dv.IsLive) return;

        try
        {
            var query = new QueryExpression("sprk_decisionrecord") { ColumnSet = new ColumnSet("sprk_decisionrecordid") };
            query.Criteria.AddCondition("sprk_reason", ConditionOperator.BeginsWith, "zz-040-");
            foreach (var row in (await _dv.OperatorClient.RetrieveMultipleAsync(query)).Entities)
            {
                try { await _dv.OperatorClient.DeleteAsync("sprk_decisionrecord", row.Id); }
                catch { /* best effort: must not mask the assertion failure */ }
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
