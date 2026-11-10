using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 045b, guard 2 (G2, G3; owner decisions 2026-11-07): a GENERIC update path — one that
/// writes a payload it did not build column by column, so it can write any pair table's typed lookup or its ADR-024 pair — runs
/// the update planner (<c>OwnedChildWrite.KeepRegardingPairWholeAsync</c>, <c>KeepRegardingPairWholeAppOnlyAsync</c>, or
/// <c>KeepPairInStepAsync</c>, which calls the first) before its write, so the pair it leaves is whole and in step.
/// </summary>
/// <remarks>
/// <para><b>Signal.</b> A source scan of <c>src/server/**</c> (line comments stripped; <see cref="SourceScan"/>). A generic update
/// path is recognised by the one reading every such path makes of an arbitrary payload: the re-file core
/// (<c>OwnedChildWrite.RefileAsync(</c>) or the generic payload's parent reading (<c>RecordReparent.ParentChangesWithClearsIn(</c>).</para>
/// <list type="number">
/// <item><b>Rule 1, discovery.</b> A file that makes that reading calls the planner. A new generic update path is flagged until
/// it does.</item>
/// <item><b>Rule 2, entry members.</b> Each known entry point (<see cref="EntryPoints"/>) exists and calls the planner in its own
/// member, so the call cannot drift into a helper the write does not run through.</item>
/// </list>
/// <para>Not seen: a generic update that neither re-files nor reads parents from its payload (none today: every generic writer
/// re-files a moved child); a planner call on a branch the write does not take; a write that rebuilds the payload after the
/// planner ran. Enforced by: this guard (ArchTests in CI and the Stop hook). No baseline: every known path calls the planner.</para>
/// <para><b>Controls.</b> <see cref="Control_AGenericUpdatePathWithoutThePlanner_IsFound"/> (must fire) and
/// <see cref="Control_APathThatCallsThePlanner_OrIsNoGenericUpdate_IsNotFound"/> (must not fire) run the same detector over
/// synthetic source.</para>
/// </remarks>
public class GenericUpdatePathsPreparePairGuardTests
{
    /// <summary>The generic update entry points (design §2.2 item 3): file → the member that must call the planner itself.</summary>
    internal static readonly (string File, string Member)[] EntryPoints =
    [
        ("src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs", "UpdateAsync"),
        ("src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseUpdateRecordHandler.cs", "ExecuteChatAsync"),
        ("src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs", "UpdateAsync"),
        ("src/server/api/Sprk.Bff.Api/Services/Dataverse/DataverseUpdateHandler.cs", "UpdateAsync"),
        ("src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs", "ApplyMappingsToChildRecordsAsync"),
    ];

