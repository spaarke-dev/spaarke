using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2799 (unified-access-control-r2) — the invoice family's core-record lookups and invoice links are written ONLY by the BFF
/// application user. Three guards keep that true: the field-security script locks exactly the BFF's set, no shipped client
/// writes a locked column on a family table, and every caller-identity write in the BFF goes through
/// <see cref="InvoiceFamilyLockedWrite"/> (check as the person, write as the app).
/// </summary>
/// <remarks>
/// <para><b>The one list.</b> <c>scripts/common/InvoiceFamilyLockedColumns.psd1</c> is read by
/// <c>scripts/Set-DocumentPointerFieldSecurity.ps1 -Target InvoiceFamilyLinks</c> (the lock) and by
/// <c>scripts/common/Find-ClientInvoiceFamilyWrite.ps1</c> (the client detector); it must equal
/// <see cref="InvoiceFamilyLockedWrite.LockedColumns"/> (derived from <see cref="InvoiceFamilyCoreRecordGuard"/>) and
/// <see cref="InvoiceFamilyLockedWrite.EntitySets"/>, both ways.</para>
/// <para><b>One detector, two runners.</b> The client guard runs THE SCRIPT'S detector through pwsh (no C# copy to drift):
/// the same file the lock's precondition (p4a) runs over every deployed web resource.</para>
/// <para><b>Caller identity.</b> The caller-identity write APIs are the caller's own PATCH (<see cref="IDataverseUserClient.PatchAsync(string, string, CancellationToken)"/>,
/// OBO), the field-dictionary updates of <see cref="IFieldMappingDataverseService"/> (which take an impersonated
/// <c>systemuserid</c>, <c>MSCRMCallerID</c>) and <see cref="InvoiceFamilyCoreRecordGuard.UpdateExistingAsync"/> (its
/// impersonating write primitive). The scan reuses <see cref="CoreRecordPairUpdateCheckGuardTests"/>' machinery (its
/// <see cref="IlCallScan"/>-based per-source-member classifier and <see cref="CoreRecordPairUpdateCheckGuardTests.SourceMember"/>):
/// a member that calls one of them passes when it also calls the split helper, or is listed with a reason it cannot carry a
/// locked column. Closed list, no baseline: a stale entry fails.</para>
/// <para>Enforced by: these ArchTests (CI and the Stop hook). Task note: <c>projects/unified-access-control-r2/notes/task-2799-invoice-family-field-lock.md</c> §5-§6.</para>
/// </remarks>
public class InvoiceFamilyFieldLockGuardTests
{
    private static string ListPath => Path.Combine(SourceScan.RepoRoot, "scripts", "common", "InvoiceFamilyLockedColumns.psd1");

    private static string DetectorPath => Path.Combine(SourceScan.RepoRoot, "scripts", "common", "Find-ClientInvoiceFamilyWrite.ps1");

    // ── 1. The script's list equals the BFF's set ────────────────────────────────────────────────────────────────

    private static readonly Regex ListEntry = new(
        @"^\s*(?<t>sprk_\w+)\s*=\s*@\{\s*EntitySet\s*=\s*'(?<s>\w+)'\s*;\s*Columns\s*=\s*@\((?<c>[^)]*)\)\s*\}\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>The (table, column) pairs and entity sets of the psd1 text.</summary>
    internal static (IReadOnlySet<string> Pairs, IReadOnlyDictionary<string, string> Sets) ParseList(string text)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        var sets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in ListEntry.Matches(text))
        {
            var table = m.Groups["t"].Value;
            sets[table] = m.Groups["s"].Value;
            foreach (Match c in Regex.Matches(m.Groups["c"].Value, @"'(sprk_\w+)'"))
                pairs.Add($"{table}.{c.Groups[1].Value}");
        }

