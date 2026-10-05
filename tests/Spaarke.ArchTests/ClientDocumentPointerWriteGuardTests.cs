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
/// <para><b>What counts as a write</b> (kept in step with the script's <c>$PointerWritePatterns</c> /
/// <c>Find-PointerWrite</c>), for <c>sprk_graphdriveid</c>, <c>sprk_graphitemid</c> and the relocation ledger
/// <c>sprk_relocationpending</c> (task 166 f1-v1; all three BFF-written only):</para>
/// <list type="bullet">
/// <item>an object-literal key <c>sprk_graphitemid:</c> / <c>"sprk_graphdriveid":</c> (a create / update payload);</item>
/// <item>a computed key <c>{ ["sprk_graphitemid"]: … }</c>;</item>
/// <item>a bracket assignment <c>payload['sprk_graphitemid'] = …</c> (also <c>??=</c>, <c>||=</c>, <c>&amp;&amp;=</c>);</item>
/// <item>a dotted assignment <c>e.sprk_graphdriveid = …</c> (minified bundles write this way);</item>
/// <item>a form <c>setValue</c> through <c>getAttribute(…)</c> or <c>attributes.get(…)</c> (also <c>?.setValue</c>);</item>
/// <item>the last three shapes and the computed key THROUGH A CONSTANT bound to a column name
/// (<c>const F = "sprk_graphitemid"; payload[F] = …</c>, <c>{ [F]: … }</c>, <c>getAttribute(FIELDS.ITEM).setValue</c>) —
/// constants bound in the same file, or in any non-bundle client source file (an exported constant).</item>
/// </list>
/// <para>Reads — <c>$select</c> strings, property access, <c>['sprk_graphitemid']</c> lookups, a ternary arm
/// <c>? rec['sprk_graphitemid'] :</c>, comparisons (<c>===</c>, <c>==</c>, <c>!==</c>), optional TypeScript properties
/// (<c>sprk_graphitemid?:</c>) — never match. <b>Not detectable statically</b>: a key built at run time (string
/// concatenation, a loop over a list of names, a value passed in from another module's function). The field-level
/// security lock is the control for those; this guard keeps every shape a person or a bundler actually writes out of
/// the shipped clients.</para>
/// <para><b>Scope.</b> Every client source file (<c>.ts</c>, <c>.tsx</c>, <c>.js</c>) under <c>src/client</c>,
/// <c>src/solutions</c> and <c>src/dataverse</c> — INCLUDING the committed PCF solution bundles
/// (<c>Solution/**/bundle.js</c>), which are what gets deployed — and excluding tests, mocks, installed packages and
/// build output.</para>
/// </remarks>
public sealed class ClientDocumentPointerWriteGuardTests
{
    /// <summary>The BFF-written columns: the two pointer columns and the relocation ledger.</summary>
    private const string Columns = "sprk_(?:graph(?:item|drive)id|relocationpending)";

    private static readonly Regex[] PointerWrites =
    [
        // object-literal key: { sprk_graphitemid: … } / { "sprk_graphdriveid": … }
        new($@"[""']?{Columns}[""']?\s*:", RegexOptions.Compiled),
        // computed key: { ["sprk_graphitemid"]: … }
        new($@"[{{,]\s*\[\s*[""'`]{Columns}[""'`]\s*\]\s*:", RegexOptions.Compiled),
        // bracket assignment: payload["sprk_graphitemid"] = …
        new($@"\[\s*[""'`]{Columns}[""'`]\s*\]\s*(?:\?\?|\|\||&&)?=(?![=>])", RegexOptions.Compiled),
        // dotted assignment: e.sprk_graphdriveid = …
        new($@"\.\s*{Columns}\s*(?:\?\?|\|\||&&)?=(?![=>])", RegexOptions.Compiled),
        // form setValue: getAttribute("sprk_graphitemid").setValue(…) / attributes.get(…)?.setValue(…)
        new($@"(?:getAttribute|attributes\s*\.\s*get)\(\s*[""'`]{Columns}[""'`]\s*\)\s*\??\.\s*setValue", RegexOptions.Compiled),
    ];

