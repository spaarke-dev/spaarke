using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 166 f1 (owner round 21 item 1 (i)) — no CLIENT writes a document's SharePoint Embedded
/// pointer (<c>sprk_graphdriveid</c> / <c>sprk_graphitemid</c>) or its relocation record. The client creates the row
/// WITHOUT it and calls the BFF's <c>POST /api/v1/documents/{id}/file</c>, which verifies the file and stamps the pointer
/// as the application.
/// </summary>
/// <remarks>
/// <para><b>Why a fitness function.</b> The pointer is what the BFF follows AS THE APPLICATION on every download, so the
/// columns are field-secured and writable by the BFF identity only (<c>scripts/Set-DocumentPointerFieldSecurity.ps1</c>).
/// Locking them is only safe once no shipped client writes them — every such upload would then be refused — and that
/// script's precondition (p4a) checks the DEPLOYED web resources for exactly the shapes this guard forbids in source. A
/// pointer write re-added anywhere in a client (a wizard, a code page, a PCF, a form script) would either reopen the
/// write the lock exists to close, or break uploads once the lock is applied. This makes it fail the build instead.</para>
/// <para><b>One detector, two runners.</b> The templates below are IDENTICAL to <c>scripts/common/Find-ClientPointerWrite.ps1</c>,
/// the file p4a dot-sources, and <see cref="TheFlsScriptsDetector_AgreesWithTheGuard_OnEveryCase"/> runs that file through
/// pwsh over every case of this class (task 166 f1-v2, F-D: the f1-v1 parity harness was never committed, and two write
/// shapes were missed by both runners).</para>
/// <para><b>The columns</b> (all BFF-written only): <c>sprk_graphdriveid</c>, <c>sprk_graphitemid</c>, the relocation
/// ledger <c>sprk_relocationpending</c> (f1-v1) and the relocation's version record <c>sprk_relocatedversions</c> (f1-v2,
/// owner round 45 item 1).</para>
/// <para><b>What counts as a write</b> — exactly these shapes:</para>
/// <list type="bullet">
/// <item>an object-literal key <c>sprk_graphitemid:</c> / <c>"sprk_graphdriveid":</c> (a create / update payload);</item>
/// <item>a computed key <c>{ ["sprk_graphitemid"]: … }</c>;</item>
/// <item>a bracket assignment <c>payload['sprk_graphitemid'] = …</c> (also <c>??=</c>, <c>||=</c>, <c>&amp;&amp;=</c>);</item>
/// <item>a dotted assignment <c>e.sprk_graphdriveid = …</c> (minified bundles write this way);</item>
/// <item>a form <c>setValue</c> through <c>getAttribute(…)</c> / <c>attributes.get(…)</c>, or through
/// <c>getControl(…)</c> / <c>controls.get(…)</c><c>.getAttribute().setValue</c> (also <c>?.</c>);</item>
/// <item><c>Reflect.set(obj, "…", v)</c> / <c>defineProperty(obj, "…", …)</c>;</item>
/// <item>the computed-key, bracket, both setValue and the reflective shapes THROUGH a name bound to a column
/// (<c>const F = "sprk_graphitemid"</c>, <c>{ ITEM: 'sprk_graphitemid' }</c>) or bound to such a name under another name
/// — an import or export rename (<c>import { F as G }</c>), a re-binding (<c>const G = F</c>, <c>const G = cols.F</c>) or a
/// destructuring rename (<c>const { F: G } = cols</c>), to a fixpoint — bound in the same file, or (source files) in any
/// other client source file (an exported constant and its aliases).</item>
/// </list>
/// <para>Reads — <c>$select</c> strings, property access, <c>['sprk_graphitemid']</c> lookups, a ternary arm, comparisons
/// (<c>===</c>, <c>==</c>, <c>!==</c>), <c>getValue()</c>, a read through a constant or an alias — never match. <b>Not
/// detectable statically</b>: a key built at run time (string concatenation, a loop over a list of names, a value returned
/// by another module's function). The field-level security lock is the control for those; this guard keeps every shape
/// listed above out of the shipped clients.</para>
/// <para><b>Scope.</b> Every client source file (<c>.ts</c>, <c>.tsx</c>, <c>.js</c>) under <c>src/client</c>,
/// <c>src/solutions</c> and <c>src/dataverse</c> — INCLUDING the committed PCF solution bundles
/// (<c>Solution/**/bundle.js</c>), which are what gets deployed — and excluding tests, mocks, installed packages and
/// build output.</para>
/// </remarks>
public sealed class ClientDocumentPointerWriteGuardTests
{
    /// <summary>The BFF-written columns: the two pointer columns, the relocation ledger and the relocation's version record.</summary>
    internal const string Columns = "sprk_(?:graph(?:item|drive)id|relocationpending|relocatedversions)";

