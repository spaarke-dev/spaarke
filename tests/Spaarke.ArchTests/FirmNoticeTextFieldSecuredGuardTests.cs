using System.Xml.Linq;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 132: <c>sprk_invoicesubmission.sprk_firmnoticetext</c> is declared field-secured
/// (<c>&lt;IsSecured&gt;1&lt;/IsSecured&gt;</c>) in every <c>sprk_invoicesubmission</c> Entity.xml under <c>src/dataverse/solutions</c>.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The column holds the notice the BFF sent to the firm (a JSON object whose <c>prose</c> task 130g shows to the firm). It is
/// firm-facing source data that only the BFF may write. If the column lost field security, any user with Write on the submission could change what a
/// firm is shown, and any user with Read could read the raw column past the BFF. This guard covers the declaration (the twin of
/// <see cref="FindingsJsonFieldSecuredGuardTests"/> for the findings column).</para>
/// <para>An explicit <c>1</c> is required: a missing <c>IsSecured</c> element, an explicit <c>0</c>, or no declaration fails.</para>
/// <para><b>Maintenance.</b> Do not unsecure the column. If it must change, first re-review every reader and writer, then update this guard in the
/// same change.</para>
/// <para>Enforced by: <c>FirmNoticeTextFieldSecuredGuardTests.FirmNoticeText_IsDeclaredFieldSecured_InEverySolution</c>.</para>
/// </remarks>
public sealed class FirmNoticeTextFieldSecuredGuardTests
{
    private const string Table = "sprk_invoicesubmission";
    private const string Column = EbillingNotificationJobHandler.FirmNoticeTextColumn;

    private static string SolutionsRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");

    [Fact(DisplayName = "task 132: sprk_invoicesubmission.sprk_firmnoticetext is declared field-secured in every solution that declares it")]
    public void FirmNoticeText_IsDeclaredFieldSecured_InEverySolution()
    {
        var violations = Violations(ReadDeclarations(SolutionsRoot));

        Assert.True(violations.Count == 0,
            "sprk_firmnoticetext holds the notice the BFF sent to the firm; it must stay field-secured to the BFF writers. "
            + "Keep <IsSecured>1</IsSecured>:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Firm-notice-text guard must-not-fire: the real Entity.xml declares the column, explicitly secured")]
    public void RealEntityXml_DeclaresTheColumnSecured_NoViolation()
    {
        var real = ReadDeclarations(SolutionsRoot);

        Assert.NotEmpty(real);
        Assert.All(real, d => Assert.True(d.IsSecured, $"{d.File} declares it unsecured."));
        Assert.Empty(Violations(real));
    }

    [Fact(DisplayName = "Firm-notice-text guard must-fire: the real declaration flipped to unsecured is reported")]
    public void RealDeclarationMadeUnsecured_IsReported()
    {
        var perturbed = ReadDeclarations(SolutionsRoot).Select(d => d with { IsSecured = false }).ToList();

        var violation = Assert.Single(Violations(perturbed));
        Assert.Contains("not an explicit 1", violation, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Firm-notice-text guard must-fire: an explicit 0, a missing IsSecured element, and no declaration")]
    public void FixtureUnsecuredMissingOrUndeclared_IsReported()
    {
        Assert.Single(Violations(Parse(EntityXml(Table, Attribute(Column, "<IsSecured>0</IsSecured>")), "zero.xml")));
        Assert.Single(Violations(Parse(EntityXml(Table, Attribute(Column, "")), "missing.xml")));

        var undeclared = Assert.Single(Violations([]));
        Assert.Contains("no declaration", undeclared, StringComparison.Ordinal);

        Assert.Empty(Violations(Parse(EntityXml(Table, Attribute(Column, "<IsSecured>1</IsSecured>")), "one.xml")));
    }

    [Fact(DisplayName = "Firm-notice-text guard parse: only the invoice submission table, and only the exact column")]
    public void Parse_OtherTableOrLookAlikeColumn_IsIgnored()
    {
        Assert.Empty(Parse(EntityXml("sprk_invoice", Attribute(Column, "<IsSecured>0</IsSecured>")), "other.xml"));
        Assert.Empty(Parse(EntityXml(Table, Attribute(Column + "_old", "<IsSecured>0</IsSecured>")), "lookalike.xml"));
    }

    // ------------------------------------------------------------------------------------------------------------------

    internal sealed record Declaration(string File, bool IsSecured);

    private static List<Declaration> ReadDeclarations(string solutionsRoot) =>
        Directory.EnumerateFiles(solutionsRoot, "Entity.xml", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))), "Entities", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => Parse(File.ReadAllText(f), Path.GetRelativePath(SourceScan.RepoRoot, f)))
            .ToList();

    /// <summary>The column's declarations in one Entity.xml when it is the invoice submission's. Secured only on an explicit 1.</summary>
    private static List<Declaration> Parse(string entityXml, string file)
    {
        var doc = XDocument.Parse(entityXml);
        var table = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value.Trim();
        if (!string.Equals(table, Table, StringComparison.OrdinalIgnoreCase))
            return [];

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "attribute")
            .Where(e => string.Equals(
                e.Elements().FirstOrDefault(c => c.Name.LocalName == "LogicalName")?.Value.Trim(), Column, StringComparison.OrdinalIgnoreCase))
            .Select(e => new Declaration(file, e.Elements().FirstOrDefault(c => c.Name.LocalName == "IsSecured")?.Value.Trim() == "1"))
            .ToList();
    }

    private static List<string> Violations(IReadOnlyList<Declaration> declarations)
    {
        if (declarations.Count == 0)
            return [$"{Column}: no declaration on {Table} under src/dataverse/solutions, so it cannot be shown secured."];

        return declarations.Where(d => !d.IsSecured)
            .Select(d => $"{Column}: {d.File} declares it with IsSecured not an explicit 1.")
            .ToList();
    }

    private static string Attribute(string logicalName, string isSecuredElement) => $"""
        <attribute PhysicalName="{logicalName}">
          <Type>ntext</Type>
          <LogicalName>{logicalName}</LogicalName>
          {isSecuredElement}
        </attribute>
        """;

    private static string EntityXml(string table, params string[] attributes) => $"""
        <Entity>
          <Name LocalizedName="X" OriginalName="X">{table}</Name>
          <EntityInfo>
            <entity Name="{table}">
              <attributes>
                {string.Join("\n", attributes)}
              </attributes>
            </entity>
          </EntityInfo>
        </Entity>
        """;
}