    /// <summary>A name bound to a column-name string: <c>const F = "sprk_graphitemid"</c> or <c>{ ITEM: 'sprk_graphitemid' }</c>.</summary>
    private static readonly Regex ColumnNameBinding =
        new($@"(?<![\w$.])([A-Za-z_$][\w$]*)\s*[=:]\s*[""'`]{Columns}[""'`]", RegexOptions.Compiled);

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

    /// <summary>The names <paramref name="text"/> binds to a column-name string.</summary>
    internal static IEnumerable<string> ColumnNameConstants(string text)
        => ColumnNameBinding.Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal);

    /// <summary>The write shapes that go THROUGH a name bound to a column (computed key, bracket assignment, setValue).</summary>
    private static IEnumerable<Regex> WritesThrough(string name)
    {
        var path = $@"(?:[\w$]+\s*\.\s*)*{Regex.Escape(name)}(?![\w$])";
        yield return new Regex($@"[{{,]\s*\[\s*{path}\s*\]\s*:");
        yield return new Regex($@"\[\s*{path}\s*\]\s*(?:\?\?|\|\||&&)?=(?![=>])");
        yield return new Regex($@"(?:getAttribute|attributes\s*\.\s*get)\(\s*{path}\s*\)\s*\??\.\s*setValue");
    }

    /// <summary>
    /// Every pointer write in <paramref name="text"/>: the literal shapes, and the shapes through a constant bound in this
    /// text or named in <paramref name="sharedConstants"/> (constants bound in other client source files).
    /// </summary>
    internal static IEnumerable<string> Offenders(string text, IEnumerable<string>? sharedConstants = null)
    {
        foreach (var hit in PointerWrites.Select(p => p.Match(text)).Where(m => m.Success))
        {
            yield return hit.Value;
        }

        foreach (var name in ColumnNameConstants(text).Concat(sharedConstants ?? []).Distinct(StringComparer.Ordinal))
        {
            foreach (var hit in WritesThrough(name).Select(p => p.Match(text)).Where(m => m.Success))
            {
                yield return hit.Value;
            }
        }
    }

    private static bool IsCommittedBundle(string file) => file.EndsWith("bundle.js", StringComparison.OrdinalIgnoreCase);

    [Fact(DisplayName = "Task 166 f1: no client source or committed PCF bundle writes a document's SPE pointer")]
    public void NoClientWritesADocumentPointer()
    {
        var files = ClientFiles();
        Assert.True(files.Count > 100, $"only {files.Count} client files were scanned — the scan roots are wrong, and an empty scan proves nothing");

        // Constants a SOURCE file binds to a column name may be imported anywhere; a minified bundle's one-letter names are
        // meaningful only inside that bundle, so they are matched per file.
        var texts = files.ToDictionary(f => f, File.ReadAllText);
        var shared = files.Where(f => !IsCommittedBundle(f))
            .SelectMany(f => ColumnNameConstants(texts[f]))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var violations = files
            .Select(f => (File: Path.GetRelativePath(SourceScan.RepoRoot, f), Hits: Offenders(texts[f], IsCommittedBundle(f) ? null : shared).Distinct().ToList()))
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

    // Negative and positive controls: the detector fires on every write shape and never on the read shapes. Each write
    // case is ONE shape on its own (task 166 f1-v1, F3: a case that combined a bracket write with an object key passed on
    // the object key alone and proved nothing about the bracket write).
    [Theory(DisplayName = "Task 166 f1: the detector fires on a pointer write")]
    [InlineData("const payload = { sprk_graphitemid: file.id };")]                                  // object key
    [InlineData("const p={sprk_graphdriveid:e.driveId,sprk_hasfile:!0}")]                          // object key, minified
    [InlineData("const q = { \"sprk_graphdriveid\": d };")]                                         // quoted object key
    [InlineData("payload[\"sprk_graphitemid\"] = x;")]                                              // bracket assignment
    [InlineData("payload['sprk_graphdriveid']=driveId;")]                                          // bracket, single quotes
    [InlineData("payload[`sprk_graphitemid`] ??= id;")]                                            // bracket, compound
    [InlineData("e.sprk_graphdriveid=t")]                                                          // dotted, minified
    [InlineData("record.sprk_graphitemid = item.id;")]                                             // dotted
    [InlineData("payload.sprk_graphitemid ||= id;")]                                               // dotted, compound
    [InlineData("const body = { [\"sprk_graphitemid\"]: id };")]                                   // computed literal key
    [InlineData("const F = \"sprk_graphitemid\"; const body = { [F]: id };")]                      // computed key via constant
    [InlineData("const FIELD_GRAPH_ITEM_ID = \"sprk_graphitemid\"; payload[FIELD_GRAPH_ITEM_ID] = id;")] // bracket via constant
    [InlineData("const n=\"sprk_graphdriveid\";function u(e,t){return{[n]:t}}")]                   // computed via constant, minified
    [InlineData("const FIELDS = { ITEM: 'sprk_graphitemid' }; formContext.getAttribute(FIELDS.ITEM).setValue(id);")] // setValue via member constant
    [InlineData("formContext.getAttribute(\"sprk_graphitemid\").setValue(id);")]                   // form setValue
    [InlineData("formContext.getAttribute('sprk_graphdriveid') . setValue(null);")]               // form setValue, spaced
    [InlineData("formContext.getAttribute(\"sprk_graphitemid\")?.setValue(id);")]                  // optional chaining
    [InlineData("formContext.data.entity.attributes.get(\"sprk_graphitemid\").setValue(id);")]     // attributes.get
    [InlineData("row.sprk_relocationpending = \"{}\";")]                                           // the relocation ledger
    [InlineData("const ledger = { sprk_relocationpending: json };")]                               // the ledger, object key
    public void TheDetectorFiresOnAWrite(string source) => Assert.NotEmpty(Offenders(source));

    [Fact(DisplayName = "Task 166 f1-v1: a write through a constant bound in ANOTHER client file is detected")]
    public void AWriteThroughAnImportedConstant_IsDetected()
    {
        Assert.Empty(Offenders("payload[DOCUMENT_FIELDS.GRAPH_ITEM] = id;"));
        Assert.NotEmpty(Offenders("payload[DOCUMENT_FIELDS.GRAPH_ITEM] = id;", sharedConstants: ["GRAPH_ITEM"]));
        Assert.Contains("GRAPH_ITEM", ColumnNameConstants("export const DOCUMENT_FIELDS = { GRAPH_ITEM: 'sprk_graphitemid' };"));
    }

    [Theory(DisplayName = "Task 166 f1: the detector never fires on a read")]
    [InlineData("'?$select=sprk_filename,sprk_graphitemid,sprk_graphdriveid'")]
    [InlineData("const itemId = record.sprk_graphitemid;")]
    [InlineData("speDriveItemId = docRec['sprk_graphitemid'] as string | undefined;")]
    [InlineData("interface Doc { sprk_graphitemid?: string | null; }")]
    [InlineData("const FIELD_GRAPH_ITEM_ID = \"sprk_graphitemid\";")]
    [InlineData("var driveId = formContext.getAttribute(\"sprk_graphdriveid\").getValue();")]
    [InlineData("const x = flag ? rec['sprk_graphitemid'] : null;")]                               // ternary arm
    [InlineData("const F = \"sprk_graphitemid\"; const id = record[F] as string | undefined;")]    // read via constant
    [InlineData("const F = \"sprk_graphitemid\"; const sel = `?$select=${F}`;")]                    // select via constant
    [InlineData("if (doc.sprk_graphitemid === id) { go(); }")]                                     // comparison
    [InlineData("if (doc.sprk_graphdriveid == null) { go(); }")]                                   // loose comparison
    [InlineData("const same = a.sprk_graphitemid !== b.sprk_graphitemid;")]                         // inequality
    [InlineData("return(null!==(t=e.sprk_graphitemid)&&void 0!==t?t:\"\").trim().length>0")]      // minified read (a live bundle)
    [InlineData("const ok = rows.some(r => r['sprk_graphitemid'] === id);")]                       // bracket read in a comparison
    public void TheDetectorNeverFiresOnARead(string source) => Assert.Empty(Offenders(source));
}
