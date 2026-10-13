using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Ontology task 142 (D-34; child-record contract C8): a Decision Record's DECISION CONTEXT — <c>sprk_corerecordid</c>,
/// <c>sprk_corerecordtype</c>, <c>sprk_policyversion</c> and <c>sprk_factsnapshot</c> — is written once, by the create, and by no
/// later path. Its filing, stamps, owner and shares are LIVE (they move with the record's parents); its context never does.
/// </summary>
/// <remarks>
/// <para><b>What is pinned.</b> (1) In all server source, a WRITE of one of the four columns (an indexer or dictionary
/// initializer <c>["col"] =</c>, <c>.Add("col", …)</c>, <c>SetAttributeValue("col", …)</c>, a <c>{ "col", value }</c> initializer,
/// or a Web API <c>"col@odata.bind"</c>) appears only in the two create builders: <c>DecisionRecordWriter.Content</c> (the Decision
/// Record's create) and <c>SignalWriter.BuildEntity</c> (the Signal's create, whose columns the record copies). (2) The Decision
/// Record writer calls its Dataverse client for <c>CreateAsync</c> and <c>RetrieveAsync</c> only: it has no update path. (3) No
/// server code addresses a Decision Record row for an update through a STRING LITERAL (see <see cref="DecisionRecordUpdate"/>): a
/// table name held in a constant or a variable and handed to a generic writer is not seen by (3); rules (1) and (2) cover the writer
/// itself.</para>
/// <para><b>Crude by design</b>, like every <see cref="SourceScan"/> rule: it reads comment-stripped text, not a compilation. Each
/// rule has a must-fire control (a seeded violation is reported) and a must-not-fire control (the sanctioned shape and the reads
/// that name the same columns are not).</para>
/// <para><b>MAINTENANCE.</b> A new create builder of a table that carries these columns joins <see cref="SanctionedWriters"/> with
/// its reason. A path that needs to change a recorded decision's context does not exist by design (D-34): raise it with the owner,
/// never add it here.</para>
/// </remarks>
public class DecisionRecordContextImmutabilityTests
{
    private const string Columns = "sprk_corerecordid|sprk_corerecordtype|sprk_policyversion|sprk_factsnapshot";

