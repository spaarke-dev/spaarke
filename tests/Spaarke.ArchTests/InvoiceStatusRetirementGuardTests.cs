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
/// <para>Transition window (D-97): two exact lines are exempt until the column is deleted from dev, the external invoices allow-list
/// entry and the one constant the allow-list test spells the name with (<c>TransitionalExemptions</c>, a ratchet that only shrinks).
/// Every other mention still fails, including the same text on another line or in another file.</para>
/// <para>Excluded: <c>projects/**</c> (task history, not scanned at all), this file (it must spell what it looks for), and build
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

    /// <summary>
    /// The TRANSITION-WINDOW exemption (ratchet baseline; D-97 / uac-r2 review F1). Until the retired column is deleted from dev, the
    /// live outside-counsel invoices grid (3ff4102c) still names it, so the BFF's external invoices allow-list admits it next to
    /// <c>statuscode</c>. Each entry is ONE exact line in ONE exact file (compared after trimming, comment included): the production
    /// allow-list entry, and the single constant the allow-list test file spells the name with. Nothing else is exempt, and the
    /// list only shrinks: <see cref="EveryTransitionalExemption_IsStillNeeded"/> fails when an entry is removed from its file
    /// without removing it here, and <see cref="TheExemptionList_NeverGrows"/> fails when one is added. Remove both entries (and
    /// this list) in the follow-up commit after the dev column delete.
    /// </summary>
    private static readonly (string File, string Line)[] TransitionalExemptions =
    {
        ("src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs",
            "\"sprk_invoicestatus\", // transitional (015/D-97): remove after the dev column delete"),
        ("tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs",
            "private const string TransitionalInvoiceStatusColumn = \"sprk_invoicestatus\"; // transitional (015/D-97): remove after the dev column delete"),
    };

    /// <summary>The two files the transition-window exemptions may name, and the marker every exempt line must carry (D-97).</summary>
    private static readonly string[] ExpectedExemptFiles =
    {
        "src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs",
        "tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs",
    };

    private const string TransitionalMarker = "transitional (015/D-97)";

    /// <summary>
    /// What is wrong with an exemption list, or <c>null</c> when it is exactly the two sanctioned entries: the file paths are exactly
    /// <see cref="ExpectedExemptFiles"/> (in that order, so a count alone cannot pass a swapped-in file) and every line carries
    /// <see cref="TransitionalMarker"/>.
    /// </summary>
    internal static string? ExemptionListProblem(IReadOnlyList<(string File, string Line)> entries)
    {
        var files = entries.Select(e => e.File).ToArray();
        if (!files.SequenceEqual(ExpectedExemptFiles, StringComparer.Ordinal))
        {
            return $"the exempt files must be exactly [{string.Join(", ", ExpectedExemptFiles)}], found [{string.Join(", ", files)}]";
        }

        var unmarked = entries.Where(e => !e.Line.Contains(TransitionalMarker, StringComparison.Ordinal)).Select(e => $"{e.File}: {e.Line}").ToList();
        return unmarked.Count == 0 ? null : $"every exempt line must contain '{TransitionalMarker}'; missing in: {string.Join(" | ", unmarked)}";
    }

    /// <summary>Index into <see cref="TransitionalExemptions"/> of the entry that exempts this line of this file, or -1.</summary>
    private static int ExemptEntryIndex(string relativePath, string line)
    {
        var path = relativePath.Replace('\\', '/');
        var trimmed = line.Trim();
        for (var i = 0; i < TransitionalExemptions.Length; i++)
        {
            if (TransitionalExemptions[i].File == path && TransitionalExemptions[i].Line == trimmed)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Violations on one line of one file: <see cref="Violations(string)"/> minus the exact transition-window entries.</summary>
    internal static IReadOnlyList<string> Violations(string relativePath, string line) =>
        ExemptEntryIndex(relativePath, line) >= 0 ? Array.Empty<string>() : Violations(line);

    /// <summary>
    /// Scans the lines of one file: the violations (<c>path:line: why</c>) and how often each exemption entry was used. An exemption
    /// covers ONE line: a second identical copy (a pasted duplicate of the marker line) is a second use, reported by
    /// <see cref="ExemptionUsageProblems"/>.
    /// </summary>
    internal static (List<string> Found, int[] ExemptHits) ScanLines(string relativePath, IReadOnlyList<string> lines)
    {
        var found = new List<string>();
        var hits = new int[TransitionalExemptions.Length];
        for (var i = 0; i < lines.Count; i++)
        {
            var entry = ExemptEntryIndex(relativePath, lines[i]);
            if (entry >= 0)
            {
                hits[entry]++;
                continue;
            }

            foreach (var why in Violations(lines[i]))
            {
                found.Add($"{relativePath}:{i + 1}: {why}");
            }
        }

        return (found, hits);
    }

    /// <summary>One message per exemption entry that was used more than once (summed over every scanned file).</summary>
    internal static IReadOnlyList<string> ExemptionUsageProblems(int[] exemptHits) =>
        exemptHits.Select((count, i) => (count, i)).Where(x => x.count > 1)
            .Select(x => $"{TransitionalExemptions[x.i].File}: the exempt line occurs {x.count} times (an exemption covers exactly one line): {TransitionalExemptions[x.i].Line}")
            .ToList();

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
                    && !Path.GetFileName(file).Equals(GuardFileName, StringComparison.OrdinalIgnoreCase))
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
        var exemptHits = new int[TransitionalExemptions.Length];
        foreach (var file in ScannedRoots.SelectMany(Walk))
        {
            var (fileFound, fileHits) = ScanLines(SourceScan.Relative(file), File.ReadAllLines(file));
            found.AddRange(fileFound);
            for (var e = 0; e < exemptHits.Length; e++)
            {
                exemptHits[e] += fileHits[e];
            }
        }

        found.AddRange(ExemptionUsageProblems(exemptHits));

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

    [Fact(DisplayName = "ebilling-r1 015 transition: every exempt line still exists in its file (a leftover exemption fails)")]
    public void EveryTransitionalExemption_IsStillNeeded()
    {
        foreach (var (file, line) in TransitionalExemptions)
        {
            var path = Path.Combine(SourceScan.RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{file}: the exempt file is gone; remove its entry from TransitionalExemptions.");
            Assert.True(File.ReadAllLines(path).Any(l => l.Trim() == line),
                $"{file}: the exempt line is gone (the transition window is over?); remove its entry from TransitionalExemptions.");
        }
    }

    [Fact(DisplayName = "ebilling-r1 015 transition: the exemption list is exactly the two named files, each line marked transitional (015/D-97)")]
    public void TheExemptionList_NeverGrows()
    {
        var problem = ExemptionListProblem(TransitionalExemptions);
        Assert.True(problem is null,
            "Only the external invoices allow-list entry and the allow-list test's one constant are exempt (D-97 transition window): " + problem);
    }

    [Fact(DisplayName = "ebilling-r1 015 transition control: a swapped-in file, an extra entry or an unmarked line fails the exemption-list check")]
    public void TheExemptionListCheck_FiresOnAnyDeparture()
    {
        var module = TransitionalExemptions[0];
        var tests = TransitionalExemptions[1];

        // the real list is clean (the control's baseline)
        Assert.Null(ExemptionListProblem(new[] { module, tests }));
        // another file's line swapped in for the test file's entry: the count is still 2, the paths are not
        Assert.NotNull(ExemptionListProblem(new[] { module, ("src/server/api/Sprk.Bff.Api/Services/Finance/FinanceSummaryService.cs", tests.Line) }));
        Assert.NotNull(ExemptionListProblem(new[] { ("src/server/api/Sprk.Bff.Api/Services/Finance/FinanceSummaryService.cs", module.Line), tests }));
        // the two entries in the other order, an entry dropped, an entry added
        Assert.NotNull(ExemptionListProblem(new[] { tests, module }));
        Assert.NotNull(ExemptionListProblem(new[] { module }));
        Assert.NotNull(ExemptionListProblem(new[] { module, tests, ("src/x/Other.cs", module.Line) }));
        // a line without the marker
        Assert.NotNull(ExemptionListProblem(new[] { module, (tests.File, "private const string TransitionalInvoiceStatusColumn = \"sprk_invoicestatus\";") }));
    }

    [Fact(DisplayName = "ebilling-r1 015 transition control: a pasted duplicate of an exempt line is a violation (an exemption covers one line)")]
    public void TheDetector_FiresOnADuplicatedExemptLine()
    {
        foreach (var (file, line) in TransitionalExemptions)
        {
            // one copy: exempt, used once
            var (foundOnce, hitsOnce) = ScanLines(file, new[] { "// before", "            " + line, "// after" });
            Assert.Empty(foundOnce);
            Assert.Empty(ExemptionUsageProblems(hitsOnce));

            // a pasted duplicate (same indentation or not): every copy still passes the per-line scan, the usage count fails
            var (foundTwice, hitsTwice) = ScanLines(file, new[] { "            " + line, "            " + line });
            Assert.Empty(foundTwice);
            Assert.NotEmpty(ExemptionUsageProblems(hitsTwice));
            Assert.NotEmpty(ExemptionUsageProblems(ScanLines(file, new[] { line, "        " + line }).ExemptHits));
        }

        // the count is summed over the whole scan: one copy in each of two scans of the same file also fails
        var (_, a) = ScanLines(TransitionalExemptions[0].File, new[] { TransitionalExemptions[0].Line });
        var (_, b) = ScanLines(TransitionalExemptions[0].File, new[] { TransitionalExemptions[0].Line });
        Assert.Empty(ExemptionUsageProblems(a));
        Assert.NotEmpty(ExemptionUsageProblems(a.Zip(b, (x, y) => x + y).ToArray()));
    }

    [Fact(DisplayName = "ebilling-r1 015 transition control: the exact exempt entries do not fire")]
    public void TheDetector_StaysQuietOnTheExactTransitionalEntries() =>
        Assert.All(TransitionalExemptions, e => Assert.Empty(Violations(e.File, "            " + e.Line)));

    [Fact(DisplayName = "ebilling-r1 015 transition control: the old column name anywhere else still fires")]
    public void TheDetector_StillFires_OnTheRetiredNameAnywhereElse()
    {
        var module = TransitionalExemptions[0].File;
        var tests = TransitionalExemptions[1].File;
        var entry = TransitionalExemptions[0].Line;

        // another file, even with the exact exempt line
        Assert.NotEmpty(Violations("src/server/api/Sprk.Bff.Api/Services/Finance/FinanceSummaryService.cs", entry));
        Assert.NotEmpty(Violations("src/client/external-spa/src/components/detail/RecordDetailFields.tsx", entry));
        Assert.NotEmpty(Violations(tests, entry));
        // an exempt file, another line naming the column (a second reader next to the allow-list entry)
        Assert.NotEmpty(Violations(module, "var status = row[\"sprk_invoicestatus\"];"));
        Assert.NotEmpty(Violations(module, "\"sprk_invoicestatus\","));
        Assert.NotEmpty(Violations(module, "\"sprk_invoicestatus\", // transitional (015/D-97)"));
        Assert.NotEmpty(Violations(module, "\"sprk_invoicestatus\", \"sprk_name\", // transitional (015/D-97): remove after the dev column delete"));
        Assert.NotEmpty(Violations(tests, "private const string Other = \"sprk_invoicestatus\";"));
        // the retired constants stay forbidden in the exempt files
        Assert.NotEmpty(Violations(module, "FinanceBaseValues.InvoiceStatus"));
    }

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
    }
}