        return (pairs, sets);
    }

    /// <summary>Every difference between the script's list and the BFF's set, both ways (columns and entity sets).</summary>
    internal static List<string> ListDifferences(
        IReadOnlySet<string> scriptPairs, IReadOnlyDictionary<string, string> scriptSets,
        IReadOnlySet<string> codePairs, IReadOnlyDictionary<string, string> codeSets)
    {
        var problems = new List<string>();
        problems.AddRange(codePairs.Except(scriptPairs).Order(StringComparer.Ordinal)
            .Select(p => $"{p} is locked by the BFF (InvoiceFamilyLockedWrite.LockedColumns) but missing from the script's list"));
        problems.AddRange(scriptPairs.Except(codePairs).Order(StringComparer.Ordinal)
            .Select(p => $"{p} is in the script's list but not locked by the BFF (InvoiceFamilyLockedWrite.LockedColumns)"));
        foreach (var (table, set) in scriptSets.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (!codeSets.TryGetValue(table, out var codeSet))
                problems.Add($"{table}: the script names entity set '{set}', the BFF names none");
            else if (!string.Equals(codeSet, set, StringComparison.Ordinal))
                problems.Add($"{table}: the script names entity set '{set}', the BFF '{codeSet}'");
        }

        foreach (var table in codeSets.Keys.Except(scriptSets.Keys).Order(StringComparer.Ordinal))
            problems.Add($"{table}: a family table of the BFF with no entry in the script's list");
        return problems;
    }

    private static (IReadOnlySet<string> Pairs, IReadOnlyDictionary<string, string> Sets) CodeSet()
    {
        var pairs = InvoiceFamilyLockedWrite.LockedColumns.Select(c => $"{c.Table}.{c.Column}").ToHashSet(StringComparer.Ordinal);
        var tables = InvoiceFamilyLockedWrite.LockedColumns.Select(c => c.Table).ToHashSet(StringComparer.Ordinal);
        var sets = InvoiceFamilyLockedWrite.EntitySets.Where(s => tables.Contains(s.Key))
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        return (pairs, sets);
    }

    [Fact(DisplayName = "#2799: the field-security script locks exactly InvoiceFamilyLockedWrite.LockedColumns (both ways, with the entity sets)")]
    public void TheScriptsList_EqualsTheBffsLockedColumns()
    {
        var (scriptPairs, scriptSets) = ParseList(File.ReadAllText(ListPath));
        var (codePairs, codeSets) = CodeSet();
        Assert.True(codePairs.Count == 24 && codeSets.Count == 6,
            $"the BFF's set reads as {codePairs.Count} columns on {codeSets.Count} tables, not 24 on 6 — re-derive §1 of the task note");
        Assert.True(scriptPairs.Count > 0 && scriptSets.Count > 0, $"{ListPath} parsed to nothing — its format changed");

        var problems = ListDifferences(scriptPairs, scriptSets, codePairs, codeSets);
        Assert.True(problems.Count == 0,
            "scripts/common/InvoiceFamilyLockedColumns.psd1 (what Set-InvoiceFamilyFieldSecurity.ps1 locks and the client detector "
            + "scans for) must equal InvoiceFamilyLockedWrite.LockedColumns and its entity sets:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "#2799: the lock's target takes its columns from the one list, and the entry point names that target")]
    public void TheLockTarget_ReadsTheOneList()
    {
        var engine = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "Set-DocumentPointerFieldSecurity.ps1"));
        Assert.Matches(@"\.\s*\(Join-Path \$PSScriptRoot 'common/Find-ClientInvoiceFamilyWrite\.ps1'\)", engine);
        Assert.Matches(@"InvoiceFamilyLinks\s*=\s*@\{\s*\r?\n\s*Pairs\s*=\s*@\(\$InvoiceFamilyLockedColumns\.Keys", engine);
        Assert.Matches(@"Find-InvoiceFamilyWrite -Text \$text -NavigationNames \$navigationNames", engine);
        var detector = File.ReadAllText(DetectorPath);
        Assert.Contains("Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'InvoiceFamilyLockedColumns.psd1')", detector, StringComparison.Ordinal);
        var entry = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "Set-InvoiceFamilyFieldSecurity.ps1"));
        Assert.Contains("Target             = 'InvoiceFamilyLinks'", entry, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "#2799 control: a seeded extra column, a missing column and a wrong entity set are each reported (must fire); the real list is not (must not fire)")]
    public void ListComparison_MustFireAndMustNotFire()
    {
        var (codePairs, codeSets) = CodeSet();
        var (scriptPairs, scriptSets) = ParseList(File.ReadAllText(ListPath));
        Assert.Empty(ListDifferences(scriptPairs, scriptSets, codePairs, codeSets));

        var text = File.ReadAllText(ListPath);
        var extra = Regex.Replace(text, @"(sprk_invoice\s*=\s*@\{[^\r\n]*Columns = @\()", "$1'sprk_name', ");
        Assert.Contains(ListDifferences(ParseList(extra).Pairs, ParseList(extra).Sets, codePairs, codeSets),
            p => p.StartsWith("sprk_invoice.sprk_name is in the script's list", StringComparison.Ordinal));

        var missing = text.Replace("'sprk_supersedesinvoice'", "'sprk_matter'", StringComparison.Ordinal);
        Assert.Contains(ListDifferences(ParseList(missing).Pairs, ParseList(missing).Sets, codePairs, codeSets),
            p => p.StartsWith("sprk_invoice.sprk_supersedesinvoice is locked by the BFF", StringComparison.Ordinal));

        var wrongSet = text.Replace("'sprk_billingevents'", "'sprk_billingevent'", StringComparison.Ordinal);
        Assert.Contains(ListDifferences(ParseList(wrongSet).Pairs, ParseList(wrongSet).Sets, codePairs, codeSets),
            p => p.StartsWith("sprk_billingevent: the script names entity set 'sprk_billingevent'", StringComparison.Ordinal));
    }

    // ── 2. No client writes a locked column on a family table ────────────────────────────────────────────────────

    /// <summary>
    /// The ratchet baseline: committed bundles that still write a locked column on a family table, with their hit count. A
    /// new violation (an unlisted file, or more hits) fails; an entry that no longer violates (fewer hits) fails as stale.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (int Hits, string Reason)> Baseline = new Dictionary<string, (int, string)>(StringComparer.Ordinal)
    {
        ["src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_creatematterwizard"] =
            (1, "stale committed bundle; removed when rebuilt from 045g source (the Create Matter wizard's invoice association, updateRecord sprk_invoice sprk_Matter@odata.bind)"),
        ["src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_spaarkeai"] =
            (2, "stale committed bundle; removed when rebuilt from 045g source (the embedded Create Matter wizard's invoice association and the Create Invoice wizard's direct createRecord sprk_invoice)"),
        ["src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_createinvoicewizard"] =
            (1, "stale committed bundle; removed when rebuilt from 045g source (the Create Invoice wizard's direct createRecord sprk_invoice, payload built at run time)"),
    };

    /// <summary>The committed web resources (extensionless JavaScript / HTML bundles, not their .data.xml or images).</summary>
    private static IEnumerable<string> CommittedWebResources()
    {
        var root = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");
        if (!Directory.Exists(root))
            return [];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => f.Replace('\\', '/').Contains("/WebResources/", StringComparison.Ordinal))
            .Where(f => !f.EndsWith(".data.xml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Regex.IsMatch(f, @"\.(png|svg|gif|jpg|jpeg|ico|xsl|xml|resx|css)$", RegexOptions.IgnoreCase));
    }

    /// <summary>Every client file the guard covers: client and solution source and PCF bundles, and the committed web resources.</summary>
    internal static IReadOnlyList<string> ScannedFiles() =>
        ClientDocumentPointerWriteGuardTests.ClientFiles()
            .Where(f =>
            {
                var rel = Path.GetRelativePath(SourceScan.RepoRoot, f).Replace('\\', '/');
                return rel.StartsWith("src/client/", StringComparison.Ordinal) || rel.StartsWith("src/solutions/", StringComparison.Ordinal);
            })
            .Concat(CommittedWebResources())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The pre-filter the detector applies first (it names a family table or entity set literally; every shape needs one):
    /// applied here too, so only those files are sent to pwsh. Built from the one list.
    /// </summary>
    private static Regex Prefilter() =>
        new("(?i)(?:" + string.Join("|", ParseList(File.ReadAllText(ListPath)).Sets.Keys.Order(StringComparer.Ordinal).Select(Regex.Escape)) + ")",
            RegexOptions.Compiled);

    internal sealed record DetectorHit(string Table, string Shape, string Detail);

    /// <summary>Runs <c>Find-ClientInvoiceFamilyWrite.ps1</c> through pwsh over <paramref name="texts"/>; the hits of each.</summary>
    internal static async Task<IReadOnlyList<IReadOnlyList<DetectorHit>>> RunDetectorAsync(IReadOnlyList<string> texts)
    {
        Assert.True(File.Exists(DetectorPath), $"{DetectorPath} is missing — Set-DocumentPointerFieldSecurity.ps1 dot-sources it");
        var work = Path.Combine(Path.GetTempPath(), "sprk-invoice-family-detector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var inFile = Path.Combine(work, "texts.json");
            var outFile = Path.Combine(work, "hits.json");
            var runner = Path.Combine(work, "run.ps1");
            File.WriteAllText(inFile, JsonSerializer.Serialize(texts));
            File.WriteAllText(runner,
                "param([string]$Detector, [string]$In, [string]$Out)\n"
                + "$ErrorActionPreference = 'Stop'\n"
                + ". $Detector\n"
                + "$result = @(foreach ($t in @(Get-Content -Raw -LiteralPath $In | ConvertFrom-Json)) {\n"
                + "  $hits = Find-InvoiceFamilyWrite -Text $t\n"
                + "  , @(foreach ($h in $hits) { [pscustomobject]@{ table = $h.Table; shape = $h.Shape; detail = $h.Detail } })\n"
                + "})\n"
                + "ConvertTo-Json -InputObject $result -Depth 5 -Compress | Set-Content -LiteralPath $Out -Encoding utf8\n");

            var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", runner, "-Detector", DetectorPath, "-In", inFile, "-Out", outFile })
                start.ArgumentList.Add(arg);

            Process process;
            try
            {
                process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException("pwsh (PowerShell 7) is required to run the invoice-family client-write detector; install it.", ex);
            }

            using (process)
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300));
                await process.WaitForExitAsync(timeout.Token);
                var output = await stdout;
                var errors = await stderr;
                Assert.True(process.ExitCode == 0, $"pwsh failed ({process.ExitCode}): {errors} {output}");
            }

            using var json = JsonDocument.Parse(File.ReadAllText(outFile).Trim().TrimStart('﻿'));
            var rows = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.EnumerateArray().ToList() : [json.RootElement];
            var result = rows.Select(row =>
                    (IReadOnlyList<DetectorHit>)(row.ValueKind == JsonValueKind.Array ? row.EnumerateArray().ToList() : row.ValueKind == JsonValueKind.Object ? [row] : [])
                    .Select(h => new DetectorHit(h.GetProperty("table").GetString()!, h.GetProperty("shape").GetString()!, h.GetProperty("detail").GetString()!))
                    .ToList())
                .ToList();
            Assert.True(result.Count == texts.Count, $"the detector answered {result.Count} texts of {texts.Count}");
            return result;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // a temp folder left behind is harmless
            }
        }
    }

    /// <summary>Every problem of the per-file hit counts against <see cref="Baseline"/>: new violations and stale entries.</summary>
    internal static List<string> RatchetProblems(IReadOnlyDictionary<string, int> hitsByFile, IReadOnlyDictionary<string, (int Hits, string Reason)> baseline)
    {
        var problems = new List<string>();
        foreach (var (file, hits) in hitsByFile.Where(h => h.Value > 0).OrderBy(h => h.Key, StringComparer.Ordinal))
        {
            if (!baseline.TryGetValue(file, out var entry))
                problems.Add($"NEW {file}: {hits} client write(s) of a locked invoice-family column");
            else if (hits > entry.Hits)
                problems.Add($"NEW {file}: {hits} client write(s), {entry.Hits} baselined");
        }

        foreach (var (file, entry) in baseline.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            var hits = hitsByFile.TryGetValue(file, out var h) ? h : 0;
            if (hits < entry.Hits)
                problems.Add($"STALE baseline {file}: {hits} write(s) now, {entry.Hits} baselined — lower or remove the entry");
        }

        return problems;
    }

    [Fact(DisplayName = "#2799: no client source or committed bundle writes a locked column on an invoice-family table (ratchet baseline)")]
    public async Task NoClientWritesALockedInvoiceFamilyColumn()
    {
        var files = ScannedFiles();
        Assert.True(files.Count > 1000, $"only {files.Count} client files were scanned — the scan roots are wrong, and an empty scan proves nothing");
        Assert.True(files.Count(f => f.Replace('\\', '/').Contains("/WebResources/", StringComparison.Ordinal)) > 50,
            "the committed web resources were not scanned — they are what gets deployed");

        var prefilter = Prefilter();
        var candidates = files.Select(f => (File: f, Text: File.ReadAllText(f))).Where(f => prefilter.IsMatch(f.Text)).ToList();
        Assert.True(candidates.Count > 0, "no client file names an invoice-family table — the pre-filter is wrong");
        foreach (var b in Baseline.Keys)
            Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot, b)), $"baselined {b} is gone — remove its entry");

        var hits = await RunDetectorAsync(candidates.Select(c => c.Text).ToList());
        var byFile = candidates.Select((c, i) => (Rel: Path.GetRelativePath(SourceScan.RepoRoot, c.File).Replace('\\', '/'), Hits: hits[i]))
            .ToDictionary(x => x.Rel, x => x.Hits.Count, StringComparer.Ordinal);
        var detail = candidates.Select((c, i) => (Rel: Path.GetRelativePath(SourceScan.RepoRoot, c.File).Replace('\\', '/'), Hits: hits[i]))
            .Where(x => x.Hits.Count > 0)
            .Select(x => $"  {x.Rel}: " + string.Join("; ", x.Hits.Select(h => $"{h.Table} {h.Shape} ({h.Detail})")));

        var problems = RatchetProblems(byFile, Baseline);
        Assert.True(problems.Count == 0,
            "The invoice family's core-record lookups and invoice links are written ONLY by the BFF application user (#2799, field-level "
            + "security). A client must not create or update a family table with one of them: send the write to the BFF "
            + "(bffChildWriteAdapter / POST|PATCH /api/v1/child-records/{table}), which checks as the person and writes as the app. "
            + "A committed bundle listed here must be rebuilt from current source. Problems:\n" + string.Join("\n", problems)
            + "\nHits:\n" + string.Join("\n", detail));
    }

    // Each case is ONE shape. The must-fire cases include the seeded write the brief names; the must-not-fire cases include the
    // same column on sprk_event.
    public static TheoryData<string> WriteCases => new()
    {
        "Xrm.WebApi.updateRecord(\"sprk_invoice\", id, {\"sprk_Matter@odata.bind\": \"/sprk_matters(\" + m + \")\"});",
        "await t.updateRecord(\"sprk_invoice\",o,{\"sprk_Matter@odata.bind\":`/sprk_matters(${i})`})",                 // minified
        "await Xrm.WebApi.updateRecord(\n  \"sprk_invoice\",\n  invoiceId,\n  {\n    \"sprk_Matter@odata.bind\": `/sprk_matters(${matterId})`,\n  },\n);", // formatted
        "Xrm.WebApi.updateRecord('sprk_billingevent', id, { 'sprk_Invoice@odata.bind': '/sprk_invoices(' + i + ')' });",
        "Xrm.WebApi.updateRecord(\"sprk_invoicesubmission\", id, { \"sprk_RegardingMatter@odata.bind\": b });",
        "Xrm.WebApi.updateRecord(\"sprk_invoiceapproval\", id, { \"sprk_regardingproject@odata.bind\": b });",         // any case
        "Xrm.WebApi.updateRecord(\"sprk_invoicecorrection\", id, { sprk_regardingworkassignment: null });",            // a clear
        "Xrm.WebApi.updateRecord(\"sprk_invoice\", id, { \"sprk_SupersedesInvoice@odata.bind\": s });",
        "const T = \"sprk_externalidentifier\"; Xrm.WebApi.updateRecord(T, id, { \"sprk_RegardingInvoice@odata.bind\": b });", // bound name
        "let a=1,E=\"sprk_invoice\";w.createRecord(E,{\"sprk_project@odata.bind\":p})",                                 // bound, minified
        "Xrm.WebApi.createRecord(\"sprk_invoice\", payload);",                                                        // unreadable payload
        "a=await this._dataService.createRecord(\"sprk_invoice\",c)",                                                  // the live bundle's shape
        "Xrm.WebApi.updateRecord(\"sprk_invoice\", id, { ...base, sprk_name: n });",                                   // spread
        "Xrm.WebApi.updateRecord(\"sprk_invoice\", id, { [key]: v });",                                                // computed key
        "fetch(`${api}/sprk_invoices(${id})`, { method: \"PATCH\", headers: h, body: JSON.stringify({ \"sprk_Project@odata.bind\": p }) });",
        "fetch(url + \"/sprk_invoiceapprovals\", { method: 'POST', body: JSON.stringify(body) });",                   // unreadable body
        "await fetch(`${base}/api/data/v9.2/sprk_billingevents(${id})`, { method: \"PATCH\", body: payload });",       // body not stringify
        "xhr.open(\"PATCH\", base + \"/api/data/v9.2/sprk_invoicesubmissions(\" + id + \")\", true);",                // XMLHttpRequest
        "const line = \"PATCH /api/data/v9.2/sprk_invoices(\" + id + \") HTTP/1.1\";",                                // $batch change set
    };

    public static TheoryData<string> ReadCases => new()
    {
        "Xrm.WebApi.updateRecord(\"sprk_event\", id, {\"sprk_Matter@odata.bind\": \"/sprk_matters(\" + m + \")\"});",   // same column, other table
        "Xrm.WebApi.createRecord(\"sprk_event\", { \"sprk_Invoice@odata.bind\": \"/sprk_invoices(\" + i + \")\" });",   // a bind TO an invoice
        "Xrm.WebApi.updateRecord(\"sprk_document\", id, { sprk_matter: m, sprk_invoice: i });",
        "Xrm.WebApi.updateRecord(\"sprk_invoice\", id, { sprk_name: \"x\", sprk_description: d });",                  // no locked column
        "Xrm.WebApi.updateRecord(\"sprk_invoice\", id, { sprk_mattername: \"x\" });",                                 // a longer name
        "Xrm.WebApi.updateRecord(\"sprk_invoicesubmission\", id, { \"sprk_SourceEmailDocument@odata.bind\": d });",    // its own other lookup
        "Xrm.WebApi.retrieveRecord(\"sprk_invoice\", id, \"?$select=_sprk_matter_value\");",
        "Xrm.WebApi.retrieveMultipleRecords(\"sprk_billingevent\", \"?$filter=_sprk_invoice_value eq \" + id);",
        "fetch(`${api}/sprk_invoices(${id})?$select=_sprk_matter_value`, { method: \"GET\" });",
        "fetch(`${bff}/api/v1/child-records/sprk_invoice/${id}`, { method: \"PATCH\", body: JSON.stringify(body) });", // the BFF route
        "fetch(`${api}/sprk_events`, { method: \"POST\", body: JSON.stringify({ \"sprk_Invoice@odata.bind\": `/sprk_invoices(${id})` }) });",
        "const r = { \"sprk_Invoice@odata.bind\": `/sprk_invoices(${id})` }; fetch(`${api}/sprk_todos`, { method: \"POST\", body: JSON.stringify(r) });",
        "const cfg = { entityType: \"sprk_invoice\", entitySet: \"sprk_invoices\", lookupAttribute: \"sprk_regardinginvoice\" };",
        "if (n === \"sprk_invoice\") { await t.updateRecord(\"sprk_matter\", i, { sprk_name: x }); }",                  // a comparison, not a binding
        "const T = \"sprk_invoice\"; Xrm.WebApi.updateRecord(T2, id, { sprk_matter: m });",                             // another name
    };

    [Fact(DisplayName = "#2799 control: the detector fires on every seeded family-table write (must fire) and on no read or other-table write (must not fire)")]
    public async Task Detector_MustFireAndMustNotFire()
    {
        var writes = WriteCases.Select(r => (string)r[0]).ToList();
        var reads = ReadCases.Select(r => (string)r[0]).ToList();
        Assert.True(writes.Count >= 15 && reads.Count >= 15, "the case lists were not read");

        var hits = await RunDetectorAsync(writes.Concat(reads).ToList());
        var missed = writes.Where((_, i) => hits[i].Count == 0).ToList();
        var firedOnReads = reads.Where((_, i) => hits[writes.Count + i].Count > 0).ToList();
        Assert.True(missed.Count == 0 && firedOnReads.Count == 0,
            "Find-ClientInvoiceFamilyWrite.ps1 must fire on every write case and on no read case.\nMissed writes:\n"
            + string.Join("\n", missed) + "\nFired on reads:\n" + string.Join("\n", firedOnReads));

        // The table each write names (the first case: the seeded write of the brief).
        Assert.Equal("sprk_invoice", hits[0][0].Table);
    }

    [Fact(DisplayName = "#2799 control: the ratchet reports an unlisted file, a grown count and a stale entry (must fire); the baseline as is does not (must not fire)")]
    public void Ratchet_MustFireAndMustNotFire()
    {
        var baseline = new Dictionary<string, (int, string)>(StringComparer.Ordinal) { ["a"] = (2, "r") };
        Assert.Empty(RatchetProblems(new Dictionary<string, int> { ["a"] = 2, ["b"] = 0 }, baseline));
        Assert.Contains(RatchetProblems(new Dictionary<string, int> { ["a"] = 2, ["b"] = 1 }, baseline), p => p.StartsWith("NEW b:", StringComparison.Ordinal));
        Assert.Contains(RatchetProblems(new Dictionary<string, int> { ["a"] = 3 }, baseline), p => p.StartsWith("NEW a:", StringComparison.Ordinal));
        Assert.Contains(RatchetProblems(new Dictionary<string, int> { ["a"] = 1 }, baseline), p => p.StartsWith("STALE baseline a:", StringComparison.Ordinal));
        Assert.Contains(RatchetProblems(new Dictionary<string, int>(), baseline), p => p.StartsWith("STALE baseline a:", StringComparison.Ordinal));
    }

    // ── 3. No caller-identity write of a locked column in the BFF bypasses InvoiceFamilyLockedWrite ────────────────

    /// <summary>
    /// The closed list: members that call a caller-identity write API without the split helper, each with the reason it cannot
    /// carry a locked invoice-family column (its table or columns are fixed, or it never acts for a person).
    /// </summary>
    private static readonly IReadOnlyList<(Type Type, string Method, string Reason)> CannotCarryALockedColumn = new (Type, string, string)[]
    {
        // Family tables, but a fixed column set that holds no locked column.
        (typeof(Sprk.Bff.Api.Services.Finance.InvoiceTotalsService), "RecalculateAsync",
            "Writes an adjustment sprk_billingevent's sprk_amount only, app-only (no impersonation argument)."),

        // The create: one app-only write after the caller's checks (task note §2.1 C7).
        (typeof(Sprk.Bff.Api.Services.RecordCreation.RecordCreationService), "CreateAsync",
            "The unified create's one app-only write (no impersonation argument) after CallerWriteAuthorization asks Create/Append and AppendTo as the caller (C7)."),

        // A configured column written as a JSON string: a lookup is set only through its navigation (<nav>@odata.bind).
        (typeof(Sprk.Bff.Api.Services.Ai.TopicRegistryWorkProductPersister), "PersistAsync",
            "Writes one registry-declared column of the host record as a JSON envelope STRING; a locked column is a lookup, which Dataverse sets only from a reference (<nav>@odata.bind), never from a string."),

        // Fixed tables outside the invoice family.
        (typeof(Sprk.Bff.Api.Services.Finance.FinanceRollupService), "WriteRollupAsync",
            "Writes a matter's or project's finance rollup columns; not a family table."),
        (typeof(Sprk.Bff.Api.Services.ScorecardCalculatorService), "UpdateEntityGradesAsync",
            "Writes a matter's or project's six grade columns; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Dataverse.ProjectMatterFiling), "FileAsync",
            "Writes a sprk_project (its other columns as the caller, its regarding pair as the app); not a family table."),
        (typeof(Sprk.Bff.Api.Api.Events.EventDueAssigneeWrite), "ApplyAsync",
            "Writes a sprk_event's due date and assignee columns; not a family table."),
        (typeof(Sprk.Bff.Api.Api.Reporting.ReportingEndpoints), "UpdateReport",
            "Writes a sprk_report row's own columns; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Finance.SignalEvaluationService), "UpsertSignalAsync",
            "Writes a sprk_spendsignal; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminAuthoringService), "SavePolicyAsync",
            "Writes a sprk_policy's current-version pointer; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminAuthoringService), "StampAndMoveAsync",
            "Writes a sprk_policy's pointer and scope; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminAuthoringService), "StampInForceToAsync",
            "Writes a sprk_policyversion's sprk_inforceto only; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminClassificationService), "PatchAsync",
            "Writes a sprk_triagecategory row; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminRulesService), "WriteAsync",
            "Writes a sprk_policy row; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.ExtendResponseDateExecutor), "WriteAsync",
            "Writes a sprk_workassignment's sprk_responseduedate only; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.RecordTheResponseExecutor), "WriteAsync",
            "Writes a sprk_workassignment's sprk_respondedon and sprk_responseoutcome only; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.ReviseBudgetExecutor), "ExecuteAsync",
            "Writes a sprk_budget's sprk_totalbudget only; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.InquiryDispositionService), "CompleteTodoAsync",
            "Writes a sprk_todo's completion columns; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.InquiryDispositionService), "ConfirmSuggestedAssociationAsync",
            "Writes a sprk_communication's association columns; not a family table."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.InquiryDispositionService), "RecordDispositionAsync",
            "Writes a sprk_servicerequest's disposition columns; not a family table."),
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    /// <summary>A caller-identity write API: the caller's PATCH, an impersonable field-dictionary update, or the guard's primitive.</summary>
    private static bool IsCallerIdentityWrite(MethodBase target)
    {
        var declaring = target.DeclaringType;
        if (declaring is null)
            return false;

        return (target.Name == nameof(IDataverseUserClient.PatchAsync) && typeof(IDataverseUserClient).IsAssignableFrom(declaring))
               || (target.Name is nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync)
                       or nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync)
                   && typeof(IFieldMappingDataverseService).IsAssignableFrom(declaring))
               || (target.Name == nameof(InvoiceFamilyCoreRecordGuard.UpdateExistingAsync) && declaring == typeof(InvoiceFamilyCoreRecordGuard));
    }

    /// <summary>The split helper: a member that calls one of these writes the locked columns as the app after the person's check.</summary>
    private static bool IsTheSplit(MethodBase target) =>
        target.DeclaringType == typeof(InvoiceFamilyLockedWrite)
        && target.Name is nameof(InvoiceFamilyLockedWrite.UpdateExistingSplitAsync) or nameof(InvoiceFamilyLockedWrite.PatchAsCallerAsync);

    /// <summary>The APIs themselves and the helper are not callers.</summary>
    private static bool IsExcluded(Type type)
    {
        var outer = IlCallScan.Outermost(type);
        return typeof(IFieldMappingDataverseService).IsAssignableFrom(outer)
               || typeof(IDataverseUserClient).IsAssignableFrom(outer)
               || outer == typeof(InvoiceFamilyLockedWrite);
    }

    /// <summary>The detector: each (outermost type, source member) that calls a caller-identity write API, and whether it splits.</summary>
    internal static IReadOnlyDictionary<(Type Type, string Method), bool> ClassifyCallerWrites(IEnumerable<Type> types)
    {
        var references = IlCallScan.MethodReferences(types.Where(t => !IsExcluded(t)))
            .Select(r => (Key: (IlCallScan.Outermost(r.Caller), CoreRecordPairUpdateCheckGuardTests.SourceMember(r.Caller, r.CallerMethod)), r.Target))
            .ToList();
        var splits = references.Where(r => IsTheSplit(r.Target)).Select(r => r.Key).ToHashSet();
        // A member that writes through the split only is a caller-identity writer too (it is how C4-C6 write now). The guard's
        // own primitive is the API, not a caller (every call of it is held here).
        return references.Where(r => IsCallerIdentityWrite(r.Target) || IsTheSplit(r.Target))
            .Select(r => r.Key)
            .Where(k => !(k.Item1 == typeof(InvoiceFamilyCoreRecordGuard) && k.Item2 == nameof(InvoiceFamilyCoreRecordGuard.UpdateExistingAsync)))
            .Distinct()
            .ToDictionary(k => k, splits.Contains);
    }

    [Fact(DisplayName = "#2799: every BFF caller-identity write (OBO PATCH, impersonable update, the guard's primitive) goes through InvoiceFamilyLockedWrite, or is listed")]
    public void EveryCallerIdentityWrite_GoesThroughTheSplit_OrIsListed()
    {
        var methods = ClassifyCallerWrites(BffTypes.Value);
        var problems = new List<string>();
        foreach (var ((type, method), splits) in methods.OrderBy(m => $"{m.Key.Type.FullName}.{m.Key.Method}", StringComparer.Ordinal))
        {
            var listed = CannotCarryALockedColumn.Any(a => a.Type == type && string.Equals(a.Method, method, StringComparison.Ordinal));
            if (!splits && !listed)
                problems.Add($"{type.FullName}.{method}: a caller-identity write that does not go through InvoiceFamilyLockedWrite");
            if (splits && listed)
                problems.Add($"STALE entry {type.FullName}.{method}: it now goes through InvoiceFamilyLockedWrite; remove it from the list.");
        }

        foreach (var (type, method, _) in CannotCarryALockedColumn)
        {
            if (!methods.ContainsKey((type, method)))
                problems.Add($"STALE entry {type.FullName}.{method}: it no longer calls a caller-identity write API; remove it from the list.");
        }

        // Non-vacuity: the six converted writers (task note §4, C1-C6) are seen, each going through the split.
        foreach (var (type, method) in new[]
                 {
                     (typeof(Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints), "ApplyMappingsToChildRecordsAsync"),
                     (typeof(Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore), "UpdateAsync"),
                     (typeof(Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler), "PatchAsync"),
                     (typeof(Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite), "RefileAsync"),
                     (typeof(Sprk.Bff.Api.Api.ChildRecordEndpoints), "UpdateAsync"),
                     (typeof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler), "ExecuteChatAsync"),
                 })
        {
            if (!methods.TryGetValue((type, method), out var splits) || !splits)
                problems.Add($"{type.FullName}.{method}: expected to be seen writing as the caller through InvoiceFamilyLockedWrite; the scan lost its signal.");
        }

        Assert.True(problems.Count == 0,
            "Once the invoice-family links are field-secured, only the BFF application user may write them (#2799). A BFF method that "
            + "writes as the caller (IDataverseUserClient.PatchAsync, an IFieldMappingDataverseService update that may impersonate, "
            + "InvoiceFamilyCoreRecordGuard.UpdateExistingAsync) goes through InvoiceFamilyLockedWrite.UpdateExistingSplitAsync / "
            + "PatchAsCallerAsync (check as the person, write the locked columns as the app), or is listed in CannotCarryALockedColumn "
            + "with the reason its table or columns are fixed:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "#2799 control: a caller PATCH, an impersonated update and a direct guard write of a family table that bypass the split are found (must fire); through the split, or another API, are not (must not fire)")]
    public void CallerWriteDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(InvoiceFamilyCallerWriteControlPatchBypass), typeof(InvoiceFamilyCallerWriteControlImpersonatedBypass),
            typeof(InvoiceFamilyCallerWriteControlGuardBypass), typeof(InvoiceFamilyCallerWriteControlThroughTheSplit),
            typeof(InvoiceFamilyCallerWriteControlOtherPatchApi),
        };
        var methods = ClassifyCallerWrites(fixtures.SelectMany(IlCallScan.WithNested));

        Assert.Equal(
            new[]
            {
                $"{nameof(InvoiceFamilyCallerWriteControlGuardBypass)}.Write",
                $"{nameof(InvoiceFamilyCallerWriteControlImpersonatedBypass)}.Write",
                $"{nameof(InvoiceFamilyCallerWriteControlPatchBypass)}.Write",
            },
            methods.Where(m => !m.Value).Select(m => $"{m.Key.Type.Name}.{m.Key.Method}").Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(
            new[] { $"{nameof(InvoiceFamilyCallerWriteControlThroughTheSplit)}.Write" },
            methods.Where(m => m.Value).Select(m => $"{m.Key.Type.Name}.{m.Key.Method}").ToArray());

        Assert.DoesNotContain(methods.Keys, k => k.Type == typeof(InvoiceFamilyCallerWriteControlOtherPatchApi));
    }
}