    /// <summary>A write of one of the four context columns.</summary>
    private static readonly Regex ContextWrite = new(
        $@"\[\s*""(?:{Columns})(?:@odata\.bind)?""\s*\]\s*=(?!=)"
        + $@"|\.(?:Add|TryAdd)\s*\(\s*""(?:{Columns})(?:@odata\.bind)?""\s*,"
        + $@"|SetAttributeValue\s*\(\s*""(?:{Columns})""\s*,"
        + $@"|\{{\s*""(?:{Columns})(?:@odata\.bind)?""\s*,\s*[^""\s]"
        + @"|""(?:sprk_corerecordtype|sprk_policyversion)@odata\.bind""",
        // Case-insensitive: a Web API bind uses the navigation property's PascalCase name ("sprk_PolicyVersion@odata.bind").
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>(file name, member) pairs that may write the context columns: the creates, each with its reason.</summary>
    private static readonly IReadOnlyDictionary<(string File, string Member), string> SanctionedWriters =
        new Dictionary<(string, string), string>
        {
            [("DecisionRecordWriter.cs", "Content")] = "the Decision Record's create payload (task 040 / 142): the record's ONLY create",
            [("SignalWriter.cs", "BuildEntity")] = "the Signal's create payload (task 030): the context the Decision Record copies",
        };

    /// <summary>A member declaration line (the census's rule): an access modifier, then a parameter list.</summary>
    private static readonly Regex MemberDeclaration = new(
        @"^[ \t]*(?:public|internal|private|protected)\b[^;=\n]*?\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>A call on the Decision Record writer's Dataverse client.</summary>
    private static readonly Regex WriterClientCall = new(@"\b_writerClient\s*\.\s*(?<method>\w+)\s*\(", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedWriterClientCalls = new(StringComparer.Ordinal) { "CreateAsync", "RetrieveAsync" };

    /// <summary>A Decision Record row addressed for an update through a string literal: a Web API path to one row
    /// (<c>"sprk_decisionrecords(…"</c>), an update/upsert/patch call naming the table or its entity set as its first argument,
    /// or an SDK <c>Entity</c> built for an EXISTING row (<c>new Entity("sprk_decisionrecord", id)</c>, the shape an update
    /// takes; the create is <c>new Entity(Table)</c> with no id).</summary>
    private static readonly Regex DecisionRecordUpdate = new(
        @"""sprk_decisionrecords\("
        + @"|(?:UpdateAsync|UpsertAsync|UpdateRecordFieldsAsync|PatchAsync)\s*\(\s*""sprk_decisionrecords?"""
        + @"|new\s+Entity\s*\(\s*""sprk_decisionrecord""\s*,",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact(DisplayName = "Task 142 / D-34: the four Decision Record context columns are written only by the create builders")]
    public void ContextColumnsAreWrittenOnlyByTheCreateBuilders()
    {
        var problems = ServerFiles()
            .SelectMany(f => ContextWrites(f.Key, f.Value))
            .Where(w => !SanctionedWriters.ContainsKey((w.File, w.Member)))
            .Select(w => $"{w.File}:{w.Line} ({w.Member})")
            .ToList();

        Assert.True(problems.Count == 0,
            "D-34: a Decision Record's context (sprk_corerecordid, sprk_corerecordtype, sprk_policyversion, sprk_factsnapshot) is "
            + "written once, at create, and never by a later path. Writes outside the sanctioned create builders:\n  "
            + string.Join("\n  ", problems));

        // The sanctioned writers still exist and still write (a rename must move this list, not silently empty it).
        foreach (var ((file, member), why) in SanctionedWriters)
        {
            var code = ServerFiles().TryGetValue(file, out var c) ? c : string.Empty;
            Assert.True(ContextWrites(file, code).Any(w => w.Member == member),
                $"{file}.{member} ({why}) no longer writes the context columns: update {nameof(SanctionedWriters)}.");
        }
    }

    [Fact(DisplayName = "Task 142 / D-34: the Decision Record writer has no update path (Create and Retrieve only)")]
    public void DecisionRecordWriterOnlyCreatesAndReads()
    {
        var code = ServerFiles()["DecisionRecordWriter.cs"];
        var calls = WriterClientCall.Matches(code).Select(m => m.Groups["method"].Value).ToList();

        Assert.Contains("CreateAsync", calls);
        var others = calls.Where(c => !AllowedWriterClientCalls.Contains(c)).Distinct().ToList();
        Assert.True(others.Count == 0,
            "D-34: DecisionRecordWriter is create-only (append-only); it calls its Dataverse client for: " + string.Join(", ", others));
    }

    [Fact(DisplayName = "Task 142 / D-34: no server code addresses a Decision Record row for an update through a string literal")]
    public void NoServerCodeUpdatesADecisionRecordByName()
    {
        var problems = ServerFiles()
            .SelectMany(f => DecisionRecordUpdate.Matches(f.Value).Select(m => $"{f.Key}:{SourceScan.LineOf(f.Value, m.Index)}"))
            .ToList();

        Assert.True(problems.Count == 0,
            "D-34: a Decision Record is append-only; its context is never updated. Update paths found:\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "Task 142 / D-34: negative and positive controls — the detectors fire on seeded writes and not on reads")]
    public void Detectors_Controls()
    {
        var seeded = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private Dictionary<string, object> Content(Request r)",
            "    {",
            "        var e = new Dictionary<string, object>();",
            "        e[\"sprk_policyversion\"] = new EntityReference(\"sprk_policyversion\", r.Version);",
            "        return e;",
            "    }",
            "",
            "    private async Task RewriteAsync(Guid id)",
            "    {",
            "        var row = new Entity(\"sprk_decisionrecord\", id);",
            "        row[\"sprk_corerecordid\"] = id.ToString();",
            "        row.Attributes.Add(\"sprk_factsnapshot\", \"{}\");",
            "        row.SetAttributeValue(\"sprk_corerecordtype\", new EntityReference(\"sprk_recordtype_ref\", id));",
            "        var body = new Dictionary<string, object?> { { \"sprk_PolicyVersion@odata.bind\", $\"/sprk_policyversions({id})\" } };",
            "        await _writerClient.UpdateAsync(row, CancellationToken.None);",
            "        await _api.PatchAsync(\"sprk_decisionrecord\", id, body);",
            "        await _http.SendAsync($\"sprk_decisionrecords({id})\");",
            "        await _api.UpdateAsync(\"sprk_decisionrecords\", id, body);",
            "    }",
            "",
            "    private IReadOnlyList<string> Reads(Entity e) => new[]",
            "    {",
            "        e.GetAttributeValue<string>(\"sprk_corerecordid\"),",
            "        e.GetAttributeValue<EntityReference>(\"sprk_policyversion\")?.Id.ToString(),",
            "    };",
            "",
            "    private static readonly string[] Select = { \"sprk_policyversion\", \"sprk_corerecordid\" };",
            "    private static bool Same(string a) => a == \"x\" && Select[0] == \"sprk_factsnapshot\";",
            "}",
        });

        var writes = ContextWrites("Seeded.cs", seeded).ToList();

        // Must fire: every seeded write, in the member that makes it.
        Assert.Contains(writes, w => w.Member == "RewriteAsync" && w.Line == 13); // indexer
        Assert.Contains(writes, w => w.Member == "RewriteAsync" && w.Line == 14); // Attributes.Add
        Assert.Contains(writes, w => w.Member == "RewriteAsync" && w.Line == 15); // SetAttributeValue
        Assert.Contains(writes, w => w.Member == "RewriteAsync" && w.Line == 16); // { "col@odata.bind", value }
        Assert.Contains(writes, w => w.Member == "Content" && w.Line == 6);       // the create builder's own write is seen

        // Must not fire: reads, a ColumnSet-style list and a comparison.
        Assert.DoesNotContain(writes, w => w.Line >= 22);

        // The writer-client and the update-by-name detectors.
        var calls = WriterClientCall.Matches(seeded).Select(m => m.Groups["method"].Value).ToList();
        Assert.Contains("UpdateAsync", calls);
        Assert.Equal(4, DecisionRecordUpdate.Matches(seeded).Count); // new Entity(table, id), PatchAsync(table), sprk_decisionrecords(, UpdateAsync(set)
        Assert.False(DecisionRecordUpdate.IsMatch("var entity = new Entity(Table); var other = new Entity(\"sprk_decisionrecord\");"), "a create has no id");
        Assert.False(DecisionRecordUpdate.IsMatch("var query = new QueryExpression(\"sprk_decisionrecord\");"), "a read query is not an update");
    }

    private sealed record Write(string File, string Member, int Line);

    private static IEnumerable<Write> ContextWrites(string fileName, string code)
    {
        var members = MemberDeclaration.Matches(code).ToList();
        foreach (Match m in ContextWrite.Matches(code))
        {
            var declaration = members.LastOrDefault(d => d.Index <= m.Index);
            var member = declaration is null
                ? "(file)"
                : Regex.Match(code[declaration.Index..], @"\b(?<name>\w+)\s*(?:<[^>()]*>)?\s*\(").Groups["name"].Value;
            yield return new Write(fileName, member, SourceScan.LineOf(code, m.Index));
        }
    }

    /// <summary>File name → comment-stripped code, for every server source file (same-named files concatenated, as the census does).</summary>
    private static Dictionary<string, string> ServerFiles()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in SourceScan.ServerSourceFiles())
        {
            var name = Path.GetFileName(path);
            var code = SourceScan.CodeText(File.ReadAllLines(path));
            files[name] = files.TryGetValue(name, out var existing) ? existing + "\n" + code : code;
        }

        return files;
    }
}
