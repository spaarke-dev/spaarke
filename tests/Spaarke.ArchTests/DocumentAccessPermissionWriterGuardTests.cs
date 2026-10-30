using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 189 (D7 T7a, #2126) — a document's <c>sprk_accesspermission</c> is max(own narrowing,
/// inherited), and under D7 its Restricted hides it from partners (task 188). Only the writers that decide it through
/// <c>InheritedAccessPermission.DecideDocument</c>, with its record in the same update, may write it. A writer that stores a
/// bare inherited value on a document lowers the user's own narrowing and shows the document to partners again.
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> A source scan of <c>src/server/**</c> for every WRITE of the column. That means an indexer
/// assignment, an <c>Add</c>/<c>TryAdd</c>/<c>SetAttributeValue</c> with the column as its key, a dictionary-initializer entry
/// (on one line, or on its own line in a multi-line initializer), a JSON writer call, or a member assignment (an anonymous payload). The column is matched by its literal and by the
/// constants that hold it (<see cref="KnownAliases"/>; a new constant fails until it is listed). Each write is attributed to
/// its enclosing member (a method or a local function), and every member that writes must be listed:</para>
/// <list type="bullet">
/// <item><see cref="DocumentWriters"/>: the approved document writers.</item>
/// <item><see cref="NonDocumentWriters"/>: members whose write can never reach a document, each with the reason (code review
/// checks the reason).</item>
/// <item><see cref="KnownViolations"/>: the ratchet baseline, for members that could write a document's value without the
/// record. It is empty. A new unlisted writer fails, and an entry that no longer writes must be removed.</item>
/// </list>
/// <para><b>The stamp path is fenced, not baselined.</b> The scan sees write sites, not callers, so a new document caller of
/// <c>CoreAncestorResolver.StampAsync</c> would not show up here. What stops it is the fence: the helper the stamp-path
/// members get their value from (<c>InheritedAccessPermissionAsync</c>, and <c>ResolveInheritedAccessPermissionAsync</c>)
/// returns none for a document (<c>InheritedAccessPermission.RecordsOwnNarrowing</c>). It is pinned by
/// <c>InheritedAccessPermissionStampTests</c>.</para>
/// <para><b>Why text, not IL.</b> The table a write targets is a runtime value, and IL cannot tell a read of the column's
/// constant from a write. The key at the write site is in the text.</para>
/// <para><b>Limits (accepted, task 189).</b> The scan works per member, as the #2091 guard works per type. A key held in a
/// local variable, a payload a caller hands in (the generic update writers, a playbook) and a write outside
/// <c>src/server</c> are not seen. For the app-only generic writers that take a caller's payload (a playbook's Update Record
/// node and output mapping), a run-time refusal covers it instead: <c>InheritedAccessPermission.RefuseAppOnlyWrite</c>
/// (fix round C, K5), pinned by <c>DocumentAccessPermissionAppOnlyWriterTests</c>.
/// The member detector is crude (a declaration line, brace depth with strings blanked); the controls below pin what it must
/// and must not see.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over inline sources: each write
/// shape is found and attributed to the right member (including the outer method after a local function). A read, a
/// comparison, a column list, a set (on one line or several), a comment, another column's constant, a log message and a one-argument <c>Add</c> are
/// not found. A new constant holding the column is reported.</para>
/// </remarks>
public class DocumentAccessPermissionWriterGuardTests
{
    private const string Reconciler = "src/server/api/Sprk.Bff.Api/Services/Access/ChildAccessPermissionReconciler.cs";
    private const string Resolver = "src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs";

    /// <summary>A member that writes the column: its file (repo-relative, forward slashes), its name and why it may.</summary>
    private sealed record Writer(string File, string Member, string Reason);

    private static readonly IReadOnlyList<Writer> DocumentWriters = new[]
    {
        new Writer(Reconciler, "DecideDocumentAsync",
            "Task 189: the L4 pass. It writes a document's value only from InheritedAccessPermission.DecideDocument, with its " +
            "record in the same update, after a re-read; or, where the record cannot be known (unreadable, or no record " +
            "column), from DecideDocumentRaiseOnly, which only raises the value and never lowers it."),
        new Writer(Resolver, "RefreshDocumentAccessPermissionAsync",
            "Task 189: the child-records create refresh (L1). Same decision, one update."),
        new Writer("src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs", "StageDocumentCommunicationFields",
            "spaarkeai-word-add-in-r1 task 130 (D7 T8): an Office email-derived document's own Restricted, set only by " +
            "OfficeDocumentPersistence.ApplyEmailCommunicationOutcome together with " +
            "AccessInheritance.OwnNarrowingUntilEmailIdentified(Restricted) (provisional, owner round 122) " +
            "(CreateDocumentRequest.AccessInheritanceRecord). The stager writes both in the one create and refuses a value " +
            "without its record (DocumentCommunicationFieldsStagingTests). ebilling-r1 task 095: the same method's " +
            "submissionNarrowing branch sets Restricted with the NON-provisional AccessInheritance.OwnNarrowing(Restricted) and " +
            "the submission marker, for the from-email submission's .eml only (SubmissionMarkerWriterGuardTests)."),
    };

    private static readonly IReadOnlyList<Writer> NonDocumentWriters = new[]
    {
        new Writer(Reconciler, "RunAsync",
            "Task 173: To Do, Event and Communication. A document always goes to DecideDocumentAsync, including where " +
            "documents lack the record column (TablesWithoutColumn): it is then raised only, never lowered."),
        new Writer(Resolver, "RefreshInheritedAccessPermissionAsync",
            "Task 173: To Do, Event and Communication. A document returns through RefreshDocumentAccessPermissionAsync before " +
            "this write (InheritedAccessPermission.RecordsOwnNarrowing)."),
        new Writer("src/server/api/Sprk.Bff.Api/Services/Access/SecureRootInheritance.Cascade.cs", "WritePermissionAsync",
            "Task 175: work assignments and projects only (FollowParentsAsync throws for any other table)."),
        new Writer("src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs", "ApplyCoreAncestorStampsAsync",
            "Task 173: the communication being associated (CommunicationEntity)."),
        new Writer("src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/EmailDraftToolHandler.cs", "BuildCommunicationItem",
            "Task 173: the payload of a communication draft."),
        new Writer("src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs", "AddRegardingWriteSet",
            "Task 173: the payload of an event create."),
        new Writer(Resolver, "StampAsync",
            "Task 189's fence (both overloads): the value comes from InheritedAccessPermissionAsync, which returns none for " +
            "a document (RecordsOwnNarrowing), so the stamp path never writes one, whoever calls it. Pinned by " +
            "InheritedAccessPermissionStampTests."),
        new Writer(Resolver, "ApplyInheritedAccessPermissionAsync",
            "Task 189's fence: the same helper (its only caller today is the Office To Do create)."),
    };

    /// <summary>The ratchet baseline: members that could write a document's value without its record. Empty since task
    /// 189 fenced the stamp path; a listed entry must still write, or it is stale.</summary>
    private static readonly IReadOnlyList<Writer> KnownViolations = Array.Empty<Writer>();

    /// <summary>The constants that hold the column, and the key form each is written with (the detector matches them).</summary>
    private static readonly IReadOnlyList<(string File, string Name)> KnownAliases = new[]
    {
        ("src/server/api/Sprk.Bff.Api/Services/Dataverse/ParentLineage.cs", "Column"), // InheritedAccessPermission.Column
        ("src/server/api/Sprk.Bff.Api/Services/Access/SecureChildShareSynchronizer.cs", "AccessPermissionColumn"),
        ("src/server/api/Sprk.Bff.Api/Services/Access/SecureRootInheritance.cs", "AccessPermissionColumn"),
        ("src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs", "AccessPermissionColumn"), // task 130
    };

    [Fact(DisplayName = "Task 189: only the approved writers write a document's sprk_accesspermission (every writer is listed)")]
    public void OnlyTheApprovedWritersWriteADocumentsAccessPermission()
    {
        var sites = SourceScan.ServerSourceFiles()
            .SelectMany(f => WritesIn(Normalize(SourceScan.Relative(f)), File.ReadAllLines(f)))
            .ToList();

        // Non-vacuity: the approved writers are seen writing (every other listed member is held by the STALE check).
        foreach (var writer in DocumentWriters.Concat(KnownViolations))
        {
            Assert.True(sites.Any(s => s.File == writer.File && s.Member == writer.Member),
                $"{writer.File} {writer.Member} should be seen writing sprk_accesspermission: the scan lost its signal.");
        }

        var listed = DocumentWriters.Concat(NonDocumentWriters).Concat(KnownViolations).ToList();
        var problems = new List<string>();
        foreach (var site in sites.OrderBy(s => s.File, StringComparer.Ordinal).ThenBy(s => s.Line))
        {
            if (!listed.Any(w => w.File == site.File && w.Member == site.Member))
                problems.Add($"NEW writer of sprk_accesspermission: {site.File}:{site.Line} in {site.Member}: {site.Statement}");
        }

        foreach (var writer in NonDocumentWriters.Concat(KnownViolations))
        {
            if (!sites.Any(s => s.File == writer.File && s.Member == writer.Member))
                problems.Add($"STALE entry {writer.File} {writer.Member}: it no longer writes sprk_accesspermission. Remove it (the ratchet).");
        }

        Assert.True(problems.Count == 0,
            "A document's sprk_accesspermission is max(own narrowing, inherited) and is written only through " +
            "InheritedAccessPermission.DecideDocument, with its record in the same update (task 189). A new writer of the " +
            "column decides a document through it, or is listed as a non-document writer with the reason it can never " +
            "reach a document:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 189: every constant that holds sprk_accesspermission is one the writer scan matches")]
    public void EveryConstantHoldingTheColumnIsMatchedByTheScan()
    {
        var found = SourceScan.ServerSourceFiles()
            .SelectMany(f => AliasesIn(File.ReadAllLines(f)).Select(name => (File: Normalize(SourceScan.Relative(f)), Name: name)))
            .ToList();

        Assert.NotEmpty(found);
        var problems = found.Except(KnownAliases).Select(a => $"NEW constant {a.Name} in {a.File}: add it to the scan's key forms")
            .Concat(KnownAliases.Except(found).Select(a => $"STALE alias {a.Name} in {a.File}: remove it"))
            // #2177 UAC pass 1 F2: a listed alias that Key does not match hides every write through it from the scan.
            // InheritedAccessPermission's own "Column" is matched qualified (and bare, in its own file: BareColumnWrite).
            .Concat(KnownAliases.Where(a => !KeyMatches(a.Name == "Column" ? "InheritedAccessPermission.Column" : a.Name))
                .Select(a => $"UNSEEN alias {a.Name} in {a.File}: Key does not match it, so writes through it are not found"))
            .ToList();
        Assert.True(problems.Count == 0,
            "A constant holding sprk_accesspermission is a key the writer scan must match, or a write through it goes unseen:\n" +
            string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 189: negative control - each write shape is found in its member; reads, lists, comments and other columns are not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var sites = WritesIn("control.cs", ControlSource.Split('\n')).ToList();

        Assert.Equal(
            new[]
            {
                ("AddStyle", 0), ("AliasWrite", 0), ("AnonymousPayload", 0), ("ExpressionBodied", 0), ("GenericTuple", 0),
                ("Initializer", 0), ("JsonWriter", 0), ("LocalFunction", 0), ("MultiLineInitializer", 0),
                ("MultiLineInitializer", 1), ("Outer", 0), ("Outer", 1), ("QualifiedAlias", 0), ("Tuple", 0),
            },
            sites.GroupBy(s => s.Member).SelectMany(g => g.Select((_, i) => (g.Key, i)))
                .OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.i).ToArray());

        Assert.DoesNotContain(sites, s => s.Member is "Reads" or "NotWrites");
        Assert.Equal(new[] { "AccessPermissionKey" }, AliasesIn(ControlSource.Split('\n')).ToArray());
    }

    [Fact(DisplayName = "Task 189 guard control: an alias Key does not match is reported; the listed forms are matched")]
    public void KeyMatches_MustFireAndMustNotFire()
    {
        Assert.False(KeyMatches("AccessPermissionKey"), "a constant Key misses must be reported UNSEEN (must fire)");
        Assert.False(KeyMatches("AccessPermissionColumnName"), "a longer name is not the column constant");
        Assert.True(KeyMatches("AccessPermissionColumn"));
        Assert.True(KeyMatches("DocumentAccessPermissionColumn"), "a prefixed name is matched (must not fire)");
        Assert.True(KeyMatches("SecureRootInheritance.AccessPermissionColumn"));
    }

    /// <summary>True when an indexer write through <paramref name="name"/> is one the scan finds.</summary>
    private static bool KeyMatches(string name) => KeyedWrites[0].IsMatch($"update[{name}] = value;");

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Site(string File, string Member, int Line, string Statement);

    /// <summary>The column as a key: its literal, or a constant that holds it (<see cref="KnownAliases"/>).</summary>
    private const string Key =
        @"(?:""sprk_accesspermission""|(?:\w+\.)*InheritedAccessPermission\.Column|(?:\w+\.)*\w*AccessPermissionColumn\b)";

    private static readonly Regex[] KeyedWrites =
    {
        new(@"\[\s*" + Key + @"\s*\]\s*=(?![=>])", RegexOptions.Compiled), // fields[KEY] = …
        new(@"\.(?:Add|TryAdd|SetAttributeValue)\(\s*" + Key + @"\s*,", RegexOptions.Compiled), // fields.Add(KEY, …)
        // new Dictionary { { KEY, … } }, and the usual multi-line initializer, whose entry starts its line
        new(@"(?:^|[{,])\s*\{\s*" + Key + @"\s*,", RegexOptions.Compiled),
        new(@"\.Write\w*\(\s*" + Key, RegexOptions.Compiled), // writer.WriteNumber(KEY, …)
    };

    /// <summary>A member assignment (an anonymous payload, an early-bound property), matched with strings blanked.</summary>
    private static readonly Regex MemberWrite =
        new(@"(?<![\w""])sprk_accesspermission\s*=(?![=>])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The bare constant inside the file that declares it (InheritedAccessPermission's own code).</summary>
    private static readonly Regex BareColumnWrite = new(@"\[\s*Column\s*\]\s*=(?![=>])", RegexOptions.Compiled);

    private static readonly Regex Alias = new(
        @"\b(?:const|static\s+readonly)\s+string\s+(\w+)\s*=\s*""sprk_accesspermission""\s*;", RegexOptions.Compiled);

    /// <summary>A method, constructor or local function declaration: modifiers, a type, a name, an open parenthesis.</summary>
    private static readonly Regex Declaration = new(
        @"^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|abstract|extern|unsafe|new|partial|readonly)\s+)*" +
        @"(?!(?:return|await|new|throw|yield|else|case|in|is|as|var|using|select|where|from|let|goto|when)\b)" +
        @"(?:[A-Za-z_][\w.]*(?:<[^;{}]*?>)?|\([^;{}]*?\))(?:\[\])?\??\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^;{}()]*>)?\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new(@"@""(?:[^""]|"""")*""|\$?""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)'", RegexOptions.Compiled);

    private static string Normalize(string relative) => relative.Replace('\\', '/');

    private static IEnumerable<Site> WritesIn(string file, IReadOnlyList<string> lines)
    {
        var code = lines.Select(SourceScan.StripLineComment).ToList();
        var bare = code.Select(l => StringLiteral.Replace(l, "\"\"")).ToList();
        var members = MembersByLine(bare);
        var declaresColumn = lines.Any(l => Regex.IsMatch(l, @"\bconst\s+string\s+Column\s*=\s*""sprk_accesspermission"""));

        for (var i = 0; i < code.Count; i++)
        {
            var write = KeyedWrites.Any(r => r.IsMatch(code[i])) || MemberWrite.IsMatch(bare[i])
                || (declaresColumn && BareColumnWrite.IsMatch(bare[i]));
            if (write)
                yield return new Site(file, members[i], i + 1, code[i].Trim());
        }
    }

    private static IEnumerable<string> AliasesIn(IReadOnlyList<string> lines) =>
        lines.Select(SourceScan.StripLineComment).SelectMany(l => Alias.Matches(l).Select(m => m.Groups[1].Value));

    /// <summary>
    /// The member each line belongs to: a declaration pushes its name; its body ends when the brace depth returns to where it
    /// was declared (or at its <c>;</c>, for an expression-bodied or abstract member). <paramref name="bare"/> has comments
    /// removed and strings blanked, so their braces do not count.
    /// </summary>
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
            private const string AccessPermissionKey = "sprk_accesspermission";

            internal void Outer(Dictionary<string, object> fields, Entity row)
            {
                fields["sprk_accesspermission"] = new OptionSetValue(1);
                async Task LocalFunction(Entity child)
                {
                    child[InheritedAccessPermission.Column] = new OptionSetValue(2);
                    await Task.CompletedTask;
                }
                var note = "} closes nothing";
                row[InheritedAccessPermission.Column] = new OptionSetValue(3);
            }

            internal void AddStyle(Dictionary<string, object> fields) =>
                fields.Add(Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.Column, new OptionSetValue(1));

            internal Dictionary<string, object> Initializer() => new Dictionary<string, object> { { "sprk_accesspermission", 1 } };

            internal Dictionary<string, object> MultiLineInitializer() => new Dictionary<string, object>
            {
                { "sprk_name", "x" },
                { "sprk_accesspermission", 1 },
                { InheritedAccessPermission.Column, 2 },
            };

            internal void JsonWriter(Utf8JsonWriter writer, int level)
            {
                writer.WriteNumber("sprk_accesspermission", level);
            }

            internal object AnonymousPayload() => new { sprk_name = "x", sprk_accesspermission = 100000002 };

            internal (bool Done, int Level) Tuple(Entity e)
            {
                e["sprk_accesspermission"] = new OptionSetValue(1);
                return (true, 1);
            }

            internal async Task<(bool Done, int Level)> GenericTuple(Entity e)
            {
                e["sprk_accesspermission"] = new OptionSetValue(1);
                return await Task.FromResult((true, 1));
            }

            internal void AliasWrite(Dictionary<string, object> update, int to)
            {
                update[AccessPermissionColumn] = new OptionSetValue(to);
            }

            internal void QualifiedAlias(Dictionary<string, object> update) { update[SecureRootInheritance.AccessPermissionColumn] = 1; }

            internal int ExpressionBodied(Entity e) => (e[InheritedAccessPermission.Column] = new OptionSetValue(1)) is null ? 0 : 1;

            internal void Reads(Entity row, Dictionary<string, object> fields, List<string> columns)
            {
                var stored = row["sprk_accesspermission"];
                if (fields[InheritedAccessPermission.Column] == null) { }
                var set = new ColumnSet("sprk_name", "sprk_accesspermission");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_accesspermission", "sprk_issecure" };
                var multiLine = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "sprk_accesspermission",
                    InheritedAccessPermission.Column,
                };
                var reasons = new Dictionary<string, string>
                {
                    { "sprk_accesspermissionreason", "x" },
                };
                columns.Add("sprk_accesspermission");
                // fields["sprk_accesspermission"] = new OptionSetValue(1);
                _logger.LogInformation("{Table}: sprk_accesspermission = {Value} {", table, value);
            }

            internal void NotWrites(Dictionary<string, object> fields, AccessInheritance record)
            {
                fields[AccessInheritance.Column] = record.Serialize();
                fields["sprk_accesspermissionreason"] = "x";
            }
        }
        """;
}
