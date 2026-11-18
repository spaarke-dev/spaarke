using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 ISS-040: <c>sprk_ruleset.sprk_visibletofirms</c> is declared field-secured
/// (<c>&lt;IsSecured&gt;1&lt;/IsSecured&gt;</c>) in every <c>sprk_ruleset</c> Entity.xml under <c>src/dataverse/solutions</c>.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Every firm-visibility reader trusts the flag: it decides whether a rule set's clause quotes and section
/// references are shown to firms. Only the BFF paths that own it change it, and the BFF writes through its application user, which
/// is in the "Spaarke BFF-Managed Field Writers" profile. Secured, a native write by anyone else is refused, while every user keeps
/// reading the value through the "Spaarke BFF-Managed Field Readers" profile. If the column lost field security (drift), writes
/// would no longer be limited to the BFF writers profile, although every firm-visibility reader trusts the flag.
/// This guard covers the declaration; the permissions travel in
/// <c>Other/FieldSecurityProfiles.xml</c> and are applied by <c>scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1</c>.</para>
/// <para>An explicit <c>1</c> is required: a missing <c>IsSecured</c> element, an explicit <c>0</c>, or no declaration fails.</para>
/// <para><b>Maintenance.</b> Do not unsecure the column. If it must change, first move the flag's writers (re-review every reader
/// with unified-access-control-r2), then update this guard in the same change. When a new solution declares the table, the guard
/// covers it without an edit.</para>
/// <para>Enforced by: <c>RuleSetVisibilityFieldSecuredGuardTests.VisibleToFirms_IsDeclaredFieldSecured_InEverySolution</c>.</para>
/// </remarks>
public sealed class RuleSetVisibilityFieldSecuredGuardTests
{
    private const string Table = "sprk_ruleset";
    private const string Column = "sprk_visibletofirms";

    private static string SolutionsRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");

    [Fact(DisplayName = "ISS-040: sprk_ruleset.sprk_visibletofirms is declared field-secured in every solution that declares it")]
    public void VisibleToFirms_IsDeclaredFieldSecured_InEverySolution()
    {
        var violations = Violations(ReadDeclarations(SolutionsRoot));

        Assert.True(violations.Count == 0,
            "sprk_visibletofirms decides what firms are shown; it must stay field-secured so only the BFF writers change it. "
            + "Keep <IsSecured>1</IsSecured>:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Rule-set visibility guard must-not-fire: the real Entity.xml declares the column, explicitly secured")]
    public void RealEntityXml_DeclaresTheColumnSecured_NoViolation()
    {
        var real = ReadDeclarations(SolutionsRoot);

        Assert.NotEmpty(real);
        Assert.All(real, d => Assert.True(d.IsSecured, $"{d.File} declares it unsecured."));
        Assert.Empty(Violations(real));
    }

    [Fact(DisplayName = "Rule-set visibility guard must-fire: the real declaration flipped to unsecured is reported")]
    public void RealDeclarationMadeUnsecured_IsReported()
    {
        var perturbed = ReadDeclarations(SolutionsRoot).Select(d => d with { IsSecured = false }).ToList();

        var violation = Assert.Single(Violations(perturbed));
        Assert.Contains("not an explicit 1", violation, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Rule-set visibility guard must-fire: an explicit 0, a missing IsSecured element, and no declaration")]
    public void FixtureUnsecuredMissingOrUndeclared_IsReported()
    {
        Assert.Single(Violations(Parse(EntityXml(Table, Attribute(Column, "<IsSecured>0</IsSecured>")), "zero.xml")));
        Assert.Single(Violations(Parse(EntityXml(Table, Attribute(Column, "")), "missing.xml")));

        var undeclared = Assert.Single(Violations([]));
        Assert.Contains("no declaration", undeclared, StringComparison.Ordinal);

        Assert.Empty(Violations(Parse(EntityXml(Table, Attribute(Column, "<IsSecured>1</IsSecured>")), "one.xml")));
    }

    [Fact(DisplayName = "Rule-set visibility guard parse: only the rule set table, and only the exact column")]
    public void Parse_OtherTableOrLookAlikeColumn_IsIgnored()
    {
        Assert.Empty(Parse(EntityXml("sprk_policy", Attribute(Column, "<IsSecured>0</IsSecured>")), "other.xml"));
        Assert.Empty(Parse(EntityXml(Table, Attribute(Column + "_old", "<IsSecured>0</IsSecured>")), "lookalike.xml"));
    }

    // ------------------------------------------------------------------------------------------------------------------

    internal sealed record Declaration(string File, bool IsSecured);

    private static List<Declaration> ReadDeclarations(string solutionsRoot) =>
        Directory.EnumerateFiles(solutionsRoot, "Entity.xml", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))), "Entities", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => Parse(File.ReadAllText(f), Path.GetRelativePath(SourceScan.RepoRoot, f)))
            .ToList();

    /// <summary>The column's declarations in one Entity.xml when it is the rule set's. Secured only on an explicit 1.</summary>
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
          <Type>bit</Type>
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
