using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22, Q5: a private message "always stays 1:1 - no filing"): the census
/// of the BFF's generic writers. Every method that asks <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> (the column rule every
/// generic writer asks, ParentLineage.cs) is a generic writer, or a predicate one of them asks. Each such method either also asks
/// <c>PrivateMessageWriteGuard.RefuseUpdateAsync</c> (the row check: an update of an existing <c>sprk_communication</c> that is a
/// private message is refused before it is written), or is listed below as create-only, as never writing a <c>sprk_communication</c>,
/// or as a predicate that writes nothing and whose writer asks the guard, with a written reason and an ADR citation.
/// </summary>
/// <remarks>
/// <para><b>Why a census.</b> The column rule reads no row, so it cannot know whether the row is a private message; the guard reads
/// it. A new generic update path copied from an existing one asks the column rule and, without this test, can forget the row check:
/// a private message would then be filed through it. Here that is a build failure.</para>
/// <para><b>How the scan works.</b> A source scan of <c>src/server/**</c> (<see cref="SourceScan"/>), comments stripped. Each
/// <c>RefuseAppOnlyWrite(</c> call is attributed to the member it is written in: the nearest preceding member declaration at four or
/// eight spaces (a lambda or local function counts as its member). The member asks the guard when its text, up to the next member
/// declaration, contains <c>PrivateMessageWriteGuard.RefuseUpdateAsync(</c>. The definition itself is not a caller.</para>
/// <para><b>Non-vacuity.</b> <see cref="Guarded"/> names the generic update writers that must be found and must ask the guard, and
/// every <see cref="Listed"/> entry must still be found as a caller: a stale entry, a lost signal or a removed guard call fails.
/// <b>Controls:</b> <see cref="Detector_MustFireOnAnUnguardedUpdateCaller_AndMustNotFireOnTheSanctionedShapes"/> runs the detector over
/// seeded source.</para>
/// <para><b>Maintenance procedure (when this test fails).</b></para>
/// <list type="number">
/// <item>A new method asks <c>RefuseAppOnlyWrite</c> and not the guard: if it sends an UPDATE of an existing row whose table can be
/// <c>sprk_communication</c>, call <c>PrivateMessageWriteGuard.RefuseUpdateAsync</c> in the same method before the write and render
/// its refusal (409 <c>sdap.communication.private.not_fileable</c>, 503 <c>sdap.communication.private.check_unavailable</c>), then
/// add the method to <see cref="Guarded"/>. Only if it is create-only, never writes a <c>sprk_communication</c>, or is a predicate
/// that writes nothing and whose writer asks the guard, add it to <see cref="Listed"/> with the reason (where the claim can be
/// checked) and the ADR. Do not list a method to make the test pass.</item>
/// <item>A listed or guarded entry is no longer found: the method was renamed, moved or no longer asks the column rule. Update the
/// entry to the new name or file; delete it only if the method no longer writes.</item>
/// <item>A guarded method no longer asks the guard: put the call back; the behaviour tests
/// (<c>PrivateMessageGenericUpdateGuardTests</c>, <c>SecureChildOwnershipAiToolTests.PrivateMessageFilingGuard</c>) say why.</item>
/// </list>
/// <para>Enforced by: this census (ArchTests in CI and the Stop hook) and those behaviour tests. Create-side: the private-context
/// references are BFF-owned columns (<c>InheritedAccessPermission.BffOwnedCommunicationColumns</c>), which every generic writer
/// refuses, so a generic create cannot make a message private.</para>
/// </remarks>
public class PrivateMessageWriteGuardCensusTests
{
    private const string Guard = "PrivateMessageWriteGuard.RefuseUpdateAsync(";
    private const string ColumnRule = "RefuseAppOnlyWrite";

    private const string FieldMappingEndpoints = "src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs";
    private const string RecordCreationService = "src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.cs";
    private const string RecordCreationMapping = "src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.Mapping.cs";
    private const string RecordCreationRepair = "src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.Repair.cs";

    /// <summary>A member of a source file: the repository-relative path (forward slashes) and the member name.</summary>
    internal sealed record Member(string File, string Method)
    {
        public override string ToString() => $"{File} {Method}";
    }

    /// <summary>A <c>RefuseAppOnlyWrite</c> call: its member, the line, and whether the member asks the guard.</summary>
    internal sealed record Caller(Member Member, int Line, bool AsksGuard);

    /// <summary>
    /// A census entry for a caller that need not ask the guard itself. <paramref name="CoveredBy"/>, when set, is the writer that
    /// sends what the predicate decides, and it must ask the guard.
    /// </summary>
    internal sealed record Listed(Member Member, string Reason, string Adr, Member? CoveredBy = null);

