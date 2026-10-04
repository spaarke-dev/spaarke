using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 166 f1 (owner round 21 item 1 (i)) — no CLIENT writes a document's SharePoint Embedded
/// pointer (<c>sprk_graphdriveid</c> / <c>sprk_graphitemid</c>). The client creates the row WITHOUT it and calls the
/// BFF's <c>POST /api/v1/documents/{id}/file</c>, which verifies the file and stamps the pointer as the application.
/// </summary>
/// <remarks>
/// <para><b>Why a fitness function.</b> The pointer is what the BFF follows AS THE APPLICATION on every download, so the
/// columns are field-secured and writable by the BFF identity only (<c>scripts/Set-DocumentPointerFieldSecurity.ps1</c>).
/// Locking them is only safe once no shipped client writes them — every such upload would then be refused — and that
/// script's precondition (p4a) checks the DEPLOYED web resources for exactly the shapes this guard forbids in source. A
/// pointer write re-added anywhere in a client (a wizard, a code page, a PCF, a form script) would either reopen the
/// write the lock exists to close, or break uploads once the lock is applied. This makes it fail the build instead.</para>
/// <para><b>What counts as a write</b> (kept in step with the script's <c>$PointerWritePatterns</c>): an object-literal key
/// <c>sprk_graphdriveid:</c> / <c>sprk_graphitemid:</c> (a create / update payload), or a form <c>setValue</c> on either
/// column. Reads — <c>$select</c> strings, property access, <c>['sprk_graphitemid']</c> lookups, optional TypeScript
/// properties (<c>sprk_graphitemid?:</c>) — never match.</para>
/// <para><b>Scope.</b> Every client source file (<c>.ts</c>, <c>.tsx</c>, <c>.js</c>) under <c>src/client</c>,
/// <c>src/solutions</c> and <c>src/dataverse</c> — INCLUDING the committed PCF solution bundles
/// (<c>Solution/**/bundle.js</c>), which are what gets deployed — and excluding tests, mocks, installed packages and
/// build output.</para>
/// </remarks>
public sealed class ClientDocumentPointerWriteGuardTests
{
    private static readonly Regex[] PointerWrites =
    [
        new(@"[""']?sprk_graph(item|drive)id[""']?\s*:", RegexOptions.Compiled),
        new(@"getAttribute\(\s*[""']sprk_graph(item|drive)id[""']\s*\)\s*\.\s*setValue", RegexOptions.Compiled),
    ];

    private static readonly string[] ExcludedSegments =
    [
        "node_modules", "dist", "out", "obj", "bin", "coverage", "__tests__", "__mocks__", ".vite",
    ];

    /// <summary>The committed PCF bundles that carried the shared upload code before task 166 f1 (the scan must see them).</summary>
    private static readonly string[] RebuiltBundles =
    [
        "CommunicationActions", "CommunicationConnections", "CommunicationConversationPanel", "CommunicationMessageActions",
        "CommunicationTimeline", "CommunicationTimelineRegarding", "TrackingFieldTrio",
    ];

    internal static IReadOnlyList<string> ClientFiles()
    {
        var roots = new[] { "client", "solutions", "dataverse" }
            .Select(r => Path.Combine(SourceScan.RepoRoot, "src", r))
            .Where(Directory.Exists);

        return roots
            .SelectMany(Walk)
            .Where(f => f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Regex.IsMatch(Path.GetFileName(f), @"\.(test|spec)\.[jt]sx?$", RegexOptions.IgnoreCase))
            .ToList();
    }

    /// <summary>Every file under <paramref name="directory"/>, never descending into an excluded directory.</summary>
    private static IEnumerable<string> Walk(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (ExcludedSegments.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Walk(child))
            {
                yield return file;
            }
        }
    }

    internal static IEnumerable<string> Offenders(string text)
        => PointerWrites.Select(p => p.Match(text)).Where(m => m.Success).Select(m => m.Value);

    [Fact(DisplayName = "Task 166 f1: no client source or committed PCF bundle writes a document's SPE pointer")]
    public void NoClientWritesADocumentPointer()
    {
        var files = ClientFiles();
        Assert.True(files.Count > 100, $"only {files.Count} client files were scanned — the scan roots are wrong, and an empty scan proves nothing");

        var violations = files
            .Select(f => (File: Path.GetRelativePath(SourceScan.RepoRoot, f), Hits: Offenders(File.ReadAllText(f)).Distinct().ToList()))
            .Where(v => v.Hits.Count > 0)
            .Select(v => $"{v.File}: {string.Join(", ", v.Hits)}")
            .ToList();

        Assert.True(violations.Count == 0,
            "A client writes sprk_graphdriveid / sprk_graphitemid. The pointer is BFF-written only (field-level security, "
            + "owner round 21 item 1): create the row WITHOUT it and attach the uploaded file with "
            + "SdapApiClient.attachDocumentFile (POST /api/v1/documents/{id}/file). A committed PCF bundle listed here must "
            + "be rebuilt (npm run build:prod) from the fixed shared library. Offenders:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "Task 166 f1: the pointer-write scan covers the deployable PCF bundles")]
    public void TheScanCoversTheDeployablePcfBundles()
    {
        var files = ClientFiles().Select(f => f.Replace('\\', '/')).ToList();

        foreach (var control in RebuiltBundles)
        {
            Assert.Contains(files, f => f.Contains($"/pcf/{control}/Solution/", StringComparison.Ordinal) && f.EndsWith("/bundle.js", StringComparison.Ordinal));
        }
    }

    // Negative and positive controls: the detector fires on every write shape and never on the read shapes.
    [Theory(DisplayName = "Task 166 f1: the detector fires on a pointer write")]
    [InlineData("const payload = { sprk_graphitemid: file.id };")]
    [InlineData("const p={sprk_graphdriveid:e.driveId,sprk_hasfile:!0}")]
    [InlineData("payload[\"sprk_graphitemid\"] = x; const q = { \"sprk_graphdriveid\": d };")]
    [InlineData("formContext.getAttribute(\"sprk_graphitemid\").setValue(id);")]
    [InlineData("formContext.getAttribute('sprk_graphdriveid') . setValue(null);")]
    public void TheDetectorFiresOnAWrite(string source) => Assert.NotEmpty(Offenders(source));

    [Theory(DisplayName = "Task 166 f1: the detector never fires on a read")]
    [InlineData("'?$select=sprk_filename,sprk_graphitemid,sprk_graphdriveid'")]
    [InlineData("const itemId = record.sprk_graphitemid;")]
    [InlineData("speDriveItemId = docRec['sprk_graphitemid'] as string | undefined;")]
    [InlineData("interface Doc { sprk_graphitemid?: string | null; }")]
    [InlineData("const FIELD_GRAPH_ITEM_ID = \"sprk_graphitemid\";")]
    [InlineData("var driveId = formContext.getAttribute(\"sprk_graphdriveid\").getValue();")]
    public void TheDetectorNeverFiresOnARead(string source) => Assert.Empty(Offenders(source));
}
