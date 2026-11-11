using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 054b (owner decision 2026-11-11, option A). A thread's type (<c>sprk_threadtype</c>) and
/// its default-thread flag (<c>sprk_isdefaultthread</c>) decide who may read its messages. The BFF writes them when it creates a
/// thread. It writes either one on an update in exactly one member: <c>ThreadResolver.AnchorThreadForFilingAsync</c>, which turns a
/// filed email conversation into its record's thread (Record-Anchored). Every other update path refuses both at run time
/// (<c>InheritedAccessPermission.BffOwnedCommunicationColumns</c>), and field security locks them to the BFF
/// (<c>scripts/Set-ThreadFlagFieldSecurity.ps1</c>).
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> A source scan of <c>src/server/**</c> for every write of either column: an indexer assignment, an
/// <c>Add</c>/<c>TryAdd</c>/<c>SetAttributeValue</c>, a collection-initializer entry, a JSON writer call, a
/// <c>[JsonPropertyName(...)]</c> member, or an anonymous member assignment. Each goes through the literal or a constant that holds
/// it (<see cref="KnownAliases"/>, discovered to a fixed point — a constant defined from a known constant is one too — and pinned).
/// A write inside the initializer of <c>new Entity("...")</c> or <c>new DataverseEntity("...")</c>, with no statement end
/// between and no <c>Id =</c> in that initializer, is a <b>create</b>, which is allowed. Every other write is an <b>update</b>. It
/// must be the sanctioned member, or an <see cref="InMemoryOnly"/> entry with its reason. No type in <c>src/server</c> carries a
/// flag column as a JSON name, so none is listed.</para>
/// <para><b>Limits.</b> A key held in a local variable, a caller-supplied payload (refused at run time) and writes outside
/// <c>src/server</c> are not seen. A create built across statements (an entity filled after its constructor) is reported as an
/// update, which fails closed.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same rules over an inline source. An update
/// dictionary, an indexer assignment on an existing row and a <c>SetAttributeValue</c> are reported. A create initializer, a
/// select array, a read and a comparison are not.</para>
/// </remarks>
public class ThreadFlagUpdateWriterGuardTests
{
    private const string Resolver = "src/server/api/Sprk.Bff.Api/Services/Communication/ThreadResolver.cs";

    /// <summary>The one member that may write either flag on an update (owner decision 2026-11-11, option A).</summary>
    private static readonly (string File, string Member) SanctionedUpdater = (Resolver, "AnchorThreadForFilingAsync");

    /// <summary>An indexer assignment on an in-memory row that is never sent to Dataverse, with its reason.</summary>
    private static readonly IReadOnlyList<(string File, string Member, string Reason)> InMemoryOnly = new[]
    {
        ("src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordFamilyReader.cs", "ToNode",
            "The family reader copies a thread's type onto its in-memory node so RecordThreadFiling.ParentsOf can rank it; the node is " +
            "read, never written."),
    };

    /// <summary>The constants that hold either column (repo-relative file, name).</summary>
    private static readonly IReadOnlyList<(string File, string Name)> KnownAliases = new[]
    {
        ("src/server/api/Sprk.Bff.Api/Services/Access/DirectMessageParticipantShares.cs", "ThreadTypeColumn"),
        ("src/server/api/Sprk.Bff.Api/Services/Access/DirectMessageParticipantShares.cs", "DefaultThreadColumn"),
        ("src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectMessageRecipient.cs", "ThreadTypeColumn"),
        ("src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs", "ThreadTypeField"),
        (Resolver, "DefaultThreadMarkerField"),
        ("src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordThreadFiling.cs", "ThreadTypeColumn"),
    };

    [Fact(DisplayName = "fm 054b: a thread's type and default flag are written on update only by ThreadResolver.AnchorThreadForFilingAsync")]
    public void TheThreadFlagsAreUpdatedOnlyByTheSanctionedAnchor()
    {
        var writes = SourceScan.ServerSourceFiles()
            .SelectMany(f => ScanFile(Normalize(SourceScan.Relative(f)), File.ReadAllLines(f), AliasNames(KnownAliases)))
            .ToList();

        // Non-vacuity: the sanctioned update and the typed creates are seen.
        Assert.Contains(writes, w => !w.IsCreate && w.File == SanctionedUpdater.File && w.Member == SanctionedUpdater.Member);
        Assert.True(writes.Count(w => w.IsCreate) >= 4,
            $"At least four thread creates should be seen writing the flags (ThreadResolver x3, DirectThreadAccessService); saw {writes.Count(w => w.IsCreate)}: the scan lost its signal.");
        Assert.Contains(writes, w => w.IsCreate && w.Line > 0 && w.Text.Contains("DefaultThreadMarkerField", StringComparison.Ordinal));

        var problems = Judge(writes, SanctionedUpdater, InMemoryOnly);
        Assert.True(problems.Count == 0,
            "sprk_threadtype and sprk_isdefaultthread are written on update only by ThreadResolver.AnchorThreadForFilingAsync " +
            "(fm task 054b, owner decision option A); creates are allowed:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "fm 054b: every constant that holds a thread flag is one the scan matches")]
    public void EveryConstantHoldingAThreadFlagIsListed()
    {
        var found = AliasFixedPoint(SourceScan.ServerSourceFiles()
            .Select(f => (File: Normalize(SourceScan.Relative(f)), Lines: (IReadOnlyList<string>)File.ReadAllLines(f)))
            .ToList());

        var problems = found.Except(KnownAliases).Select(a => $"NEW constant {a.Name} in {a.File}: list it in KnownAliases")
            .Concat(KnownAliases.Except(found).Select(a => $"STALE alias {a.Name} in {a.File}: remove it"))
            .ToList();
        Assert.True(problems.Count == 0, "A constant holding a thread flag must be listed, so its writes are scanned:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "fm 054b guard control: an update dictionary, an indexer on a row and SetAttributeValue fire; a create initializer, a select and a read do not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var controlLines = ControlSource.Split('\n');
        var aliases = AliasFixedPoint(new[] { (File: "control.cs", Lines: (IReadOnlyList<string>)controlLines) }).Select(a => a.Name).ToArray();
        var writes = ScanFile("control.cs", controlLines, aliases).ToList();
        var problems = Judge(writes, ("control.cs", "AnchorAsync"), new[] { ("control.cs", "Project", "control") });

        // Must fire: each update shape outside the sanctioned member.
        Assert.Contains(problems, p => p.Contains(" UpdateDictionaryAsync:"));
        Assert.Contains(problems, p => p.Contains(" UpdateRowAsync:"));
        Assert.Contains(problems, p => p.Contains(" SetAttributeAsync:"));
        Assert.Contains(problems, p => p.Contains(" TupleAsync:"));
        Assert.Contains(problems, p => p.Contains(" AfterConstructorAsync:"));
        Assert.Contains(problems, p => p.Contains(" AnonymousAsync:"));
        // F4 (i): a constant defined from another known constant, resolved to a fixed point (two hops).
        Assert.Contains(problems, p => p.Contains(" DerivedAliasAsync:"));
        // F4 (ii): a JSON-named member that serializes as a flag column.
        Assert.Contains(problems, p => p.Contains("JsonPropertyName(\"sprk_threadtype\")"));
        Assert.Contains(problems, p => p.Contains("JsonPropertyName(DerivedKey)"));
        // F4 (iii): an entity initializer that sets Id names an existing row: an update, not a create.
        Assert.Contains(problems, p => p.Contains(" IdInitializerAsync:"));
        Assert.Equal(10, problems.Count);

        // Must not fire: the sanctioned update, the create initializers (literal and alias), the in-memory projection, the reads.
        Assert.Contains(writes, w => !w.IsCreate && w.Member == "AnchorAsync");
        Assert.Equal(2, writes.Count(w => w.IsCreate && w.Member == "CreateAsync"));
        Assert.DoesNotContain(writes, w => w.Member is "Reads");
        Assert.Equal(new[] { "FlagKey" }, AliasesIn(controlLines).ToArray());
        Assert.Equal(new[] { "DerivedKey", "FlagKey", "TwiceDerivedKey" }, aliases.OrderBy(a => a, StringComparer.Ordinal).ToArray());
    }

    [Fact(DisplayName = "fm 054b guard control: the alias fixed point does not take a constant defined from an unrelated one")]
    public void AliasFixedPoint_MustNotFire_OnAnUnrelatedDerivedConstant()
    {
        var lines = new[]
        {
            "private const string FlagKey = \"sprk_isdefaultthread\";",
            "private const string NameKey = \"sprk_name\";",
            "private const string OtherKey = Control.NameKey;",
        };

        Assert.Equal(new[] { "FlagKey" }, AliasFixedPoint(new[] { (File: "c.cs", Lines: (IReadOnlyList<string>)lines) }).Select(a => a.Name));
    }

    // ── the scan ───────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Write(string File, string Member, int Line, bool IsCreate, string Text);

    private static List<string> Judge(
        IEnumerable<Write> writes, (string File, string Member) sanctioned, IEnumerable<(string File, string Member, string Reason)> inMemory)
    {
        var memory = inMemory.Select(m => (m.File, m.Member)).ToHashSet();
        return writes
            .Where(w => !w.IsCreate && (w.File, w.Member) != sanctioned && !memory.Contains((w.File, w.Member)))
            .OrderBy(w => w.File, StringComparer.Ordinal).ThenBy(w => w.Line)
            .Select(w => $"UPDATE WRITE at {w.File}:{w.Line} {w.Member}: {w.Text}")
            .ToList();
    }

    private static string[] AliasNames(IEnumerable<(string File, string Name)> aliases) => aliases.Select(a => a.Name).Distinct().ToArray();

    private static IEnumerable<Write> ScanFile(string file, IReadOnlyList<string> lines, IReadOnlyCollection<string> aliases)
    {
        var key = @"(?:""sprk_threadtype""|""sprk_isdefaultthread""" +
                  (aliases.Count > 0 ? @"|(?:\w+\.)*(?:" + string.Join("|", aliases.Select(Regex.Escape)) + @")\b" : string.Empty) + ")";
        var keyed = new[]
        {
            new Regex(@"\[\s*" + key + @"\s*\]\s*=(?![=>])"),
            new Regex(@"\.(?:Add|TryAdd|SetAttributeValue)\(\s*" + key + @"\s*,"),
            new Regex(@"(?:^|[{,])\s*\{\s*" + key + @"\s*,"),
            new Regex(@"\.Write\w*\(\s*" + key),
            // F4 (ii): a member serialized under a flag column's name (a DTO a writer posts).
            new Regex(@"\[\s*(?:[\w.]+\.)?JsonPropertyName(?:Attribute)?\(\s*" + key + @"\s*\)"),
        };

        var code = lines.Select(SourceScan.StripLineComment).ToList();
        var bare = code.Select(l => StringLiteral.Replace(l, "\"\"")).ToList();
        var members = MembersByLine(bare);
        for (var i = 0; i < code.Count; i++)
        {
            if (keyed.Any(r => r.IsMatch(code[i])) || MemberWrite.IsMatch(bare[i]))
                yield return new Write(file, members[i], i + 1, InsideEntityInitializer(bare, i), code[i].Trim());
        }
    }

    /// <summary>
    /// Whether line <paramref name="index"/> sits inside the object initializer of a <c>new Entity(...)</c> or
    /// <c>new DataverseEntity(...)</c> that takes one argument (the table): no statement end between them, and the initializer's
    /// brace still open.
    /// </summary>
    private static bool InsideEntityInitializer(IReadOnlyList<string> bare, int index)
    {
        for (var j = index; j >= 0 && j >= index - 40; j--)
        {
            var match = EntityCreate.Match(bare[j]);
            if (!match.Success)
            {
                if (j < index && bare[j].Contains(';'))
                    return false;
                continue;
            }

            var between = bare[j][match.Index..] + "\n" + string.Join("\n", bare.Skip(j + 1).Take(index - j));
            if (between.Contains(';'))
                return false;
            var depth = between.Count(c => c == '{') - between.Count(c => c == '}');
            if (depth <= 0)
                return false;

            // F4 (iii): an initializer that sets Id names an existing row, so the write is an update.
            return !InitializerSetsId(bare, j, match.Index);
        }

        return false;
    }

    /// <summary>Whether the object initializer opened after <c>new Entity(...)</c> at line <paramref name="start"/> assigns <c>Id</c>.</summary>
    private static bool InitializerSetsId(IReadOnlyList<string> bare, int start, int column)
    {
        var text = new System.Text.StringBuilder();
        var depth = 0;
        var opened = false;
        for (var k = start; k < bare.Count && k <= start + 60; k++)
        {
            var line = k == start ? bare[k][column..] : bare[k];
            foreach (var c in line)
            {
                if (c == '{') { depth++; opened = true; }
                else if (c == '}') depth--;
                if (opened && depth == 0)
                    return InitializerId.IsMatch(text.ToString());
                if (opened)
                    text.Append(c);
            }

            text.Append('\n');
        }

        return InitializerId.IsMatch(text.ToString());
    }

    /// <summary>An <c>Id = ...</c> member assignment at the start of an initializer entry.</summary>
    private static readonly Regex InitializerId = new(@"(?:^|[{,\n])\s*Id\s*=(?![=>])", RegexOptions.Compiled);

    private static readonly Regex EntityCreate = new(@"\bnew\s+(?:[\w.]+\.)?(?:Dataverse)?Entity\s*\(\s*[^,()]*\)", RegexOptions.Compiled);

    /// <summary>An anonymous payload's member named after a column, matched with strings blanked.</summary>
    private static readonly Regex MemberWrite = new(
        @"(?<![\w""])(?:sprk_threadtype|sprk_isdefaultthread)\s*=(?![=>])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Alias = new(
        @"\b(?:const|static\s+readonly)\s+string\s+(\w+)\s*=\s*""(?:sprk_threadtype|sprk_isdefaultthread)""\s*;", RegexOptions.Compiled);

    private static IEnumerable<string> AliasesIn(IReadOnlyList<string> lines) =>
        lines.Select(SourceScan.StripLineComment).SelectMany(l => Alias.Matches(l).Select(m => m.Groups[1].Value));

    /// <summary>A constant defined from another constant (<c>const string F = X.DefaultThreadColumn;</c>): its name and the source's.</summary>
    private static readonly Regex DerivedAlias = new(
        @"\b(?:const|static\s+readonly)\s+string\s+(\w+)\s*=\s*(?:[\w.]+\.)?(\w+)\s*;", RegexOptions.Compiled);

    /// <summary>
    /// F4 (i): every constant holding a flag — one holding the literal (<see cref="AliasesIn"/>), then, to a fixed point, one defined
    /// from a constant already found. Matched by name across files, so a same-named unrelated source over-reports (fails closed).
    /// </summary>
    private static List<(string File, string Name)> AliasFixedPoint(IReadOnlyList<(string File, IReadOnlyList<string> Lines)> files)
    {
        var stripped = files.Select(f => (f.File, Lines: f.Lines.Select(SourceScan.StripLineComment).ToList())).ToList();
        var found = new List<(string File, string Name)>();
        foreach (var (file, lines) in stripped)
            found.AddRange(AliasesIn(lines).Select(name => (file, name)));

        bool grew;
        do
        {
            grew = false;
            var names = found.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var (file, lines) in stripped)
            {
                foreach (Match m in lines.SelectMany(l => DerivedAlias.Matches(l)))
                {
                    var alias = (file, m.Groups[1].Value);
                    if (names.Contains(m.Groups[2].Value) && !found.Contains(alias))
                    {
                        found.Add(alias);
                        grew = true;
                    }
                }
            }
        }
        while (grew);

        return found.Distinct().ToList();
    }

    private static readonly Regex Declaration = new(
        @"^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|abstract|extern|unsafe|new|partial|readonly)\s+)*" +
        @"(?!(?:return|await|new|throw|yield|else|case|in|is|as|var|using|select|where|from|let|goto|when)\b)" +
        @"(?:[A-Za-z_][\w.]*(?:<[^;{}]*?>)?|\([^;{}]*?\))(?:\[\])?\??\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^;{}()]*>)?\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new(@"@""(?:[^""]|"""")*""|\$?""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)'", RegexOptions.Compiled);

    private static string Normalize(string relative) => relative.Replace('\\', '/');

    /// <summary>The member each line belongs to (the same detector as <see cref="SubmissionMarkerWriterGuardTests"/>).</summary>
    private static string[] MembersByLine(IReadOnlyList<string> bare)
    {
        var result = new string[bare.Count];
        var stack = new List<(string Name, int Depth, bool Opened)>();
        var depth = 0;
        for (var i = 0; i < bare.Count; i++)
        {
            var line = bare[i];
            if (Declaration.Match(line) is { Success: true } declared)
                stack.Add((declared.Groups["name"].Value, depth, false));
            result[i] = stack.Count > 0 ? stack[^1].Name : "(type level)";

            foreach (var c in line)
            {
                if (c == '{')
                {
                    depth++;
                    if (stack.Count > 0 && !stack[^1].Opened && depth == stack[^1].Depth + 1)
                        stack[^1] = stack[^1] with { Opened = true };
                }
                else if (c == '}')
                {
                    depth--;
                    while (stack.Count > 0 && stack[^1].Opened && depth <= stack[^1].Depth)
                        stack.RemoveAt(stack.Count - 1);
                }
                else if (c == ';')
                {
                    while (stack.Count > 0 && !stack[^1].Opened && depth == stack[^1].Depth)
                        stack.RemoveAt(stack.Count - 1);
                }
            }
        }

        return result;
    }

    // The control source. It is never compiled; only the detector reads it.
    private const string ControlSource = """
        internal sealed class Control
        {
            private const string FlagKey = "sprk_isdefaultthread";
            private const string DerivedKey = Control.FlagKey;
            private static readonly string TwiceDerivedKey = DerivedKey;

            public async Task<Guid> CreateAsync(CancellationToken ct)
            {
                var thread = new DataverseEntity("sprk_communicationthread")
                {
                    ["sprk_name"] = "x",
                    ["sprk_threadtype"] = new OptionSetValue(100000001),
                    [FlagKey] = true,
                };
                return await _entities.CreateAsync(thread, ct);
            }

            public async Task AnchorAsync(Guid id, CancellationToken ct)
            {
                var fields = new Dictionary<string, object> { ["sprk_threadtype"] = new OptionSetValue(100000000) };
                await _entities.UpdateAsync("sprk_communicationthread", id, fields, ct);
            }

            public async Task UpdateDictionaryAsync(Guid id, CancellationToken ct)
            {
                var fields = new Dictionary<string, object>
                {
                    ["sprk_threadtype"] = new OptionSetValue(100000001),
                };
                await _entities.UpdateAsync("sprk_communicationthread", id, fields, ct);
            }

            public async Task UpdateRowAsync(Entity row, CancellationToken ct)
            {
                row[FlagKey] = false;
                await _entities.UpdateAsync(row, ct);
            }

            public void SetAttributeAsync(Entity row) => row.SetAttributeValue("sprk_threadtype", new OptionSetValue(1));

            public Dictionary<string, object> TupleAsync() => new() { { "sprk_isdefaultthread", true } };

            public Entity AfterConstructorAsync(Guid id)
            {
                var row = new Entity("sprk_communicationthread", id);
                row["sprk_threadtype"] = new OptionSetValue(100000001);
                return row;
            }

            public object AnonymousAsync() => new { sprk_threadtype = 100000001 };

            public async Task DerivedAliasAsync(Guid id, CancellationToken ct)
            {
                var fields = new Dictionary<string, object> { [TwiceDerivedKey] = false };
                await _entities.UpdateAsync("sprk_communicationthread", id, fields, ct);
            }

            public async Task IdInitializerAsync(Guid id, CancellationToken ct)
            {
                var thread = new DataverseEntity("sprk_communicationthread")
                {
                    ["sprk_threadtype"] = new OptionSetValue(100000000),
                    Id = id,
                };
                await _entities.UpdateAsync(thread, ct);
            }

            public sealed class FlagPayload
            {
                [JsonPropertyName("sprk_threadtype")]
                public int ThreadType { get; init; }

                [JsonPropertyName(DerivedKey)]
                public bool IsDefault { get; init; }
            }

            public void Project(Entity node, int type) => node["sprk_threadtype"] = new OptionSetValue(type);

            public bool Reads(Entity row)
            {
                var columns = new[] { "sprk_threadtype", FlagKey };
                var isDirect = row.GetAttributeValue<OptionSetValue>("sprk_threadtype")?.Value == 100000001;
                return isDirect && row["sprk_threadtype"] == null && columns.Length == 2;
            }
        }
        """;
}