    /// <summary>The generic update writers: each asks the column rule and the guard, in the same method.</summary>
    private static readonly IReadOnlyList<Member> Guarded = new[]
    {
        // The field-mapping push: PATCHes each existing child row.
        new Member(FieldMappingEndpoints, "ApplyMappingsToChildRecordsAsync"),
        // The playbook UpdateRecord node.
        new Member("src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs", "UpdateAsync"),
        // The output-mapping / document-profile update handler.
        new Member("src/server/api/Sprk.Bff.Api/Services/Dataverse/DataverseUpdateHandler.cs", "UpdateAsync"),
        // Repair: PATCHes an existing row of a repaired table, and sprk_communication is one (RecordTableRules registry).
        new Member(RecordCreationRepair, "RepairAsync"),
    };

    /// <summary>The callers that need not ask the guard, each with its reason and ADR.</summary>
    private static readonly IReadOnlyList<Listed> ListedCallers = new[]
    {
        new Listed(new Member(RecordCreationService, "DecideAsync"),
            "Create-only: called only from CreateAsync, it decides the columns of a NEW row, which has no filing yet. A new message "
            + "cannot be made private by a generic create: the private-context references are BFF-owned "
            + "(InheritedAccessPermission.BffOwnedCommunicationColumns), refused by the column rule this method asks.",
            "ADR-002 (one server-side owner per invariant: the create service owns a new row's columns); ADR-045 (the communication's "
            + "participant and context columns are the BFF's)"),
        new Listed(new Member(RecordCreationMapping, "DropAppOnlyRefusedRules"),
            "Create-only: reached through ReadMappingInputsAsync from DecideAsync and MapFieldsAsync, both inside the create decision; "
            + "it drops mapping rules from the columns of a NEW row and sends nothing.",
            "ADR-002 (the create service is the one owner of a new row's mapped columns)"),
        new Listed(new Member(RecordCreationMapping, "IsCopyInvariantColumn"),
            "A create-only predicate that writes nothing: asked through MayCopyWithoutCallerChecks by CheckMergedPayloadAsync, step 5 "
            + "of CreateAsync, to keep the caller's checks on a BFF-owned column of a NEW row (task 064).",
            "ADR-002 (the create service is the one owner of a new row's mapped columns)"),
        new Listed(new Member("src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileOutputMapper.cs", "BuildFields"),
            "Not a communication: it asks the rule for ExternalDocumentNarrowing.DocumentTable (sprk_document) only, and builds "
            + "document profile fields.",
            "ADR-013 (AI output is mapped onto the document by the BFF); ADR-002"),
        new Listed(new Member("src/server/api/Sprk.Bff.Api/Api/FieldMappings/PushRuleSelection.cs", "TypedWriterTargetRefusal"),
            "A predicate that writes nothing: the push's rule selection (PushRuleSelection.Select) asks it per rule to leave out a "
            + "rule whose target only its typed writers write (DI-053). The push's writer asks the guard per child row.",
            "ADR-002 (one server-side owner per invariant: a typed-writer column is never a mapping target)",
            CoveredBy: new Member(FieldMappingEndpoints, "ApplyMappingsToChildRecordsAsync")),
        new Listed(new Member(FieldMappingEndpoints, "PushMayWrite"),
            "A predicate that writes nothing: the push preview (FieldMappingPushFamily Plan / PreviewAsync) lists what the push may "
            + "write. The push's writer asks the guard per child row.",
            "ADR-002 (previews write nothing; the server-side writer owns the rule)",
            CoveredBy: new Member(FieldMappingEndpoints, "ApplyMappingsToChildRecordsAsync")),
        new Listed(new Member(RecordCreationRepair, "DocumentServerOwnedRefusal"),
            "A predicate that writes nothing: Repair's mapping asks it per rule to leave a column out of the patch; RepairAsync, which "
            + "writes the patch, asks the guard on it.",
            "ADR-002 (Repair is the one server-side owner of the repaired columns)",
            CoveredBy: new Member(RecordCreationRepair, "RepairAsync")),
    };

    [Fact(DisplayName = "056 (Q5): every generic writer that asks the app-only column rule also asks the private-message write guard, or is listed")]
    public void EveryGenericUpdateWriter_AsksThePrivateMessageWriteGuard_OrIsListedCreateOnlyOrNotACommunication()
    {
        var members = new Dictionary<Member, bool>();
        var callers = new List<Caller>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            callers.AddRange(FindCallers(relative, lines));
            foreach (var (member, asksGuard) in MemberGuards(relative, lines))
                members[member] = asksGuard;
        }

