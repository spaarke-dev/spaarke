using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 015 (D-41, owner decisions D-97 / D-103): the invoice status lives on <c>statuscode</c>. The old choice column
/// and its global option set are retired (deleted from dev by 015's guarded -Delete step), so a reader that comes back (a stale branch merged later, a copied
/// query, a form or view in solution source) would fail at run time against Dataverse, or silently count no spend.
/// </summary>
/// <remarks>
/// <para>Enforced by: this source scan (CI and the Stop hook). It fails on the retired column's logical name anywhere under
/// <c>src/</c>, <c>tests/</c> and <c>scripts/</c> (code, solution XML, scripts, configuration), and on the retired constant names
/// that read it indirectly (<c>FinanceBaseValues.InvoiceStatus</c>, <c>InvoiceStatusReviewed</c>, <c>InvoiceStatusToReview</c>,
/// <c>InvInvoiceStatus</c>): the 2026-10-17 code check found readers that a literal grep alone missed. Comments are scanned too:
/// a comment that names the column sends the next reader to it.</para>
/// <para>No exemption: the external invoices allow-list entry, the last transitional mention, was removed on master in #2450
/// (spaarke-SPA-external-access-platform-r3 task 002), so the transition window and its exemption list are gone. The dev column delete
/// (015 -Delete, D-97) waits for the next dev BFF deploy.</para>
/// <para>Excluded: <c>projects/**</c> (task history, not scanned at all), the files in <see cref="ExcludedFiles"/> (this file, and
/// the external allow-list must-refuse tests: each must spell the name to guard against it), and build
/// output / dependencies (<c>bin</c>, <c>obj</c>, <c>node_modules</c>, <c>dist</c>, <c>coverage</c>, <c>.git</c>).</para>
/// <para>Controls: <see cref="TheDetector_FiresOnEachRetiredName"/> (must fire) and
/// <see cref="TheDetector_StaysQuietOnTheStatuscodeReplacements"/> (must not fire); <see cref="TheWalk_ReachesEveryScannedRoot"/>
/// proves the scan is not vacuous.</para>
/// </remarks>
public class InvoiceStatusRetirementGuardTests
{
    private static readonly string[] ScannedRoots = { "src", "tests", "scripts" };

    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "dist", "coverage", ".git", ".vite" };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".resx", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".json", ".xml", ".ps1",
        ".psm1", ".psd1", ".py", ".sh", ".md", ".yml", ".yaml", ".html", ".css", ".txt", ".sql", ".bicep", ".config", ".poml",
    };

    private const string GuardFileName = nameof(InvoiceStatusRetirementGuardTests) + ".cs";

    /// <summary>
    /// Files that must spell the retired name, each with its reason (repo-relative, forward slashes). This file names what it looks
    /// for; the external allow-list tests prove the BFF REFUSES the column (master #2450 must-refuse tests), which they cannot do
    /// without naming it. Keep this list to files whose purpose is to guard against the name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExcludedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["tests/Spaarke.ArchTests/" + GuardFileName] = "this guard spells what it looks for",
        ["tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs"] =
            "must-refuse tests: the external invoice fetch and /record refuse the retired column",
    };

    private static readonly (Regex Pattern, string Description)[] Detectors =
    {
        (new Regex(@"sprk_invoicestatus", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "names the retired invoice-status column; read statuscode (FinanceBaseValues.InvoiceStatusCodeColumn, InvoiceStatusCodes)"),
        (new Regex(@"\bFinanceBaseValues\.InvoiceStatus\b", RegexOptions.Compiled),
            "reads the retired column through FinanceBaseValues.InvoiceStatus; use FinanceBaseValues.InvoiceStatusCodeColumn"),
        (new Regex(@"\b(InvoiceStatusReviewed|InvoiceStatusToReview|InvInvoiceStatus)\b", RegexOptions.Compiled),
            "uses a retired invoice-status constant; use InvoiceStatusCodes / FinanceBaseValues.CountedInvoiceStatuses"),
    };

    internal static IReadOnlyList<string> Violations(string text) =>
        Detectors.Where(d => d.Pattern.IsMatch(text)).Select(d => d.Description).ToList();

    /// <summary>Text files under <c>{repo}/{root}</c>, pruning <see cref="ExcludedDirectoryNames"/> before descending (copied from
    /// <c>ADR002_PluginTests.Walk</c>).</summary>
    private static IEnumerable<string> Walk(string root)
    {
        var start = Path.Combine(SourceScan.RepoRoot, root);
        if (!Directory.Exists(start))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (TextExtensions.Contains(Path.GetExtension(file))
                    && !ExcludedFiles.ContainsKey(SourceScan.Relative(file).Replace('\\', '/')))
                {
                    yield return file;
                }
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }
        }
    }

    [Fact(DisplayName = "ebilling-r1 015: nothing under src/, tests/ or scripts/ names the retired invoice-status column or its constants")]
    public void NoReader_OfTheRetiredInvoiceStatusColumn()
    {
        var found = new List<string>();
        foreach (var file in ScannedRoots.SelectMany(Walk))
        {
            var relative = SourceScan.Relative(file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                found.AddRange(Violations(lines[i]).Select(why => $"{relative}:{i + 1}: {why}"));
            }
        }

        Assert.True(found.Count == 0,
            "The invoice status lives on statuscode (ebilling-r1 D-41; the old column and option set are deleted). Found:\n"
            + string.Join("\n", found));
    }

    [Theory(DisplayName = "ebilling-r1 015 control: the detector fires on each retired name")]
    [InlineData("<attribute name='sprk_invoicestatus'/>")]
    [InlineData("{ attribute: 'SPRK_InvoiceStatus', label: 'Status' }")]
    [InlineData("var s = row[\"sprk_invoicestatusname\"];")]
    [InlineData("private const string Invoice_InvoiceStatus = FinanceBaseValues.InvoiceStatus;")]
    [InlineData("h.WithInvoice(\"sprk_matter\", id, 1m, FinanceBaseValues.InvoiceStatusReviewed);")]
    [InlineData("[InvInvoiceStatus] = InvoiceStatusToReview,")]
    public void TheDetector_FiresOnEachRetiredName(string text) => Assert.NotEmpty(Violations(text));

    [Theory(DisplayName = "ebilling-r1 015 control: the detector stays quiet on the statuscode replacements")]
    [InlineData("private const string Invoice_StatusCode = FinanceBaseValues.InvoiceStatusCodeColumn;")]
    [InlineData("FinanceBaseValues.CountedInvoiceStatuses.Cast<object>().ToArray()")]
    [InlineData("new[] { InvoiceStatusCodes.InReview, InvoiceStatusCodes.Paid }")]
    [InlineData("{ attribute: 'statuscode', label: 'Status' }")]
    [InlineData("private const string PredicateInvoiceStatus = \"invoiceStatus\";")]
    [InlineData("<attribute name='sprk_invoicenumber'/><attribute name='sprk_invoicedate'/>")]
    public void TheDetector_StaysQuietOnTheStatuscodeReplacements(string text) => Assert.Empty(Violations(text));

    [Fact(DisplayName = "ebilling-r1 015 control: the walk reaches code, solution source, the SPA and scripts, and skips its own file")]
    public void TheWalk_ReachesEveryScannedRoot()
    {
        var files = ScannedRoots.SelectMany(Walk).Select(f => SourceScan.Relative(f).Replace('\\', '/')).ToList();

        Assert.Contains("src/server/api/Sprk.Bff.Api/Services/Finance/FinanceBaseValues.cs", files);
        Assert.Contains("src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/Entity.xml", files);
        Assert.Contains("src/client/external-spa/src/components/detail/RecordDetailModal.tsx", files);
        Assert.Contains("scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1", files);
        Assert.Contains("tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FinanceBaseValuesTests.cs", files);
        Assert.DoesNotContain(files, f => f.EndsWith("/" + GuardFileName, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase));
        foreach (var excluded in ExcludedFiles.Keys)
        {
            Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot, excluded)), $"excluded file is gone; remove it from ExcludedFiles: {excluded}");
            Assert.DoesNotContain(excluded, files);
        }
    }
}