    private static readonly Regex GenericUpdateReading = new(
        @"\bOwnedChildWrite\s*\.\s*RefileAsync\s*\(|\bParentChangesWithClearsIn\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlannerCall = new(
        @"\b(?:KeepRegardingPairWholeAsync|KeepRegardingPairWholeAppOnlyAsync|KeepPairInStepAsync)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A type member, as guard 1 reads one: an access modifier, so a local function stays in its member.</summary>
    private static readonly Regex MemberDeclaration = new(
        @"^[ \t]*(?:(?:static|async|override|virtual|sealed|new|readonly|partial|extern|unsafe)\s+)*(?:public|private|internal|protected)\s+[^\n;=]*?\b(\w+)\s*(?:<[^\n>]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static string Code(string source) =>
        SourceScan.CodeText(source.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'));

    /// <summary>Rule 1 over one file's source: a problem when it reads a generic payload and never calls the planner; and whether
    /// it reads one at all.</summary>
    internal static (string? Problem, bool IsGenericUpdatePath) ScanFile(string source)
    {
        var code = Code(source);
        var reading = GenericUpdateReading.Match(code);
        if (!reading.Success)
            return (null, false);
        return PlannerCall.IsMatch(code)
            ? (null, true)
            : ($"line {SourceScan.LineOf(code, reading.Index)}: a generic update ('{reading.Value.Trim()}') with no call of the "
               + "regarding pair planner (KeepRegardingPairWholeAsync / KeepRegardingPairWholeAppOnlyAsync / KeepPairInStepAsync) in the file", true);
    }

    /// <summary>Rule 2 over one file's source: a problem when <paramref name="member"/> is missing, or does not call the planner
    /// in its own span (one access-modified declaration to the next; every overload with that name counts).</summary>
    internal static string? ScanMember(string source, string member)
    {
        var code = Code(source);
        var starts = MemberDeclaration.Matches(code).Select(m => (m.Index, Name: m.Groups[1].Value)).ToList();
        var spans = starts.Select((s, i) => (s.Name, Start: s.Index, End: i + 1 < starts.Count ? starts[i + 1].Index : code.Length))
            .Where(s => s.Name == member)
            .ToList();
        if (spans.Count == 0)
            return $"{member} was not found; the guard lost its entry point (renamed? register the new one)";
        return spans.Any(s => PlannerCall.IsMatch(code[s.Start..s.End]))
            ? null
            : $"{member} does not call the regarding pair planner in its own member";
    }

    [Fact(DisplayName = "045b guard 2: every generic update path runs the regarding pair planner before its write")]
    public void EveryGenericUpdatePath_RunsTheRegardingPairPlanner()
    {
        var problems = new List<string>();
        var paths = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var (problem, isPath) = ScanFile(File.ReadAllText(file));
            if (problem is not null)
                problems.Add($"{relative}: {problem}");
            if (isPath)
                paths.Add(relative);
        }

        foreach (var (file, member) in EntryPoints)
        {
            var path = Path.Combine(SourceScan.RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                problems.Add($"{file}: not found; the guard lost its entry point");
                continue;
            }

            if (ScanMember(File.ReadAllText(path), member) is { } problem)
                problems.Add($"{file}: {problem}");
        }

        // Non-vacuity: rule 1 sees today's generic update paths by their payload reading (the browser re-file core, the chat
        // tool's re-file, the playbook core, the output mapping, the push).
        foreach (var expected in EntryPoints.Select(e => e.File))
        {
            if (!paths.Contains(expected))
                problems.Add($"{expected}: expected to be seen as a generic update path; the scan lost its signal.");
        }

        Assert.True(problems.Count == 0,
            "A generic update path keeps the ADR-024 regarding pair whole and in step through the update planner (task 045b, "
            + "G2/G3):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "045b guard 2 control (must fire): a generic update path without the planner is found")]
    public void Control_AGenericUpdatePathWithoutThePlanner_IsFound()
    {
        Assert.NotNull(ScanFile("""
            internal static async Task<IResult> UpdateAsync(string entity, Guid id, JsonElement body)
            {
                var outcome = await OwnedChildWrite.RefileAsync(user, ownership, entity, id, set, primary, item, oid, ct);
            }
            """).Problem);
        Assert.NotNull(ScanFile("""
            public async Task UpdateAsync(string table, Guid id, Dictionary<string, object?> fields)
            {
                var changes = RecordReparent.ParentChangesWithClearsIn(table, fields);
                // await OwnedChildWrite.KeepRegardingPairWholeAppOnlyAsync(entities, table, id, fields, "x", logger, ct);
            }
            """).Problem);

        // Rule 2: the planner called from another member is not the entry point's call; a missing entry point is found.
        Assert.NotNull(ScanMember("""
            public async Task UpdateAsync(string table, Guid id, Dictionary<string, object?> fields) { await WriteAsync(fields); }
            private async Task Helper() { await OwnedChildWrite.KeepRegardingPairWholeAppOnlyAsync(e, t, i, f, "x", l, c); }
            """, "UpdateAsync"));
        Assert.NotNull(ScanMember("""public async Task RenamedAsync() { await OwnedChildWrite.KeepRegardingPairWholeAsync(u, t, s, i, m, l, c); }""",
            "UpdateAsync"));
    }

    [Fact(DisplayName = "045b guard 2 control (must not fire): a path that calls the planner, or that is no generic update, is not flagged")]
    public void Control_APathThatCallsThePlanner_OrIsNoGenericUpdate_IsNotFound()
    {
        Assert.Null(ScanFile("""
            internal static async Task<IResult> UpdateAsync(string entity, Guid id, JsonElement body)
            {
                var prepared = await OwnedChildWrite.KeepRegardingPairWholeAsync(user, entity, set, id, item, logger, ct);
                var outcome = await OwnedChildWrite.RefileAsync(user, ownership, entity, id, set, primary, prepared.Item, oid, ct);
            }
            """).Problem);
        Assert.Null(ScanFile("""
            public async Task UpdateAsync(string table, Guid id, Dictionary<string, object?> fields)
            {
                fields = await OwnedChildWrite.KeepRegardingPairWholeAppOnlyAsync(entities, table, id, fields, "x", logger, ct);
                var changes = RecordReparent.ParentChangesWithClearsIn(table, fields);
            }
            """).Problem);

        // No generic payload reading: a writer that sets its own columns, or one that only names the methods in a comment.
        var (problem, isPath) = ScanFile("""
            public async Task StampAsync(Guid id) { await _service.UpdateExistingRecordFieldsAsync("sprk_todo", id, new() { ["sprk_x"] = 1 }); }
            // OwnedChildWrite.RefileAsync( is called by the routes, not here
            """);
        Assert.Null(problem);
        Assert.False(isPath);

        Assert.Null(ScanMember("""
            public async Task UpdateAsync(string table, Guid id, Dictionary<string, object?> fields)
            {
                fields = await OwnedChildWrite.KeepRegardingPairWholeAppOnlyAsync(entities, table, id, fields, "x", logger, ct);
            }
            """, "UpdateAsync"));
    }
}
