using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3a. A communication is assigned to the Direct message owner team in exactly one
/// member: <c>DirectMessageOwnerStep.WriteDirectTeamOwnerAsync</c>, which runs only after the grant, only for a Direct message filed
/// under no record, and only when the lookup resolved. The team's id comes only from the lookup (<c>DirectMessageOwnerTeam</c>).
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> A source scan of <c>src/server/**</c>. A file <b>reads the Direct team</b> when it names the lookup type
/// (<c>DirectMessageOwnerTeam</c>), either lookup method (<c>ResolveDirectMessageOwnerTeamAsync</c> /
/// <c>IdentifyDirectMessageOwnerTeamAsync</c>), either answer type (<c>DirectMessageOwnerTeamResolution</c> /
/// <c>DirectMessageOwnerTeamIdentity</c>), the shared Direct-team checks (<c>IsDirectOwnerTeamAsync</c> /
/// <c>CannotBeDirectOwnerTeamAsync</c>), or either configuration key by its literal (<c>"DirectMessage:BusinessUnitName"</c> /
/// <c>"DirectMessage:OwnerTeamName"</c>). A file <b>writes an owner</b> when it assigns the owner key (<c>"ownerid"</c>,
/// <c>"ownerid@odata.bind"</c>, or a constant holding either) through an indexer, a <c>{ key, value }</c> collection initializer,
/// <c>Add</c>/<c>TryAdd</c> or <c>SetAttributeValue</c>; or builds an <c>AssignRequest</c>; or assigns through a writer's
/// <c>.AssignAsync(</c>.</para>
/// <para><b>The rule.</b> Every file that reads the Direct team is the owner step or is listed (<see cref="Listing"/>) with its reason.
/// The owner step writes an owner only in its one member. A listed file writes an owner only in the members listed for it, each write
/// keeps the file's own rule when it has one, and no member of it both reads the Direct team and writes an owner, except a member of
/// <see cref="DirectTeamUnfileMembers"/>.</para>
/// <para><b>Limits.</b> A team id passed on from a listed file to another file, a team id carried between members of one file, and a
/// key held in a local variable, are not seen. The lookup's own file is not a reader (it computes the answer).</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same rules over inline sources.</para>
/// </remarks>
public class DirectMessageOwnerStepGuardTests
{
    private const string Lookup = "src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/DirectMessageOwnerTeam.cs";
    private const string OwnerStep = "src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectMessageOwnerStep.cs";
    private const string Reconciler = "src/server/api/Sprk.Bff.Api/Services/Access/SecureChildReconciler.cs";

    /// <summary>The one member that may assign a communication to the Direct team.</summary>
    private const string OwnerStepMember = "WriteDirectTeamOwnerAsync";

    /// <summary>
    /// The seam for fm task 054 piece 3b: the <c>SecureChildReconciler</c> members that may read the Direct team and write an owner,
    /// to move a content row back to the Direct team on unfile. Empty: piece 3b's unfile writes the Direct owner through the owner
    /// step's one member (<see cref="OwnerStepMember"/>, called by <c>DirectMessageOwnerStep.AfterReparentAsync</c>), and the
    /// reconcile moves content rows by the record ownership resolver's answer through its listed writer members, so no reconciler
    /// member reads the Direct team and writes an owner.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DirectTeamUnfileMembers = new Dictionary<string, string>();

    /// <summary>One file that reads the Direct team, other than the owner step.</summary>
    /// <param name="Reason">Why it reads the Direct team.</param>
    /// <param name="WriterMembers">The members that may write an owner, each with its reason; empty when the file writes none.</param>
    /// <param name="WriteRule">A rule every owner write of the file keeps, with its name; <c>null</c> for none.</param>
    /// <param name="DirectTeamWriters">The members that may both read the Direct team and write an owner.</param>
    private sealed record Listed(
        string Reason,
        IReadOnlyDictionary<string, string> WriterMembers,
        (string Name, Func<OwnerWrite, IReadOnlyList<string>, bool> Holds)? WriteRule = null,
        IReadOnlyDictionary<string, string>? DirectTeamWriters = null);

    private static readonly IReadOnlyDictionary<string, string> NoWrites = new Dictionary<string, string>();

    /// <summary>An owner write naming a systemuser owner (declared before <see cref="Listing"/>, which uses it).</summary>
    private static readonly Regex SystemUserOwner = new(@"new\s+EntityReference\(\s*(?:SystemUserEntity|""systemuser"")\s*,", RegexOptions.Compiled);

    /// <summary>The files that read the Direct team, other than the owner step and the lookup, with their reasons.</summary>
    private static readonly IReadOnlyDictionary<string, Listed> Listing = new Dictionary<string, Listed>
    {
        ["src/server/api/Sprk.Bff.Api/Services/Access/DirectMessageParticipantShares.cs"] = new(
            "uac-r2 task 198: compares a message's owning team with the resolved Direct team to decide its participant shares.", NoWrites),
        ["src/server/api/Sprk.Bff.Api/Services/Access/SecureChildShareReconciliationJob.cs"] = new(
            "uac-r2 task 198: lists the messages the resolved Direct team owns, to re-grant their participants.", NoWrites),
        ["src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs"] = new(
            "registers the lookup in DI.", NoWrites),
        ["src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs"] = new(
            "uac-r2 task 191c: passes the lookup to ExternalTeamShareSynchronizer.", NoWrites),
        ["src/server/api/Sprk.Bff.Api/Services/Access/ExternalTeamShareSynchronizer.cs"] = new(
            "uac-r2 task 191c: compares a row's owning team with the resolved Direct team, to report Direct message rows it leaves " +
            "to DirectMessageParticipantShares.",
            new Dictionary<string, string>
            {
                ["ReownAsync"] = "re-owns a flagged user's row to the team its own I-6 question answers (RecordOwnershipResolver).",
            },
            ("the owner written is the ownership resolver's answer, never a team it chose",
                static (write, _) => write.Text.Contains("resolution.OwningTeamId", StringComparison.Ordinal))),
        ["src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectThreadAccessService.cs"] = new(
            "uac-r2 task 198: asks whether a message's owning team is the Direct team, to choose its participant grant.",
            new Dictionary<string, string>
            {
                ["FindOrCreateDirectThreadAsync"] = "creates a Direct thread owned by the calling user.",
            },
            ("the owner written is a systemuser, never a team", static (write, _) => SystemUserOwner.IsMatch(write.Text))),
        ["src/server/api/Sprk.Bff.Api/Services/Signals/GateDecisionRecordSharing.cs"] = new(
            "D-215: a Direct message's gate Decision Records follow it to the Direct team; it asks whether the message's owner is that team.",
            new Dictionary<string, string>
            {
                ["ReownAsync"] = "re-owns a gate Decision Record to its filing's team (D-169).",
                ["ReownToDirectAsync"] = "re-owns a gate Decision Record to the Direct team its message is owned by (D-215).",
            },
            ("it assigns sprk_decisionrecord only, never a communication",
                static (write, code) => AssignsOnly("sprk_decisionrecord", write, code))),
        ["src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs"] = new(
            "fm task 054 (F1): answers the owner of a message's content row; a row whose message the Direct team owns is the " +
            "Direct team's, decided by the lookup and the shared Direct-team check, never a default team.",
            new Dictionary<string, string>
            {
                ["ApplyTo"] = "writes a resolved owner onto a new row (RecordOwnerResolution.ApplyTo).",
                ["SettleOwnerAsync"] = "the re-file's owner write, to the team the rule answered.",
                ["ReleaseOwnSecureWriteAsync"] = "puts back the ordinary owner a re-file's own Secure write replaced.",
            }),
        [Reconciler] = new(
            "uac-r2 task 198: hands the Direct team to the participant-share reconcile after a re-file.",
            new Dictionary<string, string>
            {
                ["AssignAsync"] = "the secure-child reconcile's owner write, to the team the record ownership resolver answered.",
                ["RestoreStartOwnerAsync"] = "puts back the owner a row started the reconcile with.",
                ["ReownOrdinaryRowCoreAsync"] = "re-owns an ordinary row to its parents' business-unit team after a parent move.",
                ["ReownFiledRootAsync"] = "re-owns a filed work assignment or project to its family's team.",
            },
            DirectTeamWriters: DirectTeamUnfileMembers),
    };

    [Fact(DisplayName = "fm 054 piece 3a: only DirectMessageOwnerStep.WriteDirectTeamOwnerAsync reads the Direct team and writes an owner")]
    public void OnlyTheOwnerStepAssignsACommunicationToTheDirectTeam()
    {
        var files = SourceScan.ServerSourceFiles()
            .Select(f => (File: SourceScan.Relative(f).Replace('\\', '/'), Lines: (IReadOnlyList<string>)File.ReadAllLines(f)))
            .Where(f => f.File != Lookup)
            .ToList();

        // Non-vacuity: the owner step is seen reading the team and writing in its one member; every listed file is seen reading, and
        // every member listed as a writer is seen writing.
        var step = files.Single(f => f.File == OwnerStep);
        Assert.True(ReadsTheDirectTeam(step.Lines), "The owner step should be seen reading the Direct team: the scan lost its signal.");
        Assert.Contains(OwnerWrites(step.Lines), w => w.Member == OwnerStepMember);
        foreach (var (file, listed) in Listing)
        {
            var lines = files.Single(f => f.File == file).Lines;
            Assert.True(ReadsTheDirectTeam(lines), $"{file} should be seen reading the Direct team.");
            foreach (var member in listed.WriterMembers.Keys)
                Assert.True(OwnerWrites(lines).Any(w => w.Member == member), $"{file} should be seen writing an owner in {member}.");
        }

        var problems = Judge(files, OwnerStep, OwnerStepMember, Listing);
        Assert.True(problems.Count == 0,
            "A communication is assigned to the Direct message owner team only by DirectMessageOwnerStep.WriteDirectTeamOwnerAsync " +
            "(fm task 054 piece 3a). A new reader of the team is listed with its reason, and writes an owner only where it is listed to:\n" +
            string.Join("\n", problems));
    }

    [Fact(DisplayName = "fm 054 piece 3a guard control: each signal and owner-write shape fires; the sanctioned shapes do not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var files = new List<(string File, IReadOnlyList<string> Lines)>
        {
            ("step.cs", StepControl.Split('\n')),
            ("reader.cs", ReaderControl.Split('\n')),
            ("unlisted.cs", UnlistedControl.Split('\n')),
            ("owners.cs", OwnersOnlyControl.Split('\n')),
            ("typed.cs", TypedAnswerControl.Split('\n')),
            ("lookuptype.cs", LookupTypeControl.Split('\n')),
            ("isdirect.cs", IsDirectControl.Split('\n')),
            ("cannotbe.cs", CannotBeControl.Split('\n')),
            ("refusalonly.cs", RefusalOnlyControl.Split('\n')),
            ("decisions.cs", DecisionRecordControl.Split('\n')),
            ("unfile.cs", UnfileControl.Split('\n')),
            ("unitkey.cs", UnitKeyControl.Split('\n')),
            ("teamkey.cs", TeamKeyControl.Split('\n')),
            ("otherkey.cs", OtherKeyControl.Split('\n')),
        };
        var seam = new Dictionary<string, string> { ["UnfileAsync"] = "control" };
        var listing = new Dictionary<string, Listed>
        {
            ["reader.cs"] = new("control", NoWrites),
            ["decisions.cs"] = new("control",
                new Dictionary<string, string> { ["ReownAsync"] = "control", ["ReownCommunicationAsync"] = "control" },
                ("decision records only", static (write, code) => AssignsOnly("sprk_decisionrecord", write, code))),
            ["unfile.cs"] = new("control",
                new Dictionary<string, string> { ["MoveAsync"] = "control", ["ReadsAndMovesAsync"] = "control" },
                DirectTeamWriters: seam),
        };

        var problems = Judge(files, "step.cs", "WriteAsync", listing);

        // Must fire: a second owner-step member; a listed reader that writes; each new signal in an unlisted file; each new write
        // shape; a decision-record writer assigning a communication; a member that reads the team and writes, outside the seam.
        Assert.Contains(problems, p => p.StartsWith("step.cs", StringComparison.Ordinal) && p.Contains(" AlsoWritesAsync:"));
        Assert.Contains(problems, p => p.StartsWith("step.cs", StringComparison.Ordinal) && p.Contains(" PairWritesAsync:"));
        Assert.Contains(problems, p => p.StartsWith("step.cs", StringComparison.Ordinal) && p.Contains(" RequestWritesAsync:"));
        Assert.Contains(problems, p => p.StartsWith("reader.cs", StringComparison.Ordinal) && p.Contains("writes an owner"));
        Assert.Contains(problems, p => p.StartsWith("unlisted.cs", StringComparison.Ordinal) && p.Contains("not listed"));
        Assert.Contains(problems, p => p.StartsWith("typed.cs", StringComparison.Ordinal) && p.Contains("not listed"));
        Assert.Contains(problems, p => p.StartsWith("lookuptype.cs", StringComparison.Ordinal) && p.Contains("not listed"));
        Assert.Contains(problems, p => p.StartsWith("isdirect.cs", StringComparison.Ordinal) && p.Contains("not listed"));
        Assert.Contains(problems, p => p.StartsWith("cannotbe.cs", StringComparison.Ordinal) && p.Contains("not listed"));
        Assert.Contains(problems, p => p.StartsWith("decisions.cs", StringComparison.Ordinal) && p.Contains(" ReownCommunicationAsync:")
                                       && p.Contains("decision records only"));
        Assert.Contains(problems, p => p.StartsWith("unfile.cs", StringComparison.Ordinal) && p.Contains(" ReadsAndMovesAsync:")
                                       && p.Contains("reads the Direct team and writes an owner"));
        // K4: either configuration key read by its literal is a read of the Direct team.
        Assert.Contains(problems, p => p.StartsWith("unitkey.cs", StringComparison.Ordinal) && p.Contains("not listed"));
        Assert.Contains(problems, p => p.StartsWith("teamkey.cs", StringComparison.Ordinal) && p.Contains("not listed"));

        // Must not fire: the sanctioned member, a file that writes owners but never reads the Direct team, a file that names only the
        // refusal codes, reads and comparisons, column arrays, a doc comment, the decision-record writer's own table, a listed
        // writer member that does not read the team, the seam member, and another configuration key.
        Assert.True(problems.Count == 15, string.Join("\n", problems));
        Assert.DoesNotContain(problems, p => p.StartsWith("otherkey.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => Regex.IsMatch(p, @"^step\.cs:\d+ WriteAsync:"));
        Assert.DoesNotContain(problems, p => p.StartsWith("owners.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.StartsWith("refusalonly.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => Regex.IsMatch(p, @"^decisions\.cs:\d+ ReownAsync:"));
        Assert.DoesNotContain(problems, p => Regex.IsMatch(p, @"^unfile\.cs:\d+ MoveAsync:"));
        Assert.DoesNotContain(problems, p => Regex.IsMatch(p, @"^unfile\.cs:\d+ UnfileAsync:"));
        Assert.DoesNotContain(OwnerWrites(StepControl.Split('\n')), w => w.Member == "ReadsAsync");
        Assert.DoesNotContain(OwnerWrites(StepControl.Split('\n')), w => w.Member == "ColumnsAsync");
    }

    // ── the scan ───────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record OwnerWrite(int Line, string Member, string Text);

    private static List<string> Judge(
        IReadOnlyList<(string File, IReadOnlyList<string> Lines)> files, string ownerStep, string ownerStepMember,
        IReadOnlyDictionary<string, Listed> listing)
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
            else if (listing.TryGetValue(file, out var listed))
            {
                var code = lines.Select(SourceScan.StripLineComment).ToList();
                var readingMembers = MembersReadingTheDirectTeam(lines);
                var seam = listed.DirectTeamWriters ?? NoWrites;
                foreach (var w in writes)
                {
                    if (!listed.WriterMembers.ContainsKey(w.Member) && !seam.ContainsKey(w.Member))
                        problems.Add($"{file}:{w.Line} {w.Member}: a listed reader of the Direct team writes an owner outside its listed members: {w.Text}");
                    else if (listed.WriteRule is { } rule && !rule.Holds(w, code))
                        problems.Add($"{file}:{w.Line} {w.Member}: an owner write breaks the file's rule ({rule.Name}): {w.Text}");

                    if (readingMembers.Contains(w.Member) && !seam.ContainsKey(w.Member))
                        problems.Add($"{file}:{w.Line} {w.Member}: reads the Direct team and writes an owner in one member: {w.Text}");
                }
            }
            else
            {
                problems.Add($"{file}: reads the Direct team but is not listed (list it with its reason and the owner writes it may make)");
                problems.AddRange(writes.Select(w => $"{file}:{w.Line} {w.Member}: writes an owner: {w.Text}"));
            }
        }

        return problems;
    }

    private static readonly Regex DirectTeamRead = new(
        @"\b(?:ResolveDirectMessageOwnerTeamAsync|IdentifyDirectMessageOwnerTeamAsync|DirectMessageOwnerTeamResolution|" +
        @"DirectMessageOwnerTeamIdentity|DirectMessageOwnerTeam|IsDirectOwnerTeamAsync|CannotBeDirectOwnerTeamAsync)\b" +
        // The two configuration keys, read by their literals (K4).
        @"|""DirectMessage:(?:BusinessUnitName|OwnerTeamName)""",
        RegexOptions.Compiled);

    private static bool ReadsTheDirectTeam(IReadOnlyList<string> lines) =>
        lines.Select(SourceScan.StripLineComment).Any(l => !IsDocComment(l) && DirectTeamRead.IsMatch(l));

    /// <summary>The members (as <see cref="MembersByLine"/> maps them) holding a line that reads the Direct team.</summary>
    private static HashSet<string> MembersReadingTheDirectTeam(IReadOnlyList<string> lines)
    {
        var code = lines.Select(SourceScan.StripLineComment).ToList();
        var members = MembersByLine(Bare(code));
        return code.Select((l, i) => (Line: l, Member: members[i]))
            .Where(x => !IsDocComment(x.Line) && DirectTeamRead.IsMatch(x.Line))
            .Select(x => x.Member)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsDocComment(string line) => line.TrimStart().StartsWith("///", StringComparison.Ordinal);

    private const string OwnerKeyLiteral = @"(?:\$@|@\$|\$|@)?""ownerid(?:@odata\.bind)?""";

    private static readonly Regex OwnerKeyConst = new(
        @"\b(?:const|static\s+readonly)\s+string\s+(?<name>\w+)\s*=\s*" + OwnerKeyLiteral + @"\s*;", RegexOptions.Compiled);

    /// <summary>An <c>AssignRequest</c> built, or an assign through a writer (<c>x.AssignAsync(</c>).</summary>
    private static readonly Regex AssignShape = new(@"\bnew\s+(?:\w+\.)*AssignRequest\b|\.AssignAsync\s*\(", RegexOptions.Compiled);

    private static List<OwnerWrite> OwnerWrites(IReadOnlyList<string> lines)
    {
        var code = lines.Select(SourceScan.StripLineComment).ToList();
        var aliases = code.SelectMany(l => OwnerKeyConst.Matches(l).Select(m => m.Groups["name"].Value)).Distinct().ToList();
        var key = "(?:" + OwnerKeyLiteral + (aliases.Count > 0 ? @"|(?:\w+\.)*(?:" + string.Join("|", aliases.Select(Regex.Escape)) + @")\b" : "") + ")";
        var shapes = new[]
        {
            new Regex(@"\[\s*" + key + @"\s*\]\s*=(?![=>])"),
            new Regex(@"\.(?:Add|TryAdd|SetAttributeValue)\(\s*" + key + @"\s*,"),
            // A { key, value } element of a collection initializer: its brace opens the line or follows "{" or ",", so a
            // column array (new[] { "ownerid", ... }) and a set initializer do not match.
            new Regex(@"(?:^|[{,])\s*\{\s*" + key + @"\s*,"),
            AssignShape,
        };

        var members = MembersByLine(Bare(code));
        var writes = new List<OwnerWrite>();
        for (var i = 0; i < code.Count; i++)
        {
            if (!IsDocComment(code[i]) && shapes.Any(r => r.IsMatch(code[i])))
                writes.Add(new OwnerWrite(i + 1, members[i], code[i].Trim()));
        }

        return writes;
    }

    /// <summary>The lines with every string literal blanked except the owner key, so a literal never opens or closes a member.</summary>
    private static List<string> Bare(IReadOnlyList<string> code) =>
        code.Select(l => StringLiteral.Replace(l, m => OwnerKeyLiteralRegex.IsMatch(m.Value) ? m.Value : "\"\"")).ToList();

    private static readonly Regex AssignFirstArgument = new(@"\.AssignAsync\s*\(\s*(?<arg>""[^""]*""|[\w.]+)\s*,", RegexOptions.Compiled);

    /// <summary>
    /// The write is an assign through a writer whose table argument is <paramref name="table"/>: the literal, or a constant of the
    /// file holding it.
    /// </summary>
    private static bool AssignsOnly(string table, OwnerWrite write, IReadOnlyList<string> code)
    {
        if (AssignFirstArgument.Match(write.Text) is not { Success: true } assign)
            return false;

        var argument = assign.Groups["arg"].Value;
        if (argument.StartsWith('"'))
            return argument == $"\"{table}\"";

        var constant = new Regex(@"\bconst\s+string\s+" + Regex.Escape(argument.Split('.')[^1]) + @"\s*=\s*""" + Regex.Escape(table) + @"""\s*;");
        return code.Any(constant.IsMatch);
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

            private async Task PairWritesAsync(Guid team, CancellationToken ct)
            {
                var fields = new Dictionary<string, object>
                {
                    { "ownerid", new EntityReference("team", team) },
                };
            }

            private async Task RequestWritesAsync(Guid team, CancellationToken ct)
            {
                await _service.ExecuteAsync(new AssignRequest { Target = _target, Assignee = new EntityReference("team", team) }, ct);
            }

            private async Task ReadsAsync(CancellationToken ct)
            {
                var query = new QueryExpression("sprk_communication") { ColumnSet = new ColumnSet(OwnerColumn, "ownerid") };
                var same = row.GetAttributeValue<EntityReference>(OwnerColumn)?.Id == team;
            }

            private async Task ColumnsAsync(CancellationToken ct)
            {
                var row = await _dataverse.RetrieveAsync("sprk_communication", _id, new[] { "ownerid", "createdby" }, ct);
                var thread = await _resolver.ResolveAndAssignAsync(request, ct);
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

    private const string LookupTypeControl = """
        internal sealed class HoldsTheLookup(DirectMessageOwnerTeam lookup)
        {
            public object Lookup => lookup;
        }
        """;

    private const string IsDirectControl = """
        internal sealed class AsksIsDirect
        {
            public Task<bool?> AskAsync(Guid team, CancellationToken ct) =>
                DirectMessageParticipantShares.IsDirectOwnerTeamAsync(_entities, _team, team, _logger, ct);
        }
        """;

    private const string CannotBeControl = """
        internal sealed class AsksCannotBe
        {
            public Task<bool> AskAsync(Entity team, CancellationToken ct) => _lookup.CannotBeDirectOwnerTeamAsync(team, ct);
        }
        """;

    private const string RefusalOnlyControl = """
        /// <summary>Answers from <see cref="DirectMessageOwnerTeam"/>'s refusal codes only.</summary>
        internal sealed class RefusalOnly
        {
            public bool IsPartial(string code) => code == DirectMessageOwnerTeamRefusal.PartiallyConfigured;

            public void Route(Dictionary<string, object> fields, Guid team) { fields["ownerid"] = new EntityReference("team", team); }
        }
        """;

    private const string DecisionRecordControl = """
        internal sealed class DecisionRecords
        {
            private const string Table = "sprk_decisionrecord";
            private const string CommunicationTable = "sprk_communication";

            private Task<bool?> IsDirectAsync(Guid team) => DirectMessageParticipantShares.IsDirectOwnerTeamAsync(_e, _t, team, _l, default);

            private async Task ReownAsync(Guid id, Guid team)
            {
                await _writer.AssignAsync(Table, id, new EntityReference("team", team), CancellationToken.None);
            }

            private async Task ReownCommunicationAsync(Guid id, Guid team)
            {
                await _writer.AssignAsync(CommunicationTable, id, new EntityReference("team", team), CancellationToken.None);
            }
        }
        """;

    private const string UnitKeyControl = """
        internal sealed class ReadsTheUnitKey
        {
            public void Route(IConfiguration c, Dictionary<string, object> fields, Guid team)
            {
                var unit = c["DirectMessage:BusinessUnitName"];
                fields["ownerid"] = new EntityReference("team", team);
            }
        }
        """;

    private const string TeamKeyControl = """
        internal sealed class ReadsTheTeamKey
        {
            public string? Team(IConfiguration c) => c.GetValue<string>("DirectMessage:OwnerTeamName");
        }
        """;

    private const string OtherKeyControl = """
        internal sealed class ReadsAnotherKey
        {
            public void Route(IConfiguration c, Dictionary<string, object> fields, Guid team)
            {
                var name = c["SecureRecord:OwnerTeamName"];
                fields["ownerid"] = new EntityReference("team", team);
            }
        }
        """;

    private const string UnfileControl = """
        internal sealed class Unfile
        {
            private const string OwnerColumn = "ownerid";

            private async Task MoveAsync(string table, Guid id, Guid team)
            {
                var update = new Dictionary<string, object> { [OwnerColumn] = new EntityReference("team", team) };
            }

            private async Task ReadsAndMovesAsync(string table, Guid id, CancellationToken ct)
            {
                var direct = await _lookup.ResolveDirectMessageOwnerTeamAsync(ct);
                var update = new Dictionary<string, object> { [OwnerColumn] = new EntityReference("team", direct.TeamId!.Value) };
            }

            private async Task UnfileAsync(string table, Guid id, CancellationToken ct)
            {
                var direct = await _lookup.ResolveDirectMessageOwnerTeamAsync(ct);
                var update = new Dictionary<string, object> { [OwnerColumn] = new EntityReference("team", direct.TeamId!.Value) };
            }
        }
        """;
}
