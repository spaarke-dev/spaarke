using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 165c: on the submission's main form (<c>scripts/ebilling/Deploy-EbillingFormsViews.ps1</c>, section <c>links</c>) the
/// invoice link, the source document and the correction target are read-only, like the firm. The BFF is their only writer.
/// </summary>
/// <remarks>
/// Enforced by: this text pin of the forms script (CI and the Stop hook). The script runs only against a live environment, so no unit test can
/// observe the form it builds; reading its definition is the strongest mechanism that works. Read as text (no PowerShell host, no network).
/// Controls: <see cref="TheParser_SeesAMissingColumn"/> and <see cref="TheParser_SeesNoReadOnlyList"/>.
/// </remarks>
public class InvoiceSubmissionFormLinksReadOnlyTests
{
    private static readonly string[] LinkColumns = ["sprk_invoice", "sprk_document", "sprk_resubmissionof"];

    private static readonly Regex SubmissionEntry = new(
        @"^\s*sprk_invoicesubmission\s*=\s*@\{(?<body>.*?)^\s*View\s*=", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

    private static readonly Regex LinksSection = new(
        @"@\{\s*Name\s*=\s*'links'(?<section>[^}]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ReadOnlyList = new(@"\bReadOnly\s*=\s*(?<list>[^\r\n;}]+)", RegexOptions.Compiled);

    private static readonly Regex LinksReadOnlyLine = new(@"\r?\n\s*ReadOnly = 'sprk_invoice'[^}]*\}", RegexOptions.Compiled);

    /// <summary>The columns the submission entry's <c>links</c> section lists as read-only; empty when there is no such list.</summary>
    internal static HashSet<string> ReadOnlyLinkColumns(string script)
    {
        var entry = SubmissionEntry.Match(script);
        Assert.True(entry.Success, "the forms script has an sprk_invoicesubmission entry");
        var section = LinksSection.Match(entry.Groups["body"].Value);
        Assert.True(section.Success, "the submission entry has a 'links' section");
        var list = ReadOnlyList.Match(section.Groups["section"].Value);
        return list.Success
            ? Regex.Matches(list.Groups["list"].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    [Fact(DisplayName = "165c: the submission form shows the invoice link, the source document and the correction target read-only")]
    public void TheSubmissionForm_ShowsTheLinkColumnsReadOnly()
    {
        var script = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "ebilling", "Deploy-EbillingFormsViews.ps1"));

        Assert.Superset(LinkColumns.ToHashSet(StringComparer.Ordinal), ReadOnlyLinkColumns(script));
    }

    private const string Seed = """
            sprk_invoicesubmission = @{
                Form = 'Replace'; FormName = 'Information'
                Sections = @(
                    @{ Name = 'general'; Label = 'Submission'; Cols = 'sprk_name', 'sprk_vendororg'
                        ReadOnly = 'sprk_vendororg' }
                    @{ Name = 'links'; Label = 'Resulting invoice and documents'; Cols = 'sprk_invoice', 'sprk_resubmissionof', 'sprk_document'
                        ReadOnly = 'sprk_invoice', 'sprk_document', 'sprk_resubmissionof' }
                )
                View = 'sprk_name'
            }
        """;

    [Fact(DisplayName = "165c control (must not fire): the parser reads the three columns from the links section, not the firm from another section")]
    public void TheParser_ReadsTheLinksSection() =>
        Assert.Equal(LinkColumns.Order(StringComparer.Ordinal), ReadOnlyLinkColumns(Seed).Order(StringComparer.Ordinal));

    [Fact(DisplayName = "165c control (must fire): a column dropped from the list is seen")]
    public void TheParser_SeesAMissingColumn() =>
        Assert.DoesNotContain("sprk_document", ReadOnlyLinkColumns(
            Seed.Replace("'sprk_invoice', 'sprk_document', 'sprk_resubmissionof' }", "'sprk_invoice', 'sprk_resubmissionof' }", StringComparison.Ordinal)));

    [Fact(DisplayName = "165c control (must fire): a links section with no read-only list is seen")]
    public void TheParser_SeesNoReadOnlyList()
    {
        var withoutList = LinksReadOnlyLine.Replace(Seed, " }");

        Assert.NotEqual(Seed, withoutList);
        Assert.Empty(ReadOnlyLinkColumns(withoutList));
    }
}