    // ── The detector — IDENTICAL to scripts/common/Find-ClientPointerWrite.ps1 ({C} column, {P} path, {K} known name) ──

    /// <summary>The literal write shapes ({C} = <see cref="Columns"/>).</summary>
    internal static readonly string[] WriteTemplates =
    [
        @"[""']?{C}[""']?\s*:",                                                                       // object key
        @"[{,]\s*\[\s*[""'`]{C}[""'`]\s*\]\s*:",                                                       // computed key
        @"\[\s*[""'`]{C}[""'`]\s*\]\s*(?:\?\?|\|\||&&)?=(?![=>])",                                     // bracket assignment
        @"\.\s*{C}\s*(?:\?\?|\|\||&&)?=(?![=>])",                                                      // dotted assignment
        @"(?:getAttribute|attributes\s*\.\s*get)\(\s*[""'`]{C}[""'`]\s*\)\s*\??\.\s*setValue",         // form setValue (attribute)
        @"(?:getControl|controls\s*\.\s*get)\(\s*[""'`]{C}[""'`]\s*\)\s*\??\.\s*getAttribute\(\s*\)\s*\??\.\s*setValue", // form setValue (control)
        @"(?:Reflect\s*\.\s*set|defineProperty)\(\s*[^,()]+,\s*[""'`]{C}[""'`]",                       // reflective set
    ];

    /// <summary>The write shapes THROUGH a name bound to a column ({P} = a possibly member-qualified name).</summary>
    internal static readonly string[] ThroughTemplates =
    [
        @"[{,]\s*\[\s*{P}\s*\]\s*:",                                                                   // computed key
        @"\[\s*{P}\s*\]\s*(?:\?\?|\|\||&&)?=(?![=>])",                                                 // bracket assignment
        @"(?:getAttribute|attributes\s*\.\s*get)\(\s*{P}\s*\)\s*\??\.\s*setValue",                     // form setValue (attribute)
        @"(?:getControl|controls\s*\.\s*get)\(\s*{P}\s*\)\s*\??\.\s*getAttribute\(\s*\)\s*\??\.\s*setValue", // form setValue (control)
        @"(?:Reflect\s*\.\s*set|defineProperty)\(\s*[^,()]+,\s*{P}\s*[,)]",                           // reflective set
    ];

    /// <summary>A name bound to a column-name string (group 1).</summary>
    internal const string BindingTemplate = @"(?<![\w$.])([A-Za-z_$][\w$]*)\s*[=:]\s*[""'`]{C}[""'`]";

    /// <summary>A NEW name (group 1) that holds what the known name {K} holds.</summary>
    internal static readonly string[] AliasTemplates =
    [
        @"(?<![\w$.]){K}\s+as\s+([A-Za-z_$][\w$]*)",                                                   // import / export rename
        @"(?<![\w$.])([A-Za-z_$][\w$]*)\s*=(?![=>])\s*(?:[\w$]+\s*\.\s*)*{K}(?![\w$(.\[])",           // re-binding
        @"(?:const|let|var)\s*\{[^{}]*?(?<![\w$.]){K}\s*:\s*([A-Za-z_$][\w$]*)",                      // destructuring rename
    ];

    private const string PathTemplate = @"(?:[\w$]+\s*\.\s*)*{N}(?![\w$])";

    private static readonly Regex[] PointerWrites =
        WriteTemplates.Select(t => new Regex(t.Replace("{C}", Columns, StringComparison.Ordinal), RegexOptions.Compiled)).ToArray();

    private static readonly Regex ColumnNameBinding =
        new(BindingTemplate.Replace("{C}", Columns, StringComparison.Ordinal), RegexOptions.Compiled);

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