        var problems = Violations(callers, ListedCallers, members).ToList();

        foreach (var expected in Guarded)
        {
            if (!callers.Any(c => c.Member == expected && c.AsksGuard))
                problems.Add($"{expected}: expected to ask {ColumnRule} and {Guard} in the same method; the scan lost its signal, the method moved, or the guard call was removed.");
        }

        foreach (var listed in ListedCallers)
        {
            if (!callers.Any(c => c.Member == listed.Member))
                problems.Add($"{listed.Member}: listed, but no longer found asking {ColumnRule}. Update or delete the entry (maintenance step 2).");
        }

        Assert.True(problems.Count == 0,
            "A private message is never filed or moved to another thread, by any writer (fm task 056). A generic writer that asks "
            + $"{ColumnRule} asks {Guard} in the same method before it writes, or is listed in this file with a reason and an ADR "
            + "(see the maintenance procedure in the file header):\n" + string.Join("\n", problems));

        // Non-vacuity: the census found exactly the callers it lists (task 056 counted nine).
        Assert.Equal(Guarded.Count + ListedCallers.Count, callers.Select(c => c.Member).Distinct().Count());
    }

    [Fact(DisplayName = "056 (Q5): the census detector fires on an unguarded update caller and not on the sanctioned shapes")]
    public void Detector_MustFireOnAnUnguardedUpdateCaller_AndMustNotFireOnTheSanctionedShapes()
    {
        const string file = "src/server/api/Sprk.Bff.Api/Services/Seeded/SeededWriter.cs";

        // Must fire: an update method that asks the column rule and writes without asking the guard.
        var unguarded = Source(
            "    internal async Task UpdateAsync(string table, Guid id, Dictionary<string, object?> payload, CancellationToken ct)",
            "    {",
            "        if (InheritedAccessPermission.RefuseAppOnlyWrite(table, payload.Keys) is { } refusal)",
            "            throw new InvalidOperationException(refusal.Reason);",
            "        // PrivateMessageWriteGuard.RefuseUpdateAsync( in a comment does not count",
            "        await _entities.UpdateAsync(table, id, payload, ct);",
            "    }",
            "",
            "    internal async Task OtherAsync(IGenericEntityService entities, Guid id, CancellationToken ct)",
            "    {",
            "        await PrivateMessageWriteGuard.RefuseUpdateAsync(entities, \"sprk_communication\", id, [], NullLogger.Instance, ct);",
            "    }");
        var unguardedCallers = FindCallers(file, unguarded);
        var fired = Violations(unguardedCallers, Array.Empty<Listed>(), MemberGuards(file, unguarded).ToDictionary(m => m.Member, m => m.AsksGuard)).ToList();
        Assert.Single(fired);
        Assert.Contains("UpdateAsync", fired[0], StringComparison.Ordinal);

        // Must fire: a listed predicate whose writer does not ask the guard.
        var predicate = Source(
            "    internal static bool MayWrite(string table, string column) =>",
            "        InheritedAccessPermission.RefuseAppOnlyWrite(table, [column]) is null;",
            "",
            "    internal async Task WriteAsync(string table, Guid id, CancellationToken ct)",
            "    {",
            "        await _entities.UpdateAsync(table, id, new(), ct);",
            "    }");
        var listedPredicate = new Listed(new Member(file, "MayWrite"), "seeded", "ADR-002", CoveredBy: new Member(file, "WriteAsync"));
        var uncovered = Violations(FindCallers(file, predicate), new[] { listedPredicate },
            MemberGuards(file, predicate).ToDictionary(m => m.Member, m => m.AsksGuard)).ToList();
        Assert.Single(uncovered);
        Assert.Contains("WriteAsync", uncovered[0], StringComparison.Ordinal);

        // Must not fire: the same update method asking the guard before it writes.
        var guarded = Source(
            "    internal async Task UpdateAsync(string table, Guid id, Dictionary<string, object?> payload, CancellationToken ct)",
            "    {",
            "        if (InheritedAccessPermission.RefuseAppOnlyWrite(table, payload.Keys) is { } refusal)",
            "            throw new InvalidOperationException(refusal.Reason);",
            "        if (await Sprk.Bff.Api.Services.Communication.Access.PrivateMessageWriteGuard.RefuseUpdateAsync(",
            "                _entities, table, id, payload.Keys, _logger, ct) is { } privateRefusal)",
            "            throw PrivateMessageWriteGuard.UpdateRefused(table, privateRefusal);",
            "        await _entities.UpdateAsync(table, id, payload, ct);",
            "    }");
        var guardedCallers = FindCallers(file, guarded);
        Assert.Single(guardedCallers);
        Assert.True(guardedCallers[0].AsksGuard);
        Assert.Empty(Violations(guardedCallers, Array.Empty<Listed>(), MemberGuards(file, guarded).ToDictionary(m => m.Member, m => m.AsksGuard)));

        // Must not fire: a listed create-only caller, and the definition of the rule itself.
        var createOnly = Source(
            "    private async Task<Decision> DecideAsync(string table, CreateRequest request, CancellationToken ct)",
            "    {",
            "        if (InheritedAccessPermission.RefuseAppOnlyWrite(table, request.Columns) is { } refusal)",
            "            return Decision.Refused(refusal);",
            "        return Decision.Create(request);",
            "    }");
        var listedCreate = new Listed(new Member(file, "DecideAsync"), "seeded create-only", "ADR-002");
        Assert.Empty(Violations(FindCallers(file, createOnly), new[] { listedCreate },
            MemberGuards(file, createOnly).ToDictionary(m => m.Member, m => m.AsksGuard)));

        var definition = Source(
            "    internal static RecordOwnerResolution? RefuseAppOnlyWrite(",
            "        string? table, IEnumerable<string> keys, WriteIdentity identity = WriteIdentity.AppOnly)",
            "    {",
            "        return null;",
            "    }");
        Assert.Empty(FindCallers(file, definition));
    }

    /// <summary>
    /// The writers the census above cannot see, because they do not ask the app-only column rule: the filing route
    /// (<c>OwnedChildWrite.RefileAsync</c>) and the inbound association's plain write
    /// (<c>IncomingAssociationResolver.ApplyDecisionAsync</c>). Each must ask the guard in the same method.
    /// </summary>
    private static readonly IReadOnlyList<Member> GuardedWithoutTheColumnRule = new[]
    {
        new Member("src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs", "RefileAsync"),
        new Member("src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs", "ApplyDecisionAsync"),
    };

    [Fact(DisplayName = "056 (Q5): the filing route and the inbound association write ask the private-message write guard")]
    public void TheFilingRoute_AndTheInboundAssociationWrite_AskThePrivateMessageWriteGuard()
    {
        var members = new Dictionary<Member, bool>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            if (!GuardedWithoutTheColumnRule.Any(m => m.File == relative))
                continue;
            foreach (var (member, asksGuard) in MemberGuards(relative, File.ReadAllLines(file)))
                members[member] = members.TryGetValue(member, out var seen) ? seen | asksGuard : asksGuard;
        }

        var problems = GuardedWithoutTheColumnRule.Where(m => !members.TryGetValue(m, out var asks) || !asks)
            .Select(m => $"{m}: expected to ask {Guard} in the method; the method moved, or the guard call was removed.").ToList();

        Assert.True(problems.Count == 0,
            "A private message is never filed by any writer (fm task 056). These writers do not ask the column rule, so the census "
            + "above cannot see them; each asks the guard before it writes:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "056 (Q5): the filing-route check fires on a method that lost the guard call and not on one that has it")]
    public void FilingRouteCheck_MustFireWithoutTheGuardCall_AndMustNotFireWithIt()
    {
        const string file = "src/server/api/Sprk.Bff.Api/Services/Seeded/SeededFiling.cs";
        var without = Source(
            "    internal static async Task<Outcome> RefileAsync(IGenericEntityService entities, Guid id, CancellationToken ct)",
            "    {",
            "        // PrivateMessageWriteGuard.RefuseUpdateAsync( in a comment does not count",
            "        await entities.UpdateAsync(\"sprk_communication\", id, new(), ct);",
            "        return Outcome.Done;",
            "    }");
        var with = Source(
            "    internal static async Task<Outcome> RefileAsync(IGenericEntityService entities, Guid id, CancellationToken ct)",
            "    {",
            "        if (await PrivateMessageWriteGuard.RefuseUpdateAsync(entities, \"sprk_communication\", id, [], NullLogger.Instance, ct) is { })",
            "            return Outcome.Refused;",
            "        await entities.UpdateAsync(\"sprk_communication\", id, new(), ct);",
            "        return Outcome.Done;",
            "    }");

        Assert.False(MemberGuards(file, without).Single(m => m.Member.Method == "RefileAsync").AsksGuard);
        Assert.True(MemberGuards(file, with).Single(m => m.Member.Method == "RefileAsync").AsksGuard);
    }

    /// <summary>Seeded source for the controls, in a file-scoped namespace as the BFF's files are.</summary>
    private static string[] Source(params string[] lines) => new[] { "namespace Seeded;", string.Empty }.Concat(lines).ToArray();

    // A member declaration: modifiers, a return type (a tuple type may hold parentheses, never '=' or ';'), the name, optional
    // type parameters, '('. A field initializer ("X = new(") has '=' before its parenthesis, so it is not a member here.
    private static readonly Regex MemberDeclaration = new(
        @"^(?<indent> +)(?:(?:public|internal|private|protected|static|async|override|sealed|virtual|new|partial|extern|unsafe)\s+)+[^=;]*?\b(?<name>\w+)\s*(?:<[^=;]*?>)?\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex FileScopedNamespace = new(@"^namespace\s+[\w.]+\s*;", RegexOptions.Compiled);

    private static readonly Regex ColumnRuleCall = new(@"\b" + ColumnRule + @"\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// The member declarations of a file: name, first line index, and the index after its last line (the next member's
    /// declaration). A member sits at four spaces in a file-scoped namespace and at eight in a block-scoped one; a deeper
    /// declaration (a local function, or a nested type's member) counts as the member it is written in. Limit: a caller inside a
    /// nested type is attributed to the outer member before it.
    /// </summary>
    private static List<(string Name, int Start, int End)> Members(IReadOnlyList<string> code)
    {
        var memberIndent = code.Any(l => FileScopedNamespace.IsMatch(l)) ? 4 : 8;
        var starts = new List<(string Name, int Start)>();
        for (var i = 0; i < code.Count; i++)
        {
            var match = MemberDeclaration.Match(code[i]);
            if (match.Success && match.Groups["indent"].Length == memberIndent)
                starts.Add((match.Groups["name"].Value, i));
        }

        var members = new List<(string Name, int Start, int End)>();
        for (var m = 0; m < starts.Count; m++)
            members.Add((starts[m].Name, starts[m].Start, m + 1 < starts.Count ? starts[m + 1].Start : code.Count));

        return members;
    }

    private static string[] CodeLines(IReadOnlyList<string> lines) => lines.Select(SourceScan.StripLineComment).ToArray();

    /// <summary>Every member of a file and whether its text asks the guard.</summary>
    internal static IEnumerable<(Member Member, bool AsksGuard)> MemberGuards(string relativePath, IReadOnlyList<string> lines)
    {
        var code = CodeLines(lines);
        foreach (var (name, start, end) in Members(code))
        {
            var text = string.Join("\n", code[start..end]);
            yield return (new Member(relativePath, name), text.Contains(Guard, StringComparison.Ordinal));
        }
    }

    /// <summary>Every <c>RefuseAppOnlyWrite(</c> call in a file, attributed to its member; the definition is not a call.</summary>
    internal static IReadOnlyList<Caller> FindCallers(string relativePath, IReadOnlyList<string> lines)
    {
        var code = CodeLines(lines);
        var members = Members(code);
        var callers = new List<Caller>();
        for (var i = 0; i < code.Length; i++)
        {
            if (!ColumnRuleCall.IsMatch(code[i]))
                continue;

            var owner = members.LastOrDefault(m => m.Start <= i && i < m.End);
            if (owner.Name is null)
            {
                callers.Add(new Caller(new Member(relativePath, $"(unattributed, line {i + 1})"), i + 1, AsksGuard: false));
                continue;
            }

            // The declaration line of the rule itself is its definition, not a call.
            if (owner.Name == ColumnRule && owner.Start == i)
                continue;

            var text = string.Join("\n", code[owner.Start..owner.End]);
            callers.Add(new Caller(new Member(relativePath, owner.Name), i + 1, text.Contains(Guard, StringComparison.Ordinal)));
        }

        return callers;
    }

    /// <summary>
    /// The census rule: a caller that does not ask the guard is a problem unless listed; a listed predicate's writer must ask it.
    /// </summary>
    internal static IEnumerable<string> Violations(
        IEnumerable<Caller> callers, IReadOnlyList<Listed> listed, IReadOnlyDictionary<Member, bool> memberGuards)
    {
        foreach (var caller in callers.Where(c => !c.AsksGuard).GroupBy(c => c.Member))
        {
            var entry = listed.FirstOrDefault(l => l.Member == caller.Key);
            if (entry is null)
            {
                yield return $"{caller.Key} (line {caller.First().Line}): asks {ColumnRule} but not {Guard}, and is not listed as create-only, "
                             + "not a communication, or a predicate whose writer asks the guard.";
                continue;
            }

            if (entry.CoveredBy is { } writer && !(memberGuards.TryGetValue(writer, out var asks) && asks))
                yield return $"{caller.Key}: listed as a predicate covered by {writer}, but {writer} does not ask {Guard}.";
        }
    }
}
