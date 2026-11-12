using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// ebilling-r1 D-130, ontology-r1 review (field-security guard). The derived Threshold (<see cref="FinanceBaseValues"/>) reads the base
/// columns in <see cref="FinanceBaseValues.DerivedBaseColumns"/> ORG-WIDE with the evaluator's own identity. A field-secured column
/// among them would read as empty for that identity: the approved total would silently fall back to the billed total, and the others
/// would read as 0 or as "not blocked". So none may be secured. Ontology's answer is to guard the columns, NOT to add
/// <c>sprk_approvedtotal</c> to <c>PolicyVersionValidator.AuthorableColumns</c> (an author must not be able to name it).
/// <para><b>One reviewed exception (ebilling-r1 task 166, EB-D-200; owner + uac-r2 option D).</b> <c>sprk_invoice.sprk_totalamount</c> and
/// <c>sprk_invoice.sprk_approvedtotal</c> are field-secured on purpose: only the server writes them, and every user reads them. The evaluator
/// (<c>SignalEvaluator</c>, DI singleton over the shared <c>IGenericEntityService</c>) reads both through the read grant of the profile that
/// covers its identity. They are listed in <c>DerivedBaseColumnSecurityExceptions</c> (tests/integration/Shared, shared with the live guard) with
/// that reason; every OTHER derived base column must still be unsecured, and a listed column must still be secured (a stale entry fails).
/// A different evaluator identity would need the same read grant.</para>
/// <para>This is the OFFLINE half: the SpaarkeMaster solution source (<c>Entities/{table}/Entity.xml</c> <c>IsSecured</c>), read as
/// text like <see cref="FirmReasonFieldSecurityAgreementTests"/> (no network). The live half is
/// <c>SignalPredicateTests.AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema</c>, which asks the live schema.</para>
/// </summary>
[Trait("Category", "Security")]
public class DerivedBaseColumnsFieldSecurityTests
{
    /// <summary>Columns of <see cref="FinanceBaseValues.DerivedBaseColumns"/> that are NOT in the SpaarkeMaster source yet, each with
    /// the reason. Empty today: every column is exported. A column listed here is skipped explicitly; the test below fails if a listed
    /// column IS present (the entry is stale and the column must be checked), so a skip can never hide a column silently.</summary>
    private static readonly IReadOnlyDictionary<(string Entity, string Column), string> NotYetInSolution =
        new Dictionary<(string, string), string>();

    /// <summary>The shared reviewed exception list (task 166, EB-D-200); the live guard reads the same one.</summary>
    private static IReadOnlyDictionary<(string Entity, string Column), string> SecuredForTheBffWriters => DerivedBaseColumnSecurityExceptions.SecuredForTheBffWriters;

    [Fact]
    public void EveryDerivedBaseColumn_InTheSolutionSource_IsNotFieldSecured()
    {
        var checkedColumns = new List<string>();
        foreach (var (entity, column) in FinanceBaseValues.DerivedBaseColumns)
        {
            var attribute = FindAttribute(entity, column);
            if (attribute is null)
            {
                NotYetInSolution.Should().ContainKey((entity, column),
                    $"{entity}.{column} is read by the derived Threshold but is not in the SpaarkeMaster source and has no listed reason");
                continue;
            }

            NotYetInSolution.Should().NotContainKey((entity, column), $"{entity}.{column} IS in the solution source now: remove its skip and let it be checked");
            if (SecuredForTheBffWriters.ContainsKey((entity, column)))
            {
                IsSecured(attribute).Should().BeTrue(
                    $"{entity}.{column} is listed as secured for the BFF writers (EB-D-200); if it is no longer secured, remove the entry");
            }
            else
            {
                IsSecured(attribute).Should().BeFalse(
                    $"{entity}.{column} is read org-wide by the derived Threshold's evaluator; a field-secured column would read as empty (approved total falls back to billed)");
            }

            checkedColumns.Add($"{entity}.{column}");
        }

        checkedColumns.Should().HaveCount(FinanceBaseValues.DerivedBaseColumns.Count - NotYetInSolution.Count, "every column is either checked or explicitly skipped with a reason");
        checkedColumns.Should().Contain("sprk_invoice.sprk_approvedtotal");
    }

    [Fact]
    public void TheDerivedBaseColumns_CoverTheAmountRuleAndTheHeldAntiJoin()
    {
        // Pins the set so a column the reader starts reading cannot be added to the queries without being guarded here.
        FinanceBaseValues.DerivedBaseColumns.Should().BeEquivalentTo(new (string, string)[]
        {
            ("sprk_invoice", "sprk_totalamount"),
            ("sprk_invoice", "sprk_approvedtotal"),
            ("sprk_invoice", "statuscode"),
            ("sprk_invoice", "sprk_matter"),
            ("sprk_invoice", "sprk_project"),
            ("sprk_budget", "sprk_totalbudget"),
            ("sprk_budget", "sprk_matter"),
            ("sprk_budget", "sprk_project"),
            ("sprk_invoicesubmission", "sprk_processingstatus"),
            ("sprk_invoicesubmission", "sprk_outcomereasoncode"),
            ("sprk_invoicesubmission", "sprk_invoice"),
            // Task 165b-4: counted spend needs the writer's approve Decision Record under the approval portion's key.
            ("sprk_decisionrecord", "sprk_regardinginvoice"),
            ("sprk_decisionrecord", "sprk_actioncode"),
            ("sprk_decisionrecord", "sprk_steps"),
            ("sprk_decisionrecord", "createdby"),
            ("sprk_decisionrecord", "createdonbehalfby"),
            ("sprk_invoiceapproval", "sprk_invoice"),
        });
    }

    [Fact]
    public void TheCheck_Fires_OnASecuredColumn_AndStaysQuietOnAnUnsecuredOne()
    {
        // Must-fire / must-not-fire controls for the IsSecured reader.
        var secured = XElement.Parse("<attribute><LogicalName>sprk_x</LogicalName><IsSecured>1</IsSecured></attribute>");
        var open = XElement.Parse("<attribute><LogicalName>sprk_x</LogicalName><IsSecured>0</IsSecured></attribute>");
        var absent = XElement.Parse("<attribute><LogicalName>sprk_x</LogicalName></attribute>");

        IsSecured(secured).Should().BeTrue();
        IsSecured(open).Should().BeFalse();
        IsSecured(absent).Should().BeFalse();
    }

    [Fact]
    public void TheExceptionList_NamesOnlyDerivedBaseColumns()
    {
        // A listed column that is not a derived base column is a stale or wrong entry; the exception can never widen the guard silently.
        SecuredForTheBffWriters.Keys.Should().OnlyContain(k => FinanceBaseValues.DerivedBaseColumns.Contains(k));
    }

    private static bool IsSecured(XElement attribute) => (string?)attribute.Element("IsSecured") == "1";

    private static XElement? FindAttribute(string entity, string column)
    {
        var entities = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        var folder = Directory.GetDirectories(entities).SingleOrDefault(d => string.Equals(Path.GetFileName(d), entity, StringComparison.OrdinalIgnoreCase));
        folder.Should().NotBeNull($"the SpaarkeMaster source has an Entities folder for {entity}");
        return XDocument.Load(Path.Combine(folder!, "Entity.xml")).Descendants("attribute")
            .SingleOrDefault(a => (string?)a.Element("LogicalName") == column);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
