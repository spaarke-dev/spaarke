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
/// <para><b>One create site, every entry point (task 042).</b> The writer has three public entry points that create a record,
/// <c>WriteAsync</c> (a human review), <c>WriteGateDecisionAsync</c> (an automated communications-gate decision) and, task 153,
/// <c>WriteConditionClearedAsync</c> (a Do-lane ConditionCleared closure). All must reach
/// the SAME private <c>CreateAsync</c> (the only <c>_writerClient.CreateAsync</c> call, which builds the payload from
/// <c>Content</c> and files and owns it through <c>FiledAndOwnedAsync</c>), so a new entry point cannot grow a second create that
/// skips the filing, the owner answer or the D-34 context rule. <see cref="OneCreateSiteReachedByEveryEntryPoint"/> pins it, with
/// must-fire and must-not-fire controls.</para>
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

    /// <summary>Any mention of the writer's shared sysadmin reader (<c>IGenericEntityService</c>, which CAN create and update).</summary>
    private static readonly Regex EntitiesToken = new(@"\b_entities\b", RegexOptions.Compiled);

    /// <summary>The ONE sanctioned use: a read (task 149's gate-record lookup; task 153's condition-cleared lookup and its attribution read of the
    /// subject's last change). <c>\s</c> spans newlines, so a split call is allowed.</summary>
    private static readonly Regex EntitiesLookupRead = new(@"\G_entities\s*\.\s*RetrieveMultipleAsync\s*\(", RegexOptions.Compiled);

    /// <summary>(a) the field declaration (checked inline, preceded by the type) and (b) the constructor assignment: the only other sanctioned mentions.</summary>
    private static readonly Regex EntitiesCtorAssignment = new(@"\G_entities\s*=(?![=>])\s*entities\b[^;]*;", RegexOptions.Compiled);

    /// <remarks>Task 153 (adversarial pass 2 F1): <c>GetWriterUserIdAsync</c> is WhoAmI on the writer's own connection (a read of its
    /// identity, so the "already recorded" lookups match only rows the writer created); it writes nothing.</remarks>
    private static readonly HashSet<string> AllowedWriterClientCalls = new(StringComparer.Ordinal) { "CreateAsync", "RetrieveAsync", "GetWriterUserIdAsync" };

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
            "D-34: DecisionRecordWriter is create-only (a Decision Record is append-only except the live stamp columns, task 144 C2); it calls its Dataverse client for: " + string.Join(", ", others));

        // Task 149: the shared sysadmin reader the gate lookup uses can also create and update, so it is held to the one read.
        var entityOthers = EntitiesViolations(code);
        Assert.True(entityOthers.Count == 0,
            "D-34: DecisionRecordWriter may use the shared entity service only to declare it, assign it in the constructor and call RetrieveMultipleAsync (the gate record lookup); other uses:\n  " + string.Join("\n  ", entityOthers));
    }

    /// <summary>Every <c>_entities</c> token that is not the field declaration, the constructor assignment or a
    /// <c>.RetrieveMultipleAsync(</c> call: a write call (also via <c>?.</c> or split across lines), a local alias, a method group, or a
    /// hand-off to a helper all land here. Returns "line N" for each.</summary>
    private static List<string> EntitiesViolations(string code)
    {
        var violations = new List<string>();
        foreach (Match m in EntitiesToken.Matches(code))
        {
            var before = code[..m.Index];
            if (Regex.IsMatch(before, @"\bIGenericEntityService\s+$")
                || EntitiesCtorAssignment.IsMatch(code, m.Index)
                || EntitiesLookupRead.IsMatch(code, m.Index))
            {
                continue;
            }

            violations.Add($"line {SourceScan.LineOf(code, m.Index)}");
        }

        return violations;
    }

    [Fact(DisplayName = "Task 149 / D-34: shared-entity-service detector controls — writes, aliases, ?. and split calls fire; the lookup read, declaration and constructor do not")]
    public void EntitiesCall_Controls()
    {
        static List<string> Scan(params string[] body) => EntitiesViolations(SourceScan.CodeText(
            new[] { "private async Task Other(Entity e, QueryExpression q, CancellationToken ct)", "{" }.Concat(body).Append("}").ToArray()));

        // Must fire: a create (or any write) through the shared sysadmin service.
        Assert.Single(Scan("    await _entities.CreateAsync(e, CancellationToken.None);"));

        // Must fire: the null-conditional form.
        Assert.Single(Scan("    await _entities?.UpdateAsync(e, ct);"));

        // Must fire: a local alias (the write then goes through the alias, which no token scan sees).
        Assert.Single(Scan("    var r = _entities;", "    await r.CreateAsync(e, ct);"));

        // Must fire: a call split across lines.
        Assert.Single(Scan("    await _entities", "        .CreateAsync(e, ct);"));

        // Must fire: a method group (no parenthesis) and a hand-off to a helper.
        Assert.Single(Scan("    Func<Entity, CancellationToken, Task<Guid>> f = _entities.CreateAsync;"));
        Assert.Single(Scan("    await Helper(_entities, e, ct);"));

        // Must NOT fire: the sanctioned lookup read, on one line or split across lines.
        Assert.Empty(Scan(
            "    var rows = await _entities.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);",
            "    var more = await _entities",
            "        .RetrieveMultipleAsync(q, ct);",
            "    var again = await _entities . RetrieveMultipleAsync",
            "        (q, ct);"));

        // Must NOT fire: the field declaration and the constructor assignment.
        Assert.Empty(EntitiesViolations(SourceScan.CodeText(new[]
        {
            "public sealed class DecisionRecordWriter",
            "{",
            "    private readonly IGenericEntityService _entities;",
            "",
            "    public DecisionRecordWriter(IGenericEntityService entities)",
            "    {",
            "        _entities = entities ?? throw new ArgumentNullException(nameof(entities));",
            "    }",
            "}",
        })));
    }

    /// <summary>The entry points that create a Decision Record (public members of the writer), each of which must reach <see cref="SharedCreate"/>.</summary>
    /// <remarks>Task 153 adds <c>WriteConditionClearedAsync</c> (the Routine record of a Do-lane ConditionCleared closure).</remarks>
    private static readonly string[] CreateEntryPoints = ["WriteAsync", "WriteGateDecisionAsync", "WriteConditionClearedAsync"];

    private const string SharedCreate = "CreateAsync";

    private static readonly Regex WriterClientCreate = new(@"\b_writerClient\s*\.\s*CreateAsync\s*\(", RegexOptions.Compiled);

    [Fact(DisplayName = "Task 042 / D-34: the writer has ONE create site and every entry point reaches it")]
    public void OneCreateSiteReachedByEveryEntryPoint()
    {
        var problems = CreateSiteProblems(ServerFiles()["DecisionRecordWriter.cs"]);

        Assert.True(problems.Count == 0,
            "The Decision Record writer must create through one private CreateAsync that every entry point calls (task 042):\n  "
            + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "Task 042 / D-34: create-site detector controls — a second create, a missing entry point and a bypassing entry point fire")]
    public void OneCreateSite_Controls()
    {
        static string Writer(string gateBody, string extra = "", string clearedBody = "        return await CreateAsync(review, plan, null, ct);") => SourceScan.CodeText(new[]
        {
            "public sealed class DecisionRecordWriter",
            "{",
            "    public async Task<R> WriteAsync(A request, DecisionRecordOwner? o, CancellationToken ct)",
            "    {",
            "        return await CreateAsync(request, Validate(request), o, ct);",
            "    }",
            "",
            "    public async Task<R> WriteGateDecisionAsync(G request, CancellationToken ct = default)",
            "    {",
            gateBody,
            "    }",
            "",
            "    public async Task<R> WriteConditionClearedAsync(C request, CancellationToken ct = default)",
            "    {",
            clearedBody,
            "    }",
            "",
            "    private async Task<R> CreateAsync(A request, Plan plan, DecisionRecordOwner? o, CancellationToken ct)",
            "    {",
            "        return await _writerClient.CreateAsync(entity, ct);",
            "    }",
            extra,
            "}",
        });

        // Must NOT fire: the sanctioned shape.
        Assert.Empty(CreateSiteProblems(Writer("        return await CreateAsync(review, plan, null, ct);")));

        // Must fire: the gate entry builds and creates its own record (a second create site that skips filing and owner).
        Assert.NotEmpty(CreateSiteProblems(Writer("        return await _writerClient.CreateAsync(new Entity(\"sprk_decisionrecord\"), ct);")));

        // Must fire: the gate entry never reaches the shared create.
        Assert.NotEmpty(CreateSiteProblems(Writer("        return new R();")));

        // Must fire: a third member creates on its own.
        Assert.NotEmpty(CreateSiteProblems(Writer(
            "        return await CreateAsync(review, plan, null, ct);",
            "    private async Task Other(Entity e) { await _writerClient.CreateAsync(e, CancellationToken.None); }")));

        // Must fire: an entry point is renamed away (the list must move with it, not silently empty).
        Assert.NotEmpty(CreateSiteProblems(Writer("        return await CreateAsync(review, plan, null, ct);").Replace("WriteGateDecisionAsync", "WriteGateAsync")));

        // Must fire (task 153): the condition-cleared entry builds and creates its own record, or never reaches the shared create.
        Assert.NotEmpty(CreateSiteProblems(Writer("        return await CreateAsync(review, plan, null, ct);",
            clearedBody: "        return await _writerClient.CreateAsync(new Entity(\"sprk_decisionrecord\"), ct);")));
        Assert.NotEmpty(CreateSiteProblems(Writer("        return await CreateAsync(review, plan, null, ct);", clearedBody: "        return new R();")));
    }

    /// <summary>Problems with the writer's create shape: not exactly one <c>_writerClient.CreateAsync</c> call, that call outside
    /// <see cref="SharedCreate"/>, or an entry point in <see cref="CreateEntryPoints"/> that is missing or does not call <see cref="SharedCreate"/>.</summary>
    private static List<string> CreateSiteProblems(string code)
    {
        var problems = new List<string>();
        var members = MemberDeclaration.Matches(code).ToList();
        string NameOf(Match declaration) =>
            Regex.Match(code[declaration.Index..], @"\b(?<name>\w+)\s*(?:<[^>()]*>)?\s*\(").Groups["name"].Value;

        var creates = WriterClientCreate.Matches(code).ToList();
        if (creates.Count != 1)
        {
            problems.Add($"{creates.Count} _writerClient.CreateAsync call sites (expected exactly 1)");
        }

        foreach (var create in creates)
        {
            var owner = members.LastOrDefault(d => d.Index <= create.Index);
            var name = owner is null ? "(file)" : NameOf(owner);
            if (name != SharedCreate)
            {
                problems.Add($"_writerClient.CreateAsync is called from {name} (line {SourceScan.LineOf(code, create.Index)}), not from {SharedCreate}");
            }
        }

        foreach (var entry in CreateEntryPoints)
        {
            var declarations = members.Where(d => NameOf(d) == entry).ToList();
            if (declarations.Count == 0)
            {
                problems.Add($"entry point {entry} not found: update {nameof(CreateEntryPoints)}");
                continue;
            }

            // An entry point may have forwarding overloads (WriteAsync(request, ct) forwards to the 3-argument one): at least one
            // declaration must reach the shared create itself.
            var reaches = declarations.Any(declaration =>
            {
                var next = members.FirstOrDefault(d => d.Index > declaration.Index);
                return Regex.IsMatch(code[declaration.Index..(next?.Index ?? code.Length)], $@"\b{SharedCreate}\s*\(");
            });
            if (!reaches)
            {
                problems.Add($"entry point {entry} does not call {SharedCreate}");
            }
        }

        return problems;
    }

    [Fact(DisplayName = "Task 142 / D-34: no server code addresses a Decision Record row for an update through a string literal")]
    public void NoServerCodeUpdatesADecisionRecordByName()
    {
        var problems = ServerFiles()
            .SelectMany(f => DecisionRecordUpdate.Matches(f.Value).Select(m => $"{f.Key}:{SourceScan.LineOf(f.Value, m.Index)}"))
            .ToList();

        Assert.True(problems.Count == 0,
            "D-34: a Decision Record is append-only except the live stamp columns (task 144, C2); its context is never updated. Update paths found:\n  " + string.Join("\n  ", problems));
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