// Compiled control fixtures for InvoiceFamilyFieldLockGuardTests.CallerWriteDetector_MustFireAndMustNotFire. Top-level types (the
// scan attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: the caller's own PATCH of an invoice, carrying its matter, with no split.</summary>
internal static class InvoiceFamilyCallerWriteControlPatchBypass
{
    internal static Task<DataverseUserResponse> Write(IDataverseUserClient user, Guid invoiceId, Guid matterId) =>
        user.PatchAsync($"sprk_invoices({invoiceId:D})", $"{{\"sprk_Matter@odata.bind\":\"/sprk_matters({matterId:D})\"}}", CancellationToken.None);
}

/// <summary>MUST FIRE: an impersonated field-dictionary update of an invoice's matter, with no split.</summary>
internal static class InvoiceFamilyCallerWriteControlImpersonatedBypass
{
    internal static Task Write(IFieldMappingDataverseService records, Guid invoiceId, Guid person, object matter) =>
        records.UpdateExistingRecordFieldsAsync("sprk_invoice", invoiceId, new Dictionary<string, object?> { ["sprk_matter"] = matter }, person);
}

/// <summary>MUST FIRE: the guard's impersonating primitive called directly, bypassing the split.</summary>
internal static class InvoiceFamilyCallerWriteControlGuardBypass
{
    internal static Task<string?> Write(IFieldMappingDataverseService records, Guid invoiceId, Guid person, Dictionary<string, object?> fields) =>
        InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(records, "sprk_invoice", invoiceId, fields, person, notFoundAsReadFailed: true, CancellationToken.None);
}

/// <summary>MUST NOT FIRE: the caller's PATCH through the split (the child-record route's shape).</summary>
internal static class InvoiceFamilyCallerWriteControlThroughTheSplit
{
    internal static Task<InvoiceFamilyLockedWrite.CallerPatch> Write(
        IDataverseUserClient user, IFieldMappingDataverseService app, Guid invoiceId, string body, Microsoft.Extensions.Logging.ILogger logger) =>
        InvoiceFamilyLockedWrite.PatchAsCallerAsync(user, app, "sprk_invoice", "sprk_invoices", invoiceId, body, logger, traceId: null, CancellationToken.None);
}

/// <summary>MUST NOT FIRE: a PatchAsync of another API (as the Graph SDK's) is not the caller's Dataverse PATCH.</summary>
internal static class InvoiceFamilyCallerWriteControlOtherPatchApi
{
    internal sealed class OtherClient
    {
        internal Task PatchAsync(string path, string body, CancellationToken ct) => Task.CompletedTask;
    }

    internal static Task Write(OtherClient client) => client.PatchAsync("sprk_invoices(1)", "{}", CancellationToken.None);
}
