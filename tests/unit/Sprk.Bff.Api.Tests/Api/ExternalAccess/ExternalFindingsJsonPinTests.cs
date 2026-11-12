using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// Ebilling-r1 task 095d-2a-1 (EB-D-189, uac-r2 condition 2): the stored findings (<c>sprk_invoicesubmission.sprk_findingsjson</c>) now hold
/// timekeeper names and observation quotes, and are never returned on an external route. Pinned on the production registry: no external
/// module is registered for the submission table (the module fetch refuses an unregistered entity before any read), no module can read the
/// column, and the fetch guard refuses a fetch that reads it, filters on it (a value oracle) or sorts by it, on every registered module.
/// </summary>
/// <remarks>The column is not in any module's <c>UnfilterableColumns</c>: that set narrows a READABLE column (the invoice's review detail).
/// This column is stronger: it is readable on no module, so the guard refuses it as an attribute, a condition, a <c>valueof</c> and an order.</remarks>
[Trait("category", "external-access")]
public sealed class ExternalFindingsJsonPinTests
{
    private const string Column = InvoiceSubmissionPersister.SubmissionFindingsJson;

    private static ExternalModuleRegistry ProductionRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        return services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
    }

    [Fact(DisplayName = "095d-2a-1 SEEDED: no external module is registered for sprk_invoicesubmission and none can read sprk_findingsjson")]
    public void NoExternalModule_ReadsTheStoredFindings()
    {
        var registry = ProductionRegistry();

        registry.Modules.Should().NotBeEmpty("the setup: the production registry");
        registry.FindByEntity(FinanceBaseValues.SubmissionEntity).Should().BeNull("the module fetch refuses an unregistered entity before any read");
        registry.Modules.Should().OnlyContain(m => !m.ReadableColumns.Contains(Column), "no module's allow-list may name the stored findings");
    }

    [Fact(DisplayName = "095d-2a-1 SEEDED: on every registered module the fetch guard refuses a fetch that reads, filters on or sorts by sprk_findingsjson")]
    public void TheFetchGuard_RefusesTheStoredFindings_OnEveryModule()
    {
        var extractor = new FetchXmlEntityExtractor();
        foreach (var module in ProductionRegistry().Modules)
        {
            var entity = module.RecordEntity;
            var readable = module.ReadableColumns.First();
            string[] fetches =
            [
                $"<fetch><entity name='{entity}'><attribute name='{Column}'/></entity></fetch>",
                $"<fetch><entity name='{entity}'><attribute name='{readable}'/><filter><condition attribute='{Column}' operator='like' value='%a%'/></filter></entity></fetch>",
                $"<fetch><entity name='{entity}'><attribute name='{readable}'/><filter><condition attribute='{readable}' operator='eq' valueof='{Column}'/></filter></entity></fetch>",
                $"<fetch><entity name='{entity}'><attribute name='{readable}'/><order attribute='{Column}'/></entity></fetch>",
            ];

            foreach (var fetch in fetches)
            {
                ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(fetch, entity, module.ReadableColumns, extractor, ExternalInvoiceReviewDetail.UnfilterableColumns(entity))
                    .Verdict.Should().Be(ExternalModuleDataEndpoints.FetchXmlGuardVerdict.ColumnNotPermitted, $"{module.Name}: {fetch}");
            }

            // Must-not-fire: a fetch of the module's own readable column passes.
            ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
                    $"<fetch><entity name='{entity}'><attribute name='{readable}'/></entity></fetch>", entity, module.ReadableColumns, extractor,
                    ExternalInvoiceReviewDetail.UnfilterableColumns(entity))
                .Verdict.Should().Be(ExternalModuleDataEndpoints.FetchXmlGuardVerdict.Allowed, module.Name);
        }
    }
}
