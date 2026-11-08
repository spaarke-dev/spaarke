using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Real-Dataverse seam for the spend reads the Signal evaluator, the rollup and the tool send (<see cref="FinanceBaseValues.ReadAsync"/>,
/// <see cref="FinanceBaseValues.ReadCountedInvoicesAsync"/>, <see cref="FinanceBaseValues.ReadPendingSpendAsync"/>; task 025b, owner decision
/// D-122; task 165b-4 replaced the grouped spend aggregates with one paged row read filtered by the approve Decision Record and the hold). The
/// unit tests feed the readers an in-memory world; only Dataverse can say that these queries are accepted, that the counted-status filter
/// selects real <c>statuscode</c> values (ebilling-r1 task 015, owner decision D-103: Approved, Sent to AP and Paid, Paid being Inactive; In
/// Review is not counted) and that the budget aggregate comes back in the shape the reader expects.
/// </summary>
/// <remarks>
/// <para>Opt-in exactly like <see cref="SignalWriterSeamTests"/> (same fixture: <c>AzureCliCredential</c>): unless
/// <see cref="SignalPredicateTests.UrlEnvVar"/> is set every test returns at once. The queries are sent with the OPERATOR client, the
/// unimpersonated connection that stands in for the BFF's app-only sysadmin seam, which is what the readers read through.</para>
/// <para><b>Test rows.</b> The test creates its own matter, project, invoices and budgets, all named with the <c>zz-025b-</c>
/// prefix (it does not depend on any dev data), and removes them in <c>finally</c> and again in <see cref="DisposeAsync"/>
/// (children first). Afterwards <c>sprk_matter.sprk_mattername</c>, <c>sprk_project.sprk_projectname</c>,
/// <c>sprk_invoice.sprk_name</c> and <c>sprk_budget.sprk_name</c> beginning <c>zz-025b-</c> must return 0 rows. It creates no Decision
/// Record (the ledger is append-only), so none of its invoices has an approve record and none counts as spend: that is the 165b-4 rule.</para>
/// </remarks>
[Trait("status", "new")]
[Trait("Category", "Live")]
public sealed class FinanceBaseValuesSeamTests : IClassFixture<SignalWriterSeamTests.LiveDataverse>, IAsyncLifetime
{
    private const string Prefix = "zz-025b-";

    private static readonly Guid MatterTypeRef = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");   // sprk_recordtype_ref: sprk_matter
    private static readonly Guid ProjectTypeRef = Guid.Parse("ca68b3bb-8600-f111-8407-7c1e520aa4df");  // sprk_recordtype_ref: sprk_project

    private readonly SignalWriterSeamTests.LiveDataverse _dv;

    public FinanceBaseValuesSeamTests(SignalWriterSeamTests.LiveDataverse dv) => _dv = dv;

    [Fact]
    public async Task RowReads_OnRealRows_SelectOnlyTheCountedStatuscodes_CountNothingWithoutAnApproveRecord_SumBudgets_AndKeepMatterAndProjectApart()
    {
        if (!_dv.IsLive) return;

        var run = Guid.NewGuid().ToString("N")[..8];
        var op = _dv.OperatorClient;
        var matter = await op.CreateAsync(new Entity("sprk_matter") { ["sprk_mattername"] = $"{Prefix}{run}-matter" });
        var emptyMatter = await op.CreateAsync(new Entity("sprk_matter") { ["sprk_mattername"] = $"{Prefix}{run}-empty" });
        var project = await op.CreateAsync(new Entity("sprk_project")
        {
            ["sprk_projectname"] = $"{Prefix}{run}-project",
            ["sprk_projectnumber"] = $"{Prefix}{run}",
        });

        try
        {
            // Matter (D-103): Approved 120 + Sent to AP 60 + Paid (Inactive) 20 are in the counted statuses; In Review 300 is pending;
            // Rejected 9000, Returned to Firm 8000 and Cancelled 5000 (all Inactive) are in neither.
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 300m, InvoiceLifecycleStatus.InReview);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 120m, InvoiceLifecycleStatus.Approved);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 60m, InvoiceLifecycleStatus.SentToAp);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 20m, InvoiceLifecycleStatus.Paid);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 9000m, InvoiceLifecycleStatus.Rejected);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 8000m, InvoiceLifecycleStatus.ReturnedToFirm);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 5000m, InvoiceLifecycleStatus.Cancelled);
            await BudgetAsync(run, "sprk_matter", matter, 1000m);
            await BudgetAsync(run, "sprk_matter", matter, 500m);

            // Project: its own rows only; the matter's must not leak in.
            await InvoiceAsync(run, "sprk_project", project, ProjectTypeRef, 70m, InvoiceLifecycleStatus.Approved);
            await BudgetAsync(run, "sprk_project", project, 400m);

            var entities = new SignalWriterSeamTests.SysadminAdapter(op);
            var writer = ((WhoAmIResponse)await op.ExecuteAsync(new WhoAmIRequest())).UserId;

            // The counted row read (read 1, the snapshot) selects exactly the counted statuscodes on the real rows.
            var counted = (await op.RetrieveMultipleAsync(FinanceBaseValues.InvoiceRowQuery(
                FinanceBaseValues.MatterEntity, FinanceBaseValues.CountedInvoiceStatuses, [matter, emptyMatter, project]))).Entities;
            counted.Select(FinanceBaseValues.SpendAmount).Should().BeEquivalentTo(new[] { 120m, 60m, 20m },
                "Approved, Sent to AP and Paid are selected; In Review, Rejected, Returned to Firm and Cancelled are not");
            var pendingRows = (await op.RetrieveMultipleAsync(FinanceBaseValues.InvoiceRowQuery(
                FinanceBaseValues.MatterEntity, FinanceBaseValues.PendingInvoiceStatuses, [matter]))).Entities;
            pendingRows.Select(FinanceBaseValues.SpendAmount).Should().Equal(300m);

            // No approve record exists for any of them, so none is counted spend (165b-4); budgets still sum.
            (await FinanceBaseValues.ReadCountedInvoicesAsync(entities, FinanceBaseValues.MatterEntity, [matter], writer, CancellationToken.None))
                .Should().BeEmpty("an invoice in a counted status with no approve Decision Record from the writer is not counted");
            var matters = await FinanceBaseValues.ReadAsync(
                entities, FinanceBaseValues.MatterEntity, new[] { matter, emptyMatter, project }, needSpend: true, needBudget: true, writer, CancellationToken.None);
            matters[matter].Should().Be((0m, 1500m), "no approve record, so no counted spend; budgets 1000 + 500");
            matters[emptyMatter].Should().Be((0m, 0m), "a matter with no rows has a sum of nothing");
            matters[project].Should().Be((0m, 0m), "a project id is not a matter id: the query groups on the matter lookup");

            var projects = await FinanceBaseValues.ReadAsync(
                entities, FinanceBaseValues.ProjectEntity, new[] { project, matter }, needSpend: true, needBudget: true, writer, CancellationToken.None);
            projects[project].Should().Be((0m, 400m));
            projects[matter].Should().Be((0m, 0m), "the matter's budgets are not the project's");

            // Pending: the In Review invoice has no linked submission, so it is not held and counts as pending.
            var pending = await FinanceBaseValues.ReadPendingSpendAsync(entities, FinanceBaseValues.MatterEntity, [matter, project], CancellationToken.None);
            pending[matter].Should().Be(300m);
            pending[project].Should().Be(0m);
        }
        finally
        {
            await SweepAsync();
        }
    }

    /// <summary>
    /// READ-ONLY (049, ontology-r1 review K1; D-130; task 165b-4): Dataverse accepts every query shape the spend readers send - the counted
    /// and pending row reads, the parent-scoped approve-record match (portions and records, each with an inner link to the invoice), the
    /// hold's batched submission read and the budget aggregate - and the readers read the result. It creates nothing: the ids belong to
    /// no row, so each query must run and return no rows (0, never a throw). The semantics are pinned by the unit tests; this proves only
    /// that the shapes are accepted.
    /// </summary>
    [Fact]
    public async Task SpendQueries_AreAcceptedByDataverse_ReadOnly()
    {
        if (!_dv.IsLive) return;

        var op = _dv.OperatorClient;
        var nobody = Guid.NewGuid();
        var entities = new SignalWriterSeamTests.SysadminAdapter(op);
        var writer = ((WhoAmIResponse)await op.ExecuteAsync(new WhoAmIRequest())).UserId;

        foreach (var parent in new[] { FinanceBaseValues.MatterEntity, FinanceBaseValues.ProjectEntity })
        {
            var pending = await FinanceBaseValues.ReadPendingSpendAsync(entities, parent, new[] { nobody }, CancellationToken.None);
            pending[nobody].Should().Be(0m, $"the pending row read and the hold batch on {parent} are accepted and an id with no invoices sums to 0");

            var counted = await FinanceBaseValues.ReadAsync(entities, parent, new[] { nobody }, needSpend: true, needBudget: true, writer, CancellationToken.None);
            counted[nobody].Should().Be((0m, 0m), $"the counted row read and the budget query on {parent} are accepted");

            // The snapshot is empty for an unknown parent, so the readers skip reads 2 and 3; send them directly with a made-up snapshot.
            var matched = await InvoiceApproveRecord.MatchedInvoicesAsync(entities, parent, [nobody], FinanceBaseValues.CountedInvoiceStatuses,
                new HashSet<Guid> { Guid.NewGuid() }, writer, CancellationToken.None);
            matched.Should().BeEmpty($"the parent-scoped portion and approve-record reads on {parent} are accepted");

            var verdicts = await InvoiceBlockedHold.CheckBatchAsync(entities, parent, [nobody], FinanceBaseValues.PendingInvoiceStatuses,
                [Guid.NewGuid()], CancellationToken.None);
            verdicts.Values.Should().OnlyContain(v => !v.Held, $"the hold's batched submission read on {parent} is accepted");
        }
    }

    /// <summary>Creates a test invoice (it starts In Review, the statuscode default) and, for any other status, moves it there
    /// with the statecode / statuscode pair Dataverse requires. A direct write is fine for these throwaway rows; product code moves
    /// an invoice only through <see cref="InvoiceLifecycleService"/>.</summary>
    private async Task InvoiceAsync(string run, string parentEntity, Guid parentId, Guid typeRef, decimal amount, InvoiceLifecycleStatus status)
    {
        var invoice = new Entity("sprk_invoice")
        {
            ["sprk_name"] = $"{Prefix}{run}-invoice",
            ["sprk_totalamount"] = new Money(amount),
            ["sprk_extractionstatus"] = new OptionSetValue(100000000),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", typeRef),
            [parentEntity] = new EntityReference(parentEntity, parentId),
        };
        var id = await _dv.OperatorClient.CreateAsync(invoice);
        if (status != InvoiceLifecycleStatus.InReview)
        {
            await _dv.OperatorClient.UpdateAsync(new Entity("sprk_invoice", id)
            {
                ["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateCodeOf(status)),
                ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.StatusCodeOf(status)),
            });
        }
    }

    private async Task BudgetAsync(string run, string parentEntity, Guid parentId, decimal total) =>
        await _dv.OperatorClient.CreateAsync(new Entity("sprk_budget")
        {
            ["sprk_name"] = $"{Prefix}{run}-budget",
            ["sprk_totalbudget"] = new Money(total),
            ["sprk_currency"] = "USD",
            [parentEntity] = new EntityReference(parentEntity, parentId),
        });

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _dv.IsLive ? SweepAsync() : Task.CompletedTask;

    /// <summary>Deletes every <c>zz-025b-</c> row, children before parents, with the operator client. Best effort: it must not mask a failed assertion.</summary>
    private async Task SweepAsync()
    {
        foreach (var (entity, nameColumn) in new[]
        {
            ("sprk_invoice", "sprk_name"), ("sprk_budget", "sprk_name"), ("sprk_matter", "sprk_mattername"), ("sprk_project", "sprk_projectname"),
        })
        {
            try
            {
                var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(false) };
                query.Criteria.AddCondition(nameColumn, ConditionOperator.BeginsWith, Prefix);
                foreach (var row in (await _dv.OperatorClient.RetrieveMultipleAsync(query)).Entities)
                {
                    try { await _dv.OperatorClient.DeleteAsync(entity, row.Id); }
                    catch { /* best effort */ }
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
