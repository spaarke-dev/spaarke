using FluentAssertions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Real-Dataverse seam for the grouped aggregate the Signal evaluator sends (<see cref="FinanceBaseValues.BuildGroupedSumFetchXml"/>,
/// read back by <see cref="FinanceBaseValues.ReadGroupedSums"/>; task 025b, owner decision D-122). The unit tests build the
/// FetchXML and feed the reader hand-made rows; only Dataverse can say that this query is accepted, that the alias and group-by
/// columns come back in the shape the reader expects, and that the counted-status filter selects real option values.
/// </summary>
/// <remarks>
/// <para>Opt-in exactly like <see cref="SignalWriterSeamTests"/> (same fixture: <c>AzureCliCredential</c>): unless
/// <see cref="SignalPredicateTests.UrlEnvVar"/> is set every test returns at once. The query is sent with the OPERATOR client, the
/// unimpersonated connection that stands in for the BFF's app-only sysadmin seam, which is what the evaluator reads through.</para>
/// <para><b>Test rows.</b> The test creates its own matter, project, invoices and budgets, all named with the <c>zz-025b-</c>
/// prefix (it does not depend on any dev data), and removes them in <c>finally</c> and again in <see cref="DisposeAsync"/>
/// (children first). Afterwards <c>sprk_matter.sprk_mattername</c>, <c>sprk_project.sprk_projectname</c>,
/// <c>sprk_invoice.sprk_name</c> and <c>sprk_budget.sprk_name</c> beginning <c>zz-025b-</c> must return 0 rows.</para>
/// </remarks>
[Trait("status", "new")]
[Trait("Category", "Live")]
public sealed class FinanceBaseValuesSeamTests : IClassFixture<SignalWriterSeamTests.LiveDataverse>, IAsyncLifetime
{
    private const string Prefix = "zz-025b-";

    private static readonly Guid MatterTypeRef = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");   // sprk_recordtype_ref: sprk_matter
    private static readonly Guid ProjectTypeRef = Guid.Parse("ca68b3bb-8600-f111-8407-7c1e520aa4df");  // sprk_recordtype_ref: sprk_project

    private const int ToReview = 100000000;   // sprk_invoicestatus (live option set: exactly ToReview and Reviewed)

    private readonly SignalWriterSeamTests.LiveDataverse _dv;

    public FinanceBaseValuesSeamTests(SignalWriterSeamTests.LiveDataverse dv) => _dv = dv;

    [Fact]
    public async Task GroupedSum_OnRealRows_CountsOnlyReviewedInvoices_SumsBudgets_AndKeepsMatterAndProjectApart()
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
            // Matter: 300 + 200 Reviewed count; the ToReview 9000 and the invoice with NO status 5000 do not.
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 300m, FinanceBaseValues.InvoiceStatusReviewed);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 200m, FinanceBaseValues.InvoiceStatusReviewed);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 9000m, ToReview);
            await InvoiceAsync(run, "sprk_matter", matter, MatterTypeRef, 5000m, null);
            await BudgetAsync(run, "sprk_matter", matter, 1000m);
            await BudgetAsync(run, "sprk_matter", matter, 500m);

            // Project: its own rows only; the matter's must not leak in.
            await InvoiceAsync(run, "sprk_project", project, ProjectTypeRef, 70m, FinanceBaseValues.InvoiceStatusReviewed);
            await BudgetAsync(run, "sprk_project", project, 400m);

            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
                .Returns((FetchExpression fetch, CancellationToken _) => op.RetrieveMultipleAsync(fetch, CancellationToken.None));

            var matters = await FinanceBaseValues.ReadAsync(
                entities.Object, FinanceBaseValues.MatterEntity, new[] { matter, emptyMatter, project }, needSpend: true, needBudget: true, CancellationToken.None);
            matters[matter].Should().Be((500m, 1500m), "300 + 200 Reviewed invoices (ToReview and no-status are not counted); budgets 1000 + 500");
            matters[emptyMatter].Should().Be((0m, 0m), "a matter with no rows has a sum of nothing");
            matters[project].Should().Be((0m, 0m), "a project id is not a matter id: the query groups on the matter lookup");

            var projects = await FinanceBaseValues.ReadAsync(
                entities.Object, FinanceBaseValues.ProjectEntity, new[] { project, matter }, needSpend: true, needBudget: true, CancellationToken.None);
            projects[project].Should().Be((70m, 400m));
            projects[matter].Should().Be((0m, 0m), "the matter's invoices are not the project's");

            var spendOnly = await FinanceBaseValues.ReadAsync(
                entities.Object, FinanceBaseValues.MatterEntity, new[] { matter }, needSpend: true, needBudget: false, CancellationToken.None);
            spendOnly[matter].Should().Be((500m, 0m), "budget was not asked for, so its query is not sent");
        }
        finally
        {
            await SweepAsync();
        }
    }

    private async Task InvoiceAsync(string run, string parentEntity, Guid parentId, Guid typeRef, decimal amount, int? status)
    {
        var invoice = new Entity("sprk_invoice")
        {
            ["sprk_name"] = $"{Prefix}{run}-invoice",
            ["sprk_totalamount"] = new Money(amount),
            ["sprk_extractionstatus"] = new OptionSetValue(100000000),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", typeRef),
            [parentEntity] = new EntityReference(parentEntity, parentId),
        };
        if (status is { } s) invoice["sprk_invoicestatus"] = new OptionSetValue(s);
        await _dv.OperatorClient.CreateAsync(invoice);
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
