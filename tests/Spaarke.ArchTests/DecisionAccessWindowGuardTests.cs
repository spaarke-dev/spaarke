using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-010 part C (#3184), uac-r2 amendment A2 and the "only the commit opens it" rule: the decision commit's access window
/// (<c>DecisionAccessWindow</c>) keeps the caller's privilege answers and the record-type catalog for the WHOLE commit. That is
/// sound only because nothing the commit runs edits roles or team membership; this guard fails any code on the window's path
/// that names a role or team-membership association, a team membership request, or <c>ExternalCollaboratorPlacement</c> (the one
/// BFF component that associates a role).
/// </summary>
/// <remarks>
/// <para><b>Part 1, A2 (uac-r2 rulings after run 1, option (b) tightened).</b> Roslyn, over every file under
/// <c>Services/Signals</c> and <c>Services/RecordCreation</c>, every file of the step-12 services
/// (<see cref="StepTwelveServices"/>, as <see cref="PostCreateNoCallerAccessGuardTests"/> lists them) and the four further files
/// the commit reaches (<see cref="ExtraFiles"/>: the child-record route, the caller write authorization, the ownership resolver and
/// the external team-share synchronizer). It counts each banned
/// word in identifier tokens and string text (comments and XML documentation are trivia, so they are not seen). One PINNED
/// exemption: <c>SecureRootInheritance.Cascade.cs</c> <c>PlatformReadableTablesAsync</c>, which READS whether the BFF's own user
/// holds System Administrator or a field-security profile (no association write). There each token is pinned to its exact count
/// (<see cref="PinnedReads"/>, 4 in total), each occurrence must sit in the filter argument of a <c>QueryAsync</c> call and be a
/// navigation inside <c>any(…)</c>, and the method may hold no write shape. A new occurrence, a write shape there, or the
/// method's removal fails (the stale-entry check). Two further PINNED read-only lookups (<see cref="LookupPins"/>): the one
/// method in <c>RecordOwnershipResolver</c> and the one in <c>ExternalTeamShareSynchronizer</c> that name
/// <c>ExternalCollaboratorPlacement</c> exactly once, as the target of <c>.ResolveUnitAsync(…)</c> (a business-unit lookup by
/// name, no role association), and hold no write shape; any other occurrence there, a different member, a write shape or the
/// method's removal fails.</para>
/// <para><b>Part 2.</b> <c>DecisionAccessWindow.Open(</c> is called from <c>Services/Signals/DecisionCommitService.cs</c> and
/// nowhere else (the window is one commit's; a second opener could keep answers across work the design did not audit).</para>
/// <para><b>Limits.</b> A name match, not a type-resolved one: a role edit through a type whose name carries none of the banned
/// words, or reached through a class outside the scanned files, is not seen.</para>
/// <para><b>Known limit K (uac-r2 access review, 2026-11-26, item 2).</b> Privilege answers live for the whole commit, but this
/// scan does not follow into the services the NON-child-create executors call (communication, events, the update route core).
/// Their executes run with the window suspended, so they never read a kept answer; the limit is that a role or team-membership
/// write added there would not be seen here, and a later step's kept privilege answer would predate it. Today nothing on that
/// path writes team or role membership: only <c>RegistrationDataverseService</c> and <c>ExternalCollaboratorPlacement</c> do,
/// and neither is reached by a decision. Closing it fully means widening the scanned files to everything the executors reach.</para>
/// <para>Enforced by: this guard (ArchTests, CI and the Stop hook). Must-fire and must-not-fire controls below.</para>
/// </remarks>
public class DecisionAccessWindowGuardTests
{
    private static readonly string[] BannedWords =
    [
        "systemuserroles_association",
        "teammembership_association",
        "teamroles_association",
        "AddMembersTeam",
        "RemoveMembersTeam",
        "ExternalCollaboratorPlacement",
    ];

    /// <summary>The step-12 services, by class name (each file <c>Name.cs</c> or <c>Name.*.cs</c>).</summary>
    private static readonly string[] StepTwelveServices =
    [
        "CoreAncestorRestamper",
        "SecureChildShareSynchronizer",
        "AssignedAccessMaterializer",
        "SecureRootFilingGate",
        "SecureRootInheritance",
    ];

    /// <summary>Further files the commit reaches (BFF-root relative): the route core, the caller's write authorization, the owner resolver, the external team-share synchronizer.</summary>
    private static readonly string[] ExtraFiles =
    [
        "Api/ChildRecordEndpoints.cs",
        "Services/Dataverse/CallerWriteAuthorization.cs",
        "Services/Dataverse/RecordOwnershipResolver.cs",
        "Services/Access/ExternalTeamShareSynchronizer.cs",
    ];

    /// <summary>The read-only unit lookups pinned (BFF-root relative file, method): each names <c>ExternalCollaboratorPlacement</c> once, as <c>.ResolveUnitAsync(…)</c>.</summary>
    private static readonly IReadOnlyDictionary<string, string> LookupPins = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Reads business units only (ResolveUnitAsync); uac-r2 approved 2026-11-26.
        ["Services/Dataverse/RecordOwnershipResolver.cs"] = "IsInExternalCollaboratorsUnitAsync",
        // Reads business units only (ResolveUnitAsync); uac-r2 approved 2026-11-26.
        ["Services/Access/ExternalTeamShareSynchronizer.cs"] = "ResolveExternalTeamAsync",
    };

    private const string LookupWord = "ExternalCollaboratorPlacement";
    private const string LookupMember = "ResolveUnitAsync";

    private const string PinnedFile = "SecureRootInheritance.Cascade.cs";
    private const string PinnedMethod = "PlatformReadableTablesAsync";

    /// <summary>The exact reads the pinned method makes (uac-r2: 4 in total as found, 2026-11-25).</summary>
    private static readonly IReadOnlyDictionary<string, int> PinnedReads = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["systemuserroles_association"] = 1,
        ["teamroles_association"] = 1,
        ["teammembership_association"] = 2,
    };

    private static readonly string[] WriteVerbs =
        ["Create", "Update", "Delete", "Upsert", "Associate", "Disassociate", "Post", "Patch", "Put", "Execute", "Send", "Grant", "Revoke", "Assign", "AddMembers", "RemoveMembers"];

    private const string OpenerFile = "src/server/api/Sprk.Bff.Api/Services/Signals/DecisionCommitService.cs";

    private static readonly string BffRoot = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    // ── Part 1: A2 ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ISS-010 C A2: the access window's path names no role or team-membership edit (one pinned read exempt)")]
    public void WindowPath_NamesNoRoleOrTeamMembershipEdit()
    {
        var files = ScannedFiles().ToList();
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Services", "Signals", "DecisionCommitService.cs"), StringComparison.Ordinal));
        Assert.Contains(files, f => Path.GetFileName(f) == PinnedFile);
        foreach (var extra in ExtraFiles)
            Assert.Contains(files, f => BffRelative(f) == extra);

        var problems = new List<string>();
        var pinnedSeen = false;
        var lookupsSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var bffRelative = BffRelative(file);
            LookupPins.TryGetValue(bffRelative, out var lookupMethod);
            var result = Scan(SourceScan.Relative(file).Replace('\\', '/'), File.ReadAllText(file), Path.GetFileName(file) == PinnedFile, lookupMethod);
            problems.AddRange(result.Problems);
            pinnedSeen |= result.PinnedMethodFound;
            if (result.LookupMethodFound)
                lookupsSeen.Add(bffRelative);
        }

        if (!pinnedSeen)
            problems.Add($"{PinnedFile}: {PinnedMethod} is gone. Remove its exemption from this guard (stale entry); do not leave it listed.");
        foreach (var (lookupFile, lookupMethod) in LookupPins)
        {
            if (!lookupsSeen.Contains(lookupFile))
                problems.Add($"{lookupFile}: {lookupMethod} is gone. Remove its exemption from this guard (stale entry); do not leave it listed.");
        }

        Assert.True(problems.Count == 0,
            "The decision commit's access window keeps privilege answers for the whole commit, so nothing on its path may edit "
            + "roles or team membership (ISS-010 C, uac-r2 A2):\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "ISS-010 C A2 guard (must fire): each banned word, in code or a string, and every pinned-method breach")]
    public void A2Scan_MustFire()
    {
        foreach (var word in BannedWords)
        {
            Assert.NotEmpty(Scan("fixture.cs", $"class S {{ void M() {{ var x = \"teams(1)/{word}/$ref\"; }} }}", pinnedFile: false).Problems);
        }

        Assert.NotEmpty(Scan("fixture.cs", "class S { void M() { var r = new AddMembersTeamRequest(); } }", false).Problems);
        Assert.NotEmpty(Scan("fixture.cs", "class S { void M() { var r = new RemoveMembersTeamRequest(); } }", false).Problems);
        Assert.NotEmpty(Scan("fixture.cs", "class S { Task M() => ExternalCollaboratorPlacement.ResolveUnitAsync(d, ct); }", false).Problems);
        Assert.NotEmpty(Scan("fixture.cs", "class S { void M(Guid u) { var p = $\"systemusers({u})/systemuserroles_association/$ref\"; } }", false).Problems);

        // The pinned method: a fifth occurrence, a write shape, a token outside a QueryAsync filter, a token not inside any().
        Assert.NotEmpty(Scan(PinnedFile, Pinned(extra: "var y = await _webApi.QueryAsync<R>(\"roles\", $\"teamroles_association/any(t:t/roleid eq {user})\", \"roleid\", cancellationToken: ct);"), true).Problems);
        Assert.NotEmpty(Scan(PinnedFile, Pinned(extra: "await _webApi.CreateAsync(\"x\", body, ct);"), true).Problems);
        Assert.NotEmpty(Scan(PinnedFile, Pinned(extra: "var s = \"/$ref\";"), true).Problems);
        Assert.NotEmpty(Scan(PinnedFile, Pinned(firstFilter: "\"systemuserroles_association/any(u:u/systemuserid eq x) or teamroles_association/any(t:t/teammembership_association/any(u:u/systemuserid eq x))\"", firstCall: "PatchAsync"), true).Problems);
        Assert.NotEmpty(Scan(PinnedFile, Pinned(firstFilter: "\"systemuserroles_association eq null or teamroles_association/any(t:t/teammembership_association/any(u:u/systemuserid eq x))\""), true).Problems);

        // The stale entry: the pinned method missing from its file.
        Assert.False(Scan(PinnedFile, "class SecureRootInheritance { Task Other() => Task.CompletedTask; }", true).PinnedMethodFound);

        // The read-only unit lookups: a second occurrence, another member, a write shape, a $ref, a banned association word in the
        // method, the name in another method of the file, the name in a file with no pin, and the stale entry.
        const string lookupFile = "Services/Dataverse/RecordOwnershipResolver.cs";
        Assert.NotEmpty(Scan(lookupFile, Lookup(extra: "var again = ExternalCollaboratorPlacement.ResolveUnitAsync(_dataverse, ct);"), false, LookupMethod).Problems);
        Assert.NotEmpty(Scan(lookupFile, Lookup(call: "Sprk.Bff.Api.Services.ExternalAccess.ExternalCollaboratorPlacement.PlaceAsync(_dataverse, ct)"), false, LookupMethod).Problems);
        Assert.NotEmpty(Scan(lookupFile, Lookup(call: "ExternalCollaboratorPlacement.ResolveUnitAsync"), false, LookupMethod).Problems); // named, not called
        Assert.NotEmpty(Scan(lookupFile, Lookup(extra: "await _dataverse.UpdateAsync(\"businessunit\", id, null!, ct);"), false, LookupMethod).Problems);
        Assert.NotEmpty(Scan(lookupFile, Lookup(extra: "await _dataverse.AssociateAsync(\"x\", id, \"y\", id, ct);"), false, LookupMethod).Problems);
        Assert.NotEmpty(Scan(lookupFile, Lookup(extra: "var s = \"/$ref\";"), false, LookupMethod).Problems);
        Assert.NotEmpty(Scan(lookupFile, Lookup(extra: "var s = \"systemuserroles_association\";"), false, LookupMethod).Problems);
        Assert.NotEmpty(Scan(lookupFile, Lookup(extra: "var s = \"ExternalCollaboratorPlacement\";"), false, LookupMethod).Problems);
        Assert.NotEmpty(Scan(lookupFile, Lookup() + "\nclass Other { Task M() => ExternalCollaboratorPlacement.ResolveUnitAsync(d, ct); }", false, LookupMethod).Problems);
        Assert.NotEmpty(Scan(lookupFile, Lookup(), false).Problems); // no pin for this file: the name is banned
        Assert.False(Scan(lookupFile, "class RecordOwnershipResolver { Task Other() => Task.CompletedTask; }", false, LookupMethod).LookupMethodFound);
    }

    [Fact(DisplayName = "ISS-010 C A2 guard (must not fire): the pinned reads, comments, XML docs and other association names")]
    public void A2Scan_MustNotFire()
    {
        var pinned = Scan(PinnedFile, Pinned(), true);
        Assert.True(pinned.PinnedMethodFound);
        Assert.Empty(pinned.Problems);

        // The pinned lookups: qualified and unqualified, XML docs naming the type are trivia.
        var lookup = Scan("Services/Dataverse/RecordOwnershipResolver.cs", Lookup(), false, LookupMethod);
        Assert.True(lookup.LookupMethodFound);
        Assert.Empty(lookup.Problems);
        Assert.Empty(Scan("Services/Dataverse/RecordOwnershipResolver.cs", Lookup(call: "ExternalCollaboratorPlacement.ResolveUnitAsync(_dataverse, ct)"), false, LookupMethod).Problems);
        Assert.Empty(Scan("Services/Dataverse/RecordOwnershipResolver.cs", Lookup(extra: "var teams = (await _dataverse.RetrieveMultipleAsync(q, ct)).Entities;"), false, LookupMethod).Problems);

        const string clean = """
            /// <summary>Not <see cref="ExternalCollaboratorPlacement"/>; reads no systemuserroles_association.</summary>
            class S
            {
                // teammembership_association and AddMembersTeam in a comment only.
                /* RemoveMembersTeam teamroles_association */
                private const string Other = "teamprofiles_association/any(t:t/teamid eq x)";
                private const string Shares = "sprk_matters(1)/Microsoft.Dynamics.CRM.GrantAccess";
            }
            """;
        Assert.Empty(Scan("fixture.cs", clean, false).Problems);
    }

    // ── Part 2: only the commit opens the window ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ISS-010 C: DecisionAccessWindow.Open is called from DecisionCommitService.cs and nowhere else")]
    public void OnlyTheDecisionCommit_OpensTheAccessWindow()
    {
        var problems = new List<string>();
        var seen = false;
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var calls = OpenCalls(File.ReadAllText(file));
            if (calls == 0)
                continue;
            if (relative == OpenerFile)
                seen = true;
            else
                problems.Add($"{relative}: {calls} call(s) of DecisionAccessWindow.Open");
        }

        if (!seen)
            problems.Add($"{OpenerFile}: opens no DecisionAccessWindow: the scan lost its signal or the commit no longer opens it");

        Assert.True(problems.Count == 0,
            "The access window is one decision commit's; only DecisionCommitService opens it (ISS-010 C):\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "ISS-010 C opener guard (must fire / must not fire)")]
    public void OpenerScan_Controls()
    {
        Assert.Equal(1, OpenCalls("class C { void M(HttpContext h) { using var w = DecisionAccessWindow.Open(h, id); } }"));
        Assert.Equal(1, OpenCalls("class C { void M(HttpContext h) { var w = Signals.DecisionAccessWindow\n  .Open (h, id); } }"));

        Assert.Equal(0, OpenCalls("class C { /// <see cref=\"DecisionAccessWindow.Open\"/>\n void M() { } }"));
        Assert.Equal(0, OpenCalls("class C { // DecisionAccessWindow.Open(h, id)\n void M() { var s = \"DecisionAccessWindow.Open(h)\"; } }"));
        Assert.Equal(0, OpenCalls("class C { void M(HttpContext h) { var w = DecisionAccessWindow.Of(h); } }"));
        Assert.Equal(0, OpenCalls("internal sealed class DecisionAccessWindow { internal static DecisionAccessWindow Open(HttpContext r, Guid c) => null!; }"));
    }

    // ── The scanners ──────────────────────────────────────────────────────────────────────────────────────────────────

    internal static IEnumerable<string> ScannedFiles() // ISS-010 D1: also DecisionCommitTeamWriteGuardTests' scope
    {
        static bool NotBuildOutput(string f) =>
            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

        var folders = new[] { Path.Combine(BffRoot, "Services", "Signals"), Path.Combine(BffRoot, "Services", "RecordCreation") };
        var files = folders.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)).Where(NotBuildOutput);
        var services = Directory.EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories).Where(NotBuildOutput).Where(f =>
        {
            var name = Path.GetFileName(f);
            return StepTwelveServices.Any(s => name.Equals(s + ".cs", StringComparison.Ordinal)
                                               || (name.StartsWith(s + ".", StringComparison.Ordinal) && name.EndsWith(".cs", StringComparison.Ordinal)));
        });
        var extras = ExtraFiles.Select(f => Path.Combine(BffRoot, f.Replace('/', Path.DirectorySeparatorChar)));
        return files.Concat(services).Concat(extras).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The file's path under the BFF root, with <c>/</c> separators.</summary>
    private static string BffRelative(string file) => Path.GetRelativePath(BffRoot, file).Replace('\\', '/');

    internal sealed record ScanResult(IReadOnlyList<string> Problems, bool PinnedMethodFound, bool LookupMethodFound = false);

    /// <summary>
    /// The banned words in <paramref name="source"/>'s code (identifiers and string text), with the pinned method's rules applied when
    /// <paramref name="pinnedFile"/>, and the read-only unit-lookup rules applied to <paramref name="lookupMethod"/> when given.
    /// </summary>
    internal static ScanResult Scan(string file, string source, bool pinnedFile, string? lookupMethod = null)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var pinnedMethods = pinnedFile
            ? root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == PinnedMethod).ToList()
            : [];
        var lookupMethods = lookupMethod is null
            ? []
            : root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == lookupMethod).ToList();
        var problems = new List<string>();
        var pinnedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lookupCount = 0;

        foreach (var token in root.DescendantTokens())
        {
            var text = TextOf(token);
            if (text is null)
                continue;
            var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            foreach (var word in BannedWords)
            {
                var count = Occurrences(text, word);
                if (count == 0)
                    continue;

                if (word == LookupWord && lookupMethods.Any(m => m.Span.Contains(token.Span)))
                {
                    lookupCount += count;
                    if (!token.IsKind(SyntaxKind.IdentifierToken) || !IsResolveUnitCall(token))
                        problems.Add($"{file}:{line} {lookupMethod}: {word} is not the target of .{LookupMember}(…)");
                    continue;
                }

                var inPinned = pinnedMethods.Any(m => m.Span.Contains(token.Span));
                if (!inPinned || !PinnedReads.ContainsKey(word))
                {
                    problems.Add($"{file}:{line} names {word}");
                    continue;
                }

                pinnedCounts[word] = pinnedCounts.GetValueOrDefault(word) + count;
                if (!IsQueryFilterRead(token, text, word))
                    problems.Add($"{file}:{line} {PinnedMethod}: {word} is not a navigation inside any(…) in a QueryAsync filter");
            }
        }

        if (pinnedMethods.Count > 1)
            problems.Add($"{file}: {PinnedMethod} is declared {pinnedMethods.Count} times; the exemption pins one");

        if (pinnedMethods.Count == 1)
        {
            foreach (var (word, expected) in PinnedReads)
            {
                var actual = pinnedCounts.GetValueOrDefault(word);
                if (actual != expected)
                    problems.Add($"{file} {PinnedMethod}: {word} occurs {actual} time(s), pinned at {expected}");
            }

            foreach (var call in pinnedMethods[0].DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = NameOf(call);
                if (name is not null && WriteVerbs.Any(v => name.StartsWith(v, StringComparison.Ordinal)))
                    problems.Add($"{file} {PinnedMethod}: write shape {name}(…)");
            }

            foreach (var token in pinnedMethods[0].DescendantTokens())
            {
                if (TextOf(token) is { } t && t.Contains("$ref", StringComparison.Ordinal))
                    problems.Add($"{file} {PinnedMethod}: write shape $ref");
            }
        }

        if (lookupMethods.Count > 1)
            problems.Add($"{file}: {lookupMethod} is declared {lookupMethods.Count} times; the exemption pins one");

        if (lookupMethods.Count == 1)
        {
            if (lookupCount != 1)
                problems.Add($"{file} {lookupMethod}: {LookupWord} occurs {lookupCount} time(s), pinned at 1");

            foreach (var call in lookupMethods[0].DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = NameOf(call);
                if (name is not null && WriteVerbs.Any(v => name.StartsWith(v, StringComparison.Ordinal)))
                    problems.Add($"{file} {lookupMethod}: write shape {name}(…)");
            }

            foreach (var token in lookupMethods[0].DescendantTokens())
            {
                if (TextOf(token) is { } t && t.Contains("$ref", StringComparison.Ordinal))
                    problems.Add($"{file} {lookupMethod}: write shape $ref");
            }
        }

        return new ScanResult(problems, pinnedMethods.Count == 1, lookupMethods.Count == 1);
    }

    /// <summary>The identifier is the type of a <c>Type.ResolveUnitAsync(…)</c> call (qualified or not): the unit lookup by name, which reads and writes nothing.</summary>
    private static bool IsResolveUnitCall(SyntaxToken token)
    {
        SyntaxNode? node = token.Parent;
        if (node?.Parent is MemberAccessExpressionSyntax qualified && ReferenceEquals(qualified.Name, node))
            node = qualified;

        return node?.Parent is MemberAccessExpressionSyntax member
               && ReferenceEquals(member.Expression, node)
               && member.Name.Identifier.ValueText == LookupMember
               && member.Parent is InvocationExpressionSyntax invocation
               && ReferenceEquals(invocation.Expression, member);
    }

    /// <summary>The code text of an identifier or string token; null for anything else (comments and docs are trivia, never tokens).</summary>
    private static string? TextOf(SyntaxToken token) => token.Kind() switch
    {
        SyntaxKind.IdentifierToken => token.ValueText,
        SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken or SyntaxKind.SingleLineRawStringLiteralToken
            or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8StringLiteralToken => token.Text,
        _ => null,
    };

    private static int Occurrences(string text, string word)
    {
        var count = 0;
        for (var i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + word.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>Every occurrence of <paramref name="word"/> in the string is followed by <c>/any(</c>, and the string is (part of) the filter argument of a <c>QueryAsync</c> call.</summary>
    private static bool IsQueryFilterRead(SyntaxToken token, string text, string word)
    {
        for (var i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + word.Length, StringComparison.Ordinal))
        {
            if (string.CompareOrdinal(text, i + word.Length, "/any(", 0, 5) != 0)
                return false;
        }

        var argument = token.Parent?.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
        return argument?.Parent is ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list
               && NameOf(call) == "QueryAsync"
               && list.Arguments.IndexOf(argument) == 1;
    }

    private static string? NameOf(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax n => n.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        _ => null,
    };

    /// <summary>The number of <c>DecisionAccessWindow.Open(</c> calls in <paramref name="source"/> (Roslyn: comments and strings are not calls).</summary>
    internal static int OpenCalls(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Count(call =>
            call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Open" } member
            && member.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText == "DecisionAccessWindow",
                MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.ValueText == "DecisionAccessWindow",
                _ => false,
            });

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private const string LookupMethod = "IsInExternalCollaboratorsUnitAsync";

    /// <summary>A unit-lookup method in its shape today (<c>RecordOwnershipResolver</c>), with an optional extra statement or a different call.</summary>
    private static string Lookup(
        string extra = "",
        string call = "Sprk.Bff.Api.Services.ExternalAccess.ExternalCollaboratorPlacement\n            .ResolveUnitAsync(_dataverse, ct)") =>
        $$"""
        partial class RecordOwnershipResolver
        {
            /// <summary>See <see cref="Sprk.Bff.Api.Services.ExternalAccess.ExternalCollaboratorPlacement.ResolveUnitAsync"/>.</summary>
            private async Task<bool> {{LookupMethod}}(Guid businessUnitId, CancellationToken ct)
            {
                var unit = await {{call}}.ConfigureAwait(false);
                {{extra}}
                return unit.Candidates.Contains(businessUnitId);
            }
        }
        """;

    /// <summary>The pinned method in its shape today (4 reads), with an optional extra statement or a different first filter / call.</summary>
    private static string Pinned(
        string extra = "",
        string firstFilter = "$\"_roletemplateid_value eq {id} and (systemuserroles_association/any(u:u/systemuserid eq {user}) \" + $\"or teamroles_association/any(t:t/teammembership_association/any(u:u/systemuserid eq {user})))\"",
        string firstCall = "QueryAsync") =>
        $$"""
        partial class SecureRootInheritance
        {
            /// <summary>A nested any() over <c>teammembership_association</c>.</summary>
            private async Task<HashSet<string>> {{PinnedMethod}}(CancellationToken ct)
            {
                var admin = await _webApi.{{firstCall}}<RoleIdRow>("roles",
                    {{firstFilter}},
                    "roleid", cancellationToken: ct).ConfigureAwait(false);
                var profiles = await _webApi.QueryAsync<ProfileIdRow>("fieldsecurityprofiles",
                    $"systemuserprofiles_association/any(u:u/systemuserid eq {user}) " +
                    $"or teamprofiles_association/any(t:t/teammembership_association/any(u:u/systemuserid eq {user}))",
                    "fieldsecurityprofileid", cancellationToken: ct).ConfigureAwait(false);
                {{extra}}
                return new HashSet<string>();
            }
        }
        """;
}
