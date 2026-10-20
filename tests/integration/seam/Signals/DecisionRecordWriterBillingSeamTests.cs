using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// ebilling-r1 task 074 (D-100): real-Dataverse seam for <see cref="DecisionRecordWriter.WriteBillingDecisionAsync"/>, the ADR-038 pairing for
/// <c>DecisionRecordWriterTests.Billing</c>. Writes ONE billing record about a throwaway invoice filed under a matter AS THE WRITER principal,
/// reads it back with the operator client, proves a repeat finds it through the live <c>createdby</c> lookup, then deletes both rows.
/// </summary>
/// <remarks>
/// Opt-in exactly like <see cref="DecisionRecordWriterSeamTests"/> (same fixture, same environment variables); unless
/// <see cref="SignalPredicateTests.UrlEnvVar"/> is set the test returns at once. Rows carry the <c>zz-074-</c> prefix (the invoice's name and
/// the record's <c>sprk_reason</c>); <see cref="DisposeAsync"/> sweeps by it, pass or fail.
/// </remarks>
[Trait("status", "repaired")]
[Trait("Category", "Live")]
public sealed class DecisionRecordWriterBillingSeamTests : IClassFixture<SignalWriterSeamTests.LiveDataverse>, IAsyncLifetime
{
    private const string Marker = "zz-074-seam";

    private static readonly Guid PositiveMatter = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid MatterTypeRef = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");   // sprk_recordtype_ref: sprk_matter
    private static readonly Guid TestUser1 = Guid.Parse("8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c");

    private readonly SignalWriterSeamTests.LiveDataverse _dv;

    public DecisionRecordWriterBillingSeamTests(SignalWriterSeamTests.LiveDataverse dv) => _dv = dv;

    private DecisionRecordWriter BuildWriter()
    {
        var client = new OntologyWriterDataverseClient(() => _dv.WriterClient, NullLogger<OntologyWriterDataverseClient>.Instance);
        // The BFF's sysadmin seam: reads over the NON-impersonated operator connection; this test never writes through it.
        var sysadmin = new Mock<IGenericEntityService>(MockBehavior.Strict);
        sysadmin.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken ct) => _dv.OperatorClient.RetrieveAsync(table, id, new ColumnSet(columns), ct));
        sysadmin.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken ct) => _dv.OperatorClient.RetrieveMultipleAsync(query, ct));
        sysadmin.Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression fetch, CancellationToken ct) => _dv.OperatorClient.RetrieveMultipleAsync(fetch, ct));
        var ownership = new RecordOwnershipResolver(sysadmin.Object, new ConfigurationBuilder().Build(), NullLogger<RecordOwnershipResolver>.Instance);
        var coreAncestors = new CoreAncestorResolver(
            sysadmin.Object,
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
            client, ownership, coreAncestors, sysadmin.Object, recordTypes, TimeProvider.System, NullLogger<DecisionRecordWriter>.Instance,
            new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
    }

    [Fact]
    public async Task WriteBillingDecisionAsync_AnInvoiceApproval_AsTheWriter_IsFiledUnderTheInvoice_AndARepeatFindsIt()
    {
        if (!_dv.IsLive) return;

        var invoice = await _dv.OperatorClient.CreateAsync(new Entity("sprk_invoice")
        {
            ["sprk_name"] = $"{Marker}-invoice",
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", MatterTypeRef),
            ["sprk_matter"] = new EntityReference("sprk_matter", PositiveMatter),
        });
        var request = new BillingDecisionRecordRequest(
            Guid.NewGuid(), "approve-invoice", new DecisionRecordRef("sprk_invoice", invoice), TestUser1, "Billing reviewer",
            new Dictionary<string, object?> { ["note"] = "zz-074" }, "Approved",
            new Dictionary<string, object?> { ["billedTotal"] = 1200m, ["approvedTotal"] = 1200m }, $"{Marker} approve");
        var writer = BuildWriter();

        var first = await writer.WriteBillingDecisionAsync(request);
        var repeat = await writer.WriteBillingDecisionAsync(request);

        repeat.AlreadyRecorded.Should().BeTrue("the live lookup finds the writer's own record by key, code and createdby");
        repeat.RecordId.Should().Be(first.RecordId);
        var row = await _dv.OperatorClient.RetrieveAsync("sprk_decisionrecord", first.RecordId, new ColumnSet(
            "sprk_regardinginvoice", "sprk_matter", "sprk_corerecordid", "sprk_actioncode", "sprk_confirmedby", "sprk_gatetier",
            "sprk_recordclass", "sprk_decisionoutcome", "sprk_policyversion", "owningteam"));
        row.GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(invoice);
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(PositiveMatter, "the invoice's own core");
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(PositiveMatter.ToString("D"));
        row.GetAttributeValue<string>("sprk_actioncode").Should().Be("approve-invoice");
        row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(TestUser1);
        row.GetAttributeValue<string>("sprk_gatetier").Should().Be("Tier4");
        row.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be(100000000, "Judgement");
        row.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome").Value.Should().Be(100000000, "Authorized");
        row.GetAttributeValue<EntityReference>("sprk_policyversion").Should().BeNull("a clean approval has none");
        row.GetAttributeValue<EntityReference>("owningteam").Should().NotBeNull("D-129: team-owned, set in the create");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Sweeps the records by the <c>zz-074-</c> reason and the invoices by name, with the operator client (the writer cannot delete).</summary>
    public async Task DisposeAsync()
    {
        if (!_dv.IsLive) return;

        foreach (var (table, column) in new[] { ("sprk_decisionrecord", "sprk_reason"), ("sprk_invoice", "sprk_name") })
        {
            try
            {
                var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false) };
                query.Criteria.AddCondition(column, ConditionOperator.BeginsWith, "zz-074-");
                foreach (var row in (await _dv.OperatorClient.RetrieveMultipleAsync(query)).Entities)
                {
                    try { await _dv.OperatorClient.DeleteAsync(table, row.Id); }
                    catch { /* best effort: must not mask the assertion failure */ }
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
