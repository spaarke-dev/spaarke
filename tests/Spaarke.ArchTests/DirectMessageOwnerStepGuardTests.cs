using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3a. A communication is assigned to the Direct message owner team in exactly one
/// member: <c>DirectMessageOwnerStep.WriteDirectTeamOwnerAsync</c>, which runs only after the grant, only for a Direct message filed
/// under no record, and only when the lookup resolved. The team's id comes only from the lookup
/// (<c>DirectMessageOwnerTeam.ResolveDirectMessageOwnerTeamAsync</c> / <c>IdentifyDirectMessageOwnerTeamAsync</c>, whose answers are
/// <c>DirectMessageOwnerTeamResolution</c> / <c>DirectMessageOwnerTeamIdentity</c>).
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> A source scan of <c>src/server/**</c>. A file <b>reads the Direct team</b> when it calls either lookup
/// method or names either answer type. A file <b>writes an owner</b> when it assigns the owner key (<c>"ownerid"</c>,
/// <c>"ownerid@odata.bind"</c>, or a constant holding either) through an indexer, <c>Add</c>/<c>TryAdd</c> or
/// <c>SetAttributeValue</c>. The rule: every file that reads the Direct team is listed, either as the owner step or as a reader with its
/// reason; a listed reader writes no owner; and the owner step writes an owner only in its one member.</para>
/// <para><b>Limits.</b> A team id passed on from a listed reader to another file, and a key held in a local variable, are not seen.
/// The lookup's own file is not a reader (it computes the answer).</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same rules over inline sources.</para>
/// </remarks>
public class DirectMessageOwnerStepGuardTests
{
    private const string Lookup = "src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/DirectMessageOwnerTeam.cs";
    private const string OwnerStep = "src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectMessageOwnerStep.cs";

    /// <summary>The one member that may assign a communication to the Direct team.</summary>
    private const string OwnerStepMember = "WriteDirectTeamOwnerAsync";

    /// <summary>The files that read the Direct team and write no owner, with their reasons.</summary>
    private static readonly IReadOnlyDictionary<string, string> Readers = new Dictionary<string, string>
    {
        ["src/server/api/Sprk.Bff.Api/Services/Access/DirectMessageParticipantShares.cs"] =
            "uac-r2 task 198: compares a message's owning team with the resolved Direct team to decide its participant shares.",
        ["src/server/api/Sprk.Bff.Api/Services/Access/SecureChildShareReconciliationJob.cs"] =
            "uac-r2 task 198: lists the messages the resolved Direct team owns, to re-grant their participants.",
    };

    [Fact(DisplayName = "fm 054 piece 3a: only DirectMessageOwnerStep.WriteDirectTeamOwnerAsync reads the Direct team and writes an owner")]
    public void OnlyTheOwnerStepAssignsACommunicationToTheDirectTeam()
    {
        var files = SourceScan.ServerSourceFiles()
            .Select(f => (File: SourceScan.Relative(f).Replace('\\', '/'), Lines: (IReadOnlyList<string>)File.ReadAllLines(f)))
            .Where(f => f.File != Lookup)
            .ToList();

        // Non-vacuity: the owner step is seen reading the team and writing in its one member; the listed readers are seen reading.
        var step = files.Single(f => f.File == OwnerStep);
        Assert.True(ReadsTheDirectTeam(step.Lines), "The owner step should be seen reading the Direct team: the scan lost its signal.");
        Assert.Contains(OwnerWrites(step.Lines), w => w.Member == OwnerStepMember);
        foreach (var reader in Readers.Keys)
            Assert.True(ReadsTheDirectTeam(files.Single(f => f.File == reader).Lines), $"{reader} should be seen reading the Direct team.");

        var problems = Judge(files, OwnerStep, OwnerStepMember, Readers.Keys.ToHashSet());
        Assert.True(problems.Count == 0,
            "A communication is assigned to the Direct message owner team only by DirectMessageOwnerStep.WriteDirectTeamOwnerAsync " +
            "(fm task 054 piece 3a). A new reader of the team is listed with its reason and writes no owner:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "fm 054 piece 3a guard control: an unlisted reader, a reader that writes an owner and a second owner-step member fire; the sanctioned shapes do not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var files = new List<(string File, IReadOnlyList<string> Lines)>
        {
            ("step.cs", StepControl.Split('\n')),
            ("reader.cs", ReaderControl.Split('\n')),
            ("unlisted.cs", UnlistedControl.Split('\n')),
            ("owners.cs", OwnersOnlyControl.Split('\n')),
            ("typed.cs", TypedAnswerControl.Split('\n')),
        };

        var problems = Judge(files, "step.cs", "WriteAsync", new HashSet<string> { "reader.cs" });

        // Must fire.
        Assert.Contains(problems, p => p.StartsWith("step.cs", StringComparison.Ordinal) && p.Contains(" AlsoWritesAsync:"));
        Assert.Contains(problems, p => p.StartsWith("reader.cs", StringComparison.Ordinal) && p.Contains("writes an owner"));
        Assert.Contains(problems, p => p.StartsWith("unlisted.cs", StringComparison.Ordinal) && p.Contains("not listed"));
        Assert.Contains(problems, p => p.StartsWith("typed.cs", StringComparison.Ordinal) && p.Contains("not listed"));
        Assert.Equal(5, problems.Count);

        // Must not fire: the sanctioned member, a file that writes owners but never reads the Direct team, reads and comparisons.
        Assert.Single(problems, p => p.StartsWith("step.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.StartsWith("owners.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(OwnerWrites(StepControl.Split('\n')), w => w.Member == "ReadsAsync");
    }

    // ── the scan ───────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record OwnerWrite(int Line, string Member, string Text);

    private static List<string> Judge(
        IReadOnlyList<(string File, IReadOnlyList<string> Lines)> files, string ownerStep, string ownerStepMember, IReadOnlySet<string> readers)
    {
        var problems = new List<string>();
        foreach (var (file, lines) in files.OrderBy(f => f.File, StringComparer.Ordinal))
        {
            if (!ReadsTheDirectTeam(lines))
                continue;

            var writes = OwnerWrites(lines);
            if (file == ownerStep)
            {
                problems.AddRange(writes.Where(w => w.Member != ownerStepMember)
                    .Select(w => $"{file}:{w.Line} {w.Member}: an owner write outside {ownerStepMember}: {w.Text}"));
            }
            else if (readers.Contains(file))
            {
                problems.AddRange(writes.Select(w => $"{file}:{w.Line} {w.Member}: a listed reader of the Direct team writes an owner: {w.Text}"));
            }
            else
            {
                problems.Add($"{file}: reads the Direct team but is not listed (list it with its reason; it may not write an owner)");
                problems.AddRange(writes.Select(w => $"{file}:{w.Line} {w.Member}: writes an owner: {w.Text}"));
            }
        }

        return problems;
    }

    private static readonly Regex DirectTeamRead = new(
        @"\b(?:ResolveDirectMessageOwnerTeamAsync|IdentifyDirectMessageOwnerTeamAsync|DirectMessageOwnerTeamResolution|DirectMessageOwnerTeamIdentity)\b",
        RegexOptions.Compiled);

    private static bool ReadsTheDirectTeam(IReadOnlyList<string> lines) =>
        lines.Select(SourceScan.StripLineComment).Any(l => !IsDocComment(l) && DirectTeamRead.IsMatch(l));

    private static bool IsDocComment(string line) => line.TrimStart().StartsWith("///", StringComparison.Ordinal);

    private const string OwnerKeyLiteral = @"(?:\$@|@\$|\$|@)?""ownerid(?:@odata\.bind)?""";

    private static readonly Regex OwnerKeyConst = new(
        @"\b(?:const|static\s+readonly)\s+string\s+(?<name>\w+)\s*=\s*" + OwnerKeyLiteral + @"\s*;", RegexOptions.Compiled);

    private static List<OwnerWrite> OwnerWrites(IReadOnlyList<string> lines)
    {
        var code = lines.Select(SourceScan.StripLineComment).ToList();
        var aliases = code.SelectMany(l => OwnerKeyConst.Matches(l).Select(m => m.Groups["name"].Value)).Distinct().ToList();
        var key = "(?:" + OwnerKeyLiteral + (aliases.Count > 0 ? @"|(?:\w+\.)*(?:" + string.Join("|", aliases.Select(Regex.Escape)) + @")\b" : "") + ")";
        var shapes = new[]
        {
            new Regex(@"\[\s*" + key + @"\s*\]\s*=(?![=>])"),
            new Regex(@"\.(?:Add|TryAdd|SetAttributeValue)\(\s*" + key + @"\s*,"),
        };

        var bare = code.Select(l => StringLiteral.Replace(l, m => OwnerKeyLiteralRegex.IsMatch(m.Value) ? m.Value : "\"\"")).ToList();
        var members = MembersByLine(bare);
        var writes = new List<OwnerWrite>();
        for (var i = 0; i < code.Count; i++)
        {
            if (shapes.Any(r => r.IsMatch(code[i])))
                writes.Add(new OwnerWrite(i + 1, members[i], code[i].Trim()));
        }

        return writes;
    }

    private static readonly Regex OwnerKeyLiteralRegex = new("^" + OwnerKeyLiteral + "$", RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new(@"@""(?:[^""]|"""")*""|\$?""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)'", RegexOptions.Compiled);

    private static readonly Regex Declaration = new(
        @"^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|abstract|extern|unsafe|new|partial|readonly)\s+)*" +
        @"(?!(?:return|await|new|throw|yield|else|case|in|is|as|var|using|select|where|from|let|goto|when)\b)" +
        @"(?:[A-Za-z_][\w.]*(?:<[^;{}]*?>)?|\([^;{}]*?\))(?:\[\])?\??\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^;{}()]*>)?\s*\(",
        RegexOptions.Compiled);

    /// <summary>The member each line belongs to (the innermost method declared around it), as the thread-flag guard maps it.</summary>
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

    // The control sources. They are never compiled; only the detector reads them.
    private const string StepControl = """
        internal sealed class Step
        {
            private const string OwnerColumn = "ownerid";

            public async Task OwnAsync(CancellationToken ct)
            {
                var resolution = await _lookup.ResolveDirectMessageOwnerTeamAsync(ct);
                await WriteAsync(resolution.TeamId!.Value, ct);
            }

            private async Task WriteAsync(Guid team, CancellationToken ct)
            {
                var owner = new Dictionary<string, object> { [OwnerColumn] = new EntityReference("team", team) };
                await _dataverse.UpdateIfRowVersionMatchesAsync("sprk_communication", _id, "1", owner, ct);
            }

            private async Task AlsoWritesAsync(Guid team, CancellationToken ct)
            {
                var row = new Entity("sprk_communicationattachment", _id);
                row["ownerid"] = new EntityReference("team", team);
            }

            private async Task ReadsAsync(CancellationToken ct)
            {
                var query = new QueryExpression("sprk_communication") { ColumnSet = new ColumnSet(OwnerColumn, "ownerid") };
                var same = row.GetAttributeValue<EntityReference>(OwnerColumn)?.Id == team;
            }
        }
        """;

    private const string ReaderControl = """
        internal sealed class Reader
        {
            public async Task<bool> IsDirectAsync(Guid owningTeam, CancellationToken ct)
            {
                var direct = await lookup.ResolveDirectMessageOwnerTeamAsync(ct);
                return direct.IsResolved && direct.TeamId == owningTeam;
            }

            public void Move(Entity row, Guid team) => row.SetAttributeValue("ownerid@odata.bind", $"/teams({team})");
        }
        """;

    private const string UnlistedControl = """
        internal sealed class Unlisted
        {
            public async Task AssignAsync(CancellationToken ct)
            {
                var identity = await lookup.IdentifyDirectMessageOwnerTeamAsync(ct);
                fields.Add("ownerid", new EntityReference("team", identity.TeamId!.Value));
            }
        }
        """;

    private const string OwnersOnlyControl = """
        internal sealed class OwnersOnly
        {
            public void Route(Dictionary<string, object> fields, Guid team) { fields["ownerid"] = new EntityReference("team", team); }
        }
        """;

    private const string TypedAnswerControl = """
        internal sealed class TypedAnswer
        {
            public bool Holds(DirectMessageOwnerTeamResolution answer) => answer.IsResolved;
        }
        """;
}