    /// <summary>The names <paramref name="text"/> binds DIRECTLY to a column-name string.</summary>
    internal static IEnumerable<string> ColumnNameConstants(string text)
        => ColumnNameBinding.Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Every name that holds a column in <paramref name="text"/>: its direct bindings and <paramref name="known"/>, and
    /// every alias of either (an import / export rename, a re-binding, a destructuring rename), to a fixpoint.
    /// </summary>
    internal static IReadOnlyCollection<string> ColumnNames(string text, IEnumerable<string>? known = null)
    {
        var names = new HashSet<string>(ColumnNameConstants(text), StringComparer.Ordinal);
        names.UnionWith((known ?? []).Where(n => !string.IsNullOrEmpty(n)));
        var pending = names.ToList();
        for (var round = 0; round < 10 && pending.Count > 0; round++)
        {
            var next = new List<string>();
            foreach (var name in pending.Where(n => text.Contains(n, StringComparison.Ordinal)))
            {
                foreach (var template in AliasTemplates)
                {
                    foreach (Match m in Regex.Matches(text, template.Replace("{K}", Regex.Escape(name), StringComparison.Ordinal)))
                    {
                        if (names.Add(m.Groups[1].Value))
                        {
                            next.Add(m.Groups[1].Value);
                        }
                    }
                }
            }

            pending = next;
        }

        return names;
    }

    /// <summary>
    /// Every pointer write in <paramref name="text"/>: the literal shapes, and the shapes through a name that holds a column
    /// — bound in this text, named in <paramref name="sharedConstants"/> (constants bound in other client source files), or
    /// an alias of either.
    /// </summary>
    internal static IEnumerable<string> Offenders(string text, IEnumerable<string>? sharedConstants = null)
    {
        foreach (var hit in PointerWrites.Select(p => p.Match(text)).Where(m => m.Success))
        {
            yield return hit.Value;
        }

        foreach (var name in ColumnNames(text, sharedConstants).Where(n => text.Contains(n, StringComparison.Ordinal)))
        {
            var path = PathTemplate.Replace("{N}", Regex.Escape(name), StringComparison.Ordinal);
            foreach (var template in ThroughTemplates)
            {
                var hit = Regex.Match(text, template.Replace("{P}", path, StringComparison.Ordinal));
                if (hit.Success)
                {
                    yield return hit.Value;
                }
            }
        }
    }

    private static bool IsCommittedBundle(string file) => file.EndsWith("bundle.js", StringComparison.OrdinalIgnoreCase);

