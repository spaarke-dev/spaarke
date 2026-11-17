using System.Xml.Linq;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Issue #2844 (ebilling-r1): every column the invoice indexing job reads exists in the solution source. Dataverse refuses a query that names a
/// column the table does not have, so a column removed from the table (task 094 removed <c>sprk_invoice.sprk_document</c>) but still named here
/// fails every re-index at run time, and invoice search goes stale with no build error.
/// </summary>
/// <remarks>
/// <para><b>The source.</b> The attributes of <c>src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/Entity.xml</c> and
/// <c>sprk_BillingEvent/Entity.xml</c> (logical names, lower-cased). The columns checked are <see cref="InvoiceIndexDocument.InvoiceColumns"/>
/// and <see cref="InvoiceIndexDocument.LineColumns"/>, the column sets of <c>InvoiceQuery</c> and <c>LineQuery</c>.</para>
/// <para><b>Controls.</b> <see cref="Missing"/> is pure. A synthetic list naming the removed <c>sprk_document</c> MUST be reported (must fire),
/// and the real lists MUST NOT be (must not fire).</para>
/// <para><b>Maintenance.</b> When this fails, either the index names a column the table no longer has (drop it from the index; see #2844), or
/// a column was added to the table by a deploy script and never exported into the solution source (export the solution so Entity.xml carries
/// it). Do not add an allowlist here: a column missing from the source is the failure this guard exists to catch.</para>
/// </remarks>
public class InvoiceIndexColumnsGuardTests
{
    private static string EntitiesRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");

    /// <summary>The attribute logical names (lower-cased) of one table's Entity.xml.</summary>
    private static IReadOnlySet<string> AttributesOf(string entityFolder)
    {
        var file = Path.Combine(EntitiesRoot, entityFolder, "Entity.xml");
        Assert.True(File.Exists(file), $"The solution source has no {entityFolder}/Entity.xml; the scan went stale.");
        return XDocument.Load(file).Descendants("attribute")
            .Select(a => ((string?)a.Element("LogicalName") ?? (string?)a.Attribute("PhysicalName"))?.ToLowerInvariant())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The columns of <paramref name="columns"/> that are not attributes of the table (pure).</summary>
    internal static IReadOnlyList<string> Missing(IEnumerable<string> columns, IReadOnlySet<string> attributes) =>
        columns.Select(c => c.ToLowerInvariant()).Where(c => !attributes.Contains(c)).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();

    [Fact(DisplayName = "#2844: every invoice column the invoice index reads exists on sprk_invoice in the solution source")]
    public void EveryInvoiceIndexColumn_IsAnInvoiceAttribute()
    {
        var missing = Missing(InvoiceIndexDocument.InvoiceColumns, AttributesOf("sprk_Invoice"));
        Assert.True(missing.Count == 0, $"The invoice index reads columns sprk_invoice does not have: {string.Join(", ", missing)}.");
    }

    [Fact(DisplayName = "#2844: every line column the invoice index reads exists on sprk_billingevent in the solution source")]
    public void EveryInvoiceIndexLineColumn_IsABillingEventAttribute()
    {
        var missing = Missing(InvoiceIndexDocument.LineColumns, AttributesOf("sprk_BillingEvent"));
        Assert.True(missing.Count == 0, $"The invoice index reads columns sprk_billingevent does not have: {string.Join(", ", missing)}.");
    }

    [Fact(DisplayName = "#2844 controls: the removed sprk_document is reported (must fire); the real invoice columns are not (must not fire)")]
    public void Missing_ReportsARemovedColumn_AndNotTheRealOnes()
    {
        var invoiceAttributes = AttributesOf("sprk_Invoice");

        Assert.Equal(["sprk_document"], Missing([.. InvoiceIndexDocument.InvoiceColumns, "sprk_document"], invoiceAttributes));
        Assert.Empty(Missing(InvoiceIndexDocument.InvoiceColumns, invoiceAttributes));
    }
}