    [Fact(DisplayName = "Task 166 f1: no client source or committed PCF bundle writes a document's SPE pointer")]
    public void NoClientWritesADocumentPointer()
    {
        var files = ClientFiles();
        Assert.True(files.Count > 100, $"only {files.Count} client files were scanned — the scan roots are wrong, and an empty scan proves nothing");

        // Names a SOURCE file binds to a column — and their aliases in any source file, to a fixpoint — may be imported
        // anywhere; a minified bundle's one-letter names are meaningful only inside that bundle, so they are matched per file.
        var texts = files.ToDictionary(f => f, File.ReadAllText);
        var sources = files.Where(f => !IsCommittedBundle(f)).ToList();
        var shared = new HashSet<string>(sources.SelectMany(f => ColumnNameConstants(texts[f])), StringComparer.Ordinal);
        for (var round = 0; round < 10; round++)
        {
            var before = shared.Count;
            foreach (var file in sources)
            {
                shared.UnionWith(ColumnNames(texts[file], shared));
            }

            if (shared.Count == before)
            {
                break;
            }
        }

        var violations = files
            .Select(f => (File: Path.GetRelativePath(SourceScan.RepoRoot, f), Hits: Offenders(texts[f], IsCommittedBundle(f) ? null : shared).Distinct().ToList()))
            .Where(v => v.Hits.Count > 0)
            .Select(v => $"{v.File}: {string.Join(", ", v.Hits)}")
            .ToList();

        Assert.True(violations.Count == 0,
            "A client writes sprk_graphdriveid / sprk_graphitemid / the relocation record. They are BFF-written only "
            + "(field-level security, owner round 21 item 1): create the row WITHOUT them and attach the uploaded file with "
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

    // ── The cases. Each write case is ONE shape on its own (task 166 f1-v1, F3: a case that combined a bracket write with
    //    an object key passed on the object key alone and proved nothing about the bracket write). The second value is the
    //    constants other client files bind (the cross-file cases); empty for a single-text case. ──

    public static TheoryData<string, string[]> WriteCases => new()
    {
        { "const payload = { sprk_graphitemid: file.id };", [] },                                   // object key
        { "const p={sprk_graphdriveid:e.driveId,sprk_hasfile:!0}", [] },                           // object key, minified
        { "const q = { \"sprk_graphdriveid\": d };", [] },                                          // quoted object key
        { "payload[\"sprk_graphitemid\"] = x;", [] },                                               // bracket assignment
        { "payload['sprk_graphdriveid']=driveId;", [] },                                           // bracket, single quotes
        { "payload[`sprk_graphitemid`] ??= id;", [] },                                             // bracket, compound
        { "e.sprk_graphdriveid=t", [] },                                                           // dotted, minified
        { "record.sprk_graphitemid = item.id;", [] },                                              // dotted
        { "payload.sprk_graphitemid ||= id;", [] },                                                // dotted, compound
        { "const body = { [\"sprk_graphitemid\"]: id };", [] },                                    // computed literal key
        { "const F = \"sprk_graphitemid\"; const body = { [F]: id };", [] },                       // computed key via constant
        { "const FIELD_GRAPH_ITEM_ID = \"sprk_graphitemid\"; payload[FIELD_GRAPH_ITEM_ID] = id;", [] }, // bracket via constant
        { "const n=\"sprk_graphdriveid\";function u(e,t){return{[n]:t}}", [] },                    // computed via constant, minified
        { "const FIELDS = { ITEM: 'sprk_graphitemid' }; formContext.getAttribute(FIELDS.ITEM).setValue(id);", [] }, // setValue via member constant
        { "formContext.getAttribute(\"sprk_graphitemid\").setValue(id);", [] },                    // form setValue
        { "formContext.getAttribute('sprk_graphdriveid') . setValue(null);", [] },                // form setValue, spaced
        { "formContext.getAttribute(\"sprk_graphitemid\")?.setValue(id);", [] },                   // optional chaining
        { "formContext.data.entity.attributes.get(\"sprk_graphitemid\").setValue(id);", [] },      // attributes.get
        { "row.sprk_relocationpending = \"{}\";", [] },                                            // the relocation ledger
        { "const ledger = { sprk_relocationpending: json };", [] },                                // the ledger, object key
        // f1-v2 (F-D, owner round 45 item 5): the two shapes the verifier seeded, their siblings, and the version record.
        { "formContext.getControl(\"sprk_graphitemid\").getAttribute().setValue(x);", [] },        // form setValue through the control
        { "formContext.getControl('sprk_graphdriveid')?.getAttribute()?.setValue(x);", [] },       // control, optional chaining
        { "formContext.ui.controls.get(\"sprk_graphitemid\").getAttribute().setValue(x);", [] },   // controls.get
        { "const F = \"sprk_graphitemid\"; formContext.getControl(F).getAttribute().setValue(x);", [] }, // control via constant
        { "import { POINTER_ITEM_COL as COL } from './cols'; p[COL] = v;", ["POINTER_ITEM_COL"] }, // aliased import of another file's constant
        { "export { POINTER_ITEM_COL as ITEM } from './cols'; payload[ITEM] = v;", ["POINTER_ITEM_COL"] }, // aliased re-export
        { "const { POINTER_ITEM_COL: COL } = cols; p[COL] = v;", ["POINTER_ITEM_COL"] },           // destructuring rename
        { "const POINTER_ITEM_COL = \"sprk_graphitemid\"; const COL = POINTER_ITEM_COL; p[COL] = v;", [] }, // same-file re-binding
        { "const COL = cols.POINTER_ITEM_COL; const body = { [COL]: v };", ["POINTER_ITEM_COL"] },  // re-binding of a member
        { "import { A as B } from './a'; import { B as C } from './b'; p[C] = v;", ["A"] },         // alias of an alias
        { "Reflect.set(payload, \"sprk_graphitemid\", id);", [] },                                 // Reflect.set
        { "Object.defineProperty(payload, 'sprk_graphdriveid', { value: d });", [] },              // defineProperty
        { "const F = \"sprk_graphitemid\"; Reflect.set(payload, F, id);", [] },                    // Reflect.set via constant
        { "payload.sprk_relocatedversions = \"{}\";", [] },                                         // the version record
    };

    public static TheoryData<string, string[]> ReadCases => new()
    {
        { "'?$select=sprk_filename,sprk_graphitemid,sprk_graphdriveid'", [] },
        { "const itemId = record.sprk_graphitemid;", [] },
        { "speDriveItemId = docRec['sprk_graphitemid'] as string | undefined;", [] },
        { "interface Doc { sprk_graphitemid?: string | null; }", [] },
        { "const FIELD_GRAPH_ITEM_ID = \"sprk_graphitemid\";", [] },
        { "var driveId = formContext.getAttribute(\"sprk_graphdriveid\").getValue();", [] },
        { "const x = flag ? rec['sprk_graphitemid'] : null;", [] },                                 // ternary arm
        { "const F = \"sprk_graphitemid\"; const id = record[F] as string | undefined;", [] },      // read via constant
        { "const F = \"sprk_graphitemid\"; const sel = `?$select=${F}`;", [] },                      // select via constant
        { "if (doc.sprk_graphitemid === id) { go(); }", [] },                                       // comparison
        { "if (doc.sprk_graphdriveid == null) { go(); }", [] },                                     // loose comparison
        { "const same = a.sprk_graphitemid !== b.sprk_graphitemid;", [] },                           // inequality
        { "return(null!==(t=e.sprk_graphitemid)&&void 0!==t?t:\"\").trim().length>0", [] },        // minified read (a live bundle)
        { "const ok = rows.some(r => r['sprk_graphitemid'] === id);", [] },                         // bracket read in a comparison
        // f1-v2: reads of the new shapes never fire.
        { "const v = formContext.getControl(\"sprk_graphitemid\").getAttribute().getValue();", [] }, // control read
        { "formContext.getControl(\"sprk_graphitemid\").setVisible(false);", [] },                  // control, not the value
        { "import { POINTER_ITEM_COL as COL } from './cols'; const id = row[COL];", ["POINTER_ITEM_COL"] }, // read through an alias
        { "const COL = POINTER_ITEM_COL; if (row[COL] === id) { go(); }", ["POINTER_ITEM_COL"] },   // re-bound alias, compared
        { "const id = Reflect.get(payload, \"sprk_graphitemid\");", [] },                            // Reflect.get
        { "const sel = '?$select=sprk_relocatedversions,sprk_relocationpending';", [] },            // the record, selected
        { "const isItem = name === POINTER_ITEM_COL;", ["POINTER_ITEM_COL"] },                       // comparison with a constant
    };

    [Theory(DisplayName = "Task 166 f1: the detector fires on a pointer write")]
    [MemberData(nameof(WriteCases))]
    public void TheDetectorFiresOnAWrite(string source, string[] shared) => Assert.NotEmpty(Offenders(source, shared));

    [Theory(DisplayName = "Task 166 f1: the detector never fires on a read")]
    [MemberData(nameof(ReadCases))]
    public void TheDetectorNeverFiresOnARead(string source, string[] shared) => Assert.Empty(Offenders(source, shared));

    [Fact(DisplayName = "Task 166 f1-v1: a write through a constant bound in ANOTHER client file is detected")]
    public void AWriteThroughAnImportedConstant_IsDetected()
    {
        Assert.Empty(Offenders("payload[DOCUMENT_FIELDS.GRAPH_ITEM] = id;"));
        Assert.NotEmpty(Offenders("payload[DOCUMENT_FIELDS.GRAPH_ITEM] = id;", sharedConstants: ["GRAPH_ITEM"]));
        Assert.Contains("GRAPH_ITEM", ColumnNameConstants("export const DOCUMENT_FIELDS = { GRAPH_ITEM: 'sprk_graphitemid' };"));
    }

    [Fact(DisplayName = "Task 166 f1-v2: an alias of another file's constant, renamed on import, is a column name")]
    public void AnImportRenameOfAColumnConstant_IsAColumnName()
    {
        Assert.DoesNotContain("COL", ColumnNames("import { POINTER_ITEM_COL as COL } from './cols';"));
        Assert.Contains("COL", ColumnNames("import { POINTER_ITEM_COL as COL } from './cols';", ["POINTER_ITEM_COL"]));
        Assert.DoesNotContain("COL", ColumnNames("import { SOMETHING_ELSE as COL } from './cols';", ["POINTER_ITEM_COL"]));
    }

    /// <summary>
    /// Task 166 f1-v2, F-D: the FLS script's p4a runs <c>scripts/common/Find-ClientPointerWrite.ps1</c> over every deployed
    /// web resource. This runs THAT file through pwsh over every write and read case above and requires the same verdict
    /// as the C# detector for each — the committed form of the parity harness.
    /// </summary>
    [Fact(DisplayName = "Task 166 f1-v2: the FLS script's detector agrees with the guard on every case")]
    public async System.Threading.Tasks.Task TheFlsScriptsDetector_AgreesWithTheGuard_OnEveryCase()
    {
        var cases = WriteCases.Select(r => (Text: (string)r[0], Shared: (string[])r[1], Write: true))
            .Concat(ReadCases.Select(r => (Text: (string)r[0], Shared: (string[])r[1], Write: false)))
            .ToList();
        Assert.True(cases.Count >= 50, $"only {cases.Count} cases — the case lists were not read");

        var detector = Path.Combine(SourceScan.RepoRoot, "scripts", "common", "Find-ClientPointerWrite.ps1");
        Assert.True(File.Exists(detector), $"{detector} is missing — Set-DocumentPointerFieldSecurity.ps1 dot-sources it");

        var work = Path.Combine(Path.GetTempPath(), "sprk-pointer-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var casesFile = Path.Combine(work, "cases.json");
            var outFile = Path.Combine(work, "verdicts.json");
            var runner = Path.Combine(work, "run.ps1");
            File.WriteAllText(casesFile, JsonSerializer.Serialize(cases.Select(c => new { text = c.Text, shared = c.Shared })));
            File.WriteAllText(runner,
                "param([string]$Detector, [string]$Cases, [string]$Out)\n"
                + "$ErrorActionPreference = 'Stop'\n"
                + ". $Detector\n"
                + "$verdicts = @(foreach ($c in @(Get-Content -Raw -LiteralPath $Cases | ConvertFrom-Json)) { [bool](Find-PointerWrite -Text $c.text -SharedConstants @($c.shared)) })\n"
                + "ConvertTo-Json -InputObject $verdicts -Compress | Set-Content -LiteralPath $Out -Encoding utf8\n");

            var start = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", runner, "-Detector", detector, "-Cases", casesFile, "-Out", outFile })
            {
                start.ArgumentList.Add(arg);
            }

            Process process;
            try
            {
                process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "pwsh (PowerShell 7) is required to verify the FLS script's detector against this guard; install it.", ex);
            }

            using (process)
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(120));
                await process.WaitForExitAsync(timeout.Token);
                var output = await stdout;
                var errors = await stderr;
                Assert.True(process.ExitCode == 0, $"pwsh failed ({process.ExitCode}): {errors} {output}");
            }

            var verdicts = JsonSerializer.Deserialize<bool[]>(File.ReadAllText(outFile).Trim().TrimStart('\uFEFF'))!;
            Assert.Equal(cases.Count, verdicts.Length);
            var disagreements = cases
                .Select((c, i) => (c.Text, c.Write, Guard: Offenders(c.Text, c.Shared).Any(), Script: verdicts[i]))
                .Where(x => x.Guard != x.Write || x.Script != x.Write)
                .Select(x => $"{(x.Write ? "WRITE" : "READ ")} guard={x.Guard} script={x.Script}: {x.Text}")
                .ToList();
            Assert.True(disagreements.Count == 0,
                "The C# guard and scripts/common/Find-ClientPointerWrite.ps1 must both fire on every write and on no read:"
                + Environment.NewLine + string.Join(Environment.NewLine, disagreements));
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
}
