using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 062 (spaarke-ontology-platform-r1; FR-31, FR-33, D-42; row-contract requirements 2 and 4): the guards that keep the Briefing
/// narrative and the Know lane out of the Work Item list.
///
///   - No model output influences order or membership: the Signal read route (the one place a worklist is ranked and filtered) and its
///     endpoint reach no model, narrator or briefing code, and nothing writes <c>sprk_rankscore</c> (D-42 amended: rank lives in the
///     read route, never in a stored score).
///   - A new matter or document is not a Work Item: no seeded policy turns the creation of a Know record (matter, project, document)
///     into a Signal on its own. Know fails requirement 4 (it asks for no action that changes something), so a Know record reaches the
///     worklist only through a rule with a real predicate that PROMOTES it (FR-30; the first such rule is task 063).
///
/// Each source-scan guard ships with a must-fire control, so a guard that stops matching fails here rather than passing vacuously.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). Enforced by: this file (CLAUDE.md section 16 enforcement ladder: source-scan guard).</remarks>
[Trait("status", "new")]
public class WorkItemOrderAndMembershipTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static string Api(params string[] parts) =>
        Path.Combine(new[] { RepoRoot(), "src", "server", "api", "Sprk.Bff.Api" }.Concat(parts).ToArray());

    /// <summary>The C# a file runs: line comments and block comments removed, so prose cannot trip (or hide) a match.</summary>
    private static string CodeOnly(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//.*$", string.Empty, RegexOptions.Multiline);

    /// <summary>What a ranking / membership file must not reach: the narrators, the briefing, the model clients and the AI facade.</summary>
    private static readonly Regex ModelReach = new(
        @"Services\.Ai\.Narrators|Services\.Ai\.Nodes|DailyBriefing|IOpenAiClient|IChatClient|ICompletion|PublicContracts|Narrat(?:or|e)\b|TldrFacts",
        RegexOptions.Compiled);

    // ---- No model output influences order or membership -------------------------------------------------------------------

    [Theory]
    [InlineData("Services/Signals/SignalReadService.cs")]
    [InlineData("Api/Signals/SignalReadEndpoints.cs")]
    public void TheSignalReadRoute_ReachesNoModelNarratorOrBriefingCode(string relativePath)
    {
        var code = CodeOnly(File.ReadAllText(Api(relativePath.Split('/'))));

        ModelReach.Matches(code).Select(m => m.Value).Distinct().Should().BeEmpty(
            "the list's membership and order are deterministic (row-contract requirement 2); a model may write ABOUT the list, never shape it");
    }

    [Fact]
    public void TheModelReachGuard_Fires_OnCodeThatReachesTheNarrator()
    {
        // Must-fire control: the same scan over code that does what the guard forbids.
        var offending = CodeOnly("using Sprk.Bff.Api.Services.Ai.Narrators;\nvar facts = DailyBriefingCollector.BuildTldrFacts(request);");

        ModelReach.IsMatch(offending).Should().BeTrue();
    }

    [Fact]
    public void TheModelReachGuard_DoesNotFire_OnComments()
    {
        // Must-not-fire control: prose that names the narrator is not code.
        var prose = CodeOnly("// the narrator (DailyBriefing) never orders this list\n/* TldrFacts is not used here */\nvar x = 1;");

        ModelReach.IsMatch(prose).Should().BeFalse();
    }

    [Fact]
    public void NothingWritesOrReadsASavedRankScore_RankLivesInTheReadRoute()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src", "server"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => CodeOnly(File.ReadAllText(f)).Contains("sprk_rankscore", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(RepoRoot(), f))
            .ToArray();

        offenders.Should().BeEmpty("D-42 as amended: nothing writes sprk_rankscore; the order is computed in SignalReadService on every read");
    }

    [Fact]
    public void TheRankKeys_AreSeverityThenHighPriorityThenOldestThenRecordNumber_InTheReadRoute()
    {
        // D-42 stated in the code that applies it. The full ordering is exercised end to end by
        // SignalReadRouteTests.LaneOrder_IsSeverityThenHighPriorityThenOldestThenRecordNumber_AndRepeatable.
        var code = CodeOnly(File.ReadAllText(Api("Services", "Signals", "SignalReadService.cs")));

        var byFlag = code.IndexOf("(a.HighPriority ? 0 : 1).CompareTo(b.HighPriority ? 0 : 1)", StringComparison.Ordinal);
        byFlag.Should().BeGreaterThan(0, "sprk_highpriority is a rank input of the read route");
        code.IndexOf("HighPriorityColumn", StringComparison.Ordinal).Should().BeGreaterThan(0);
    }

    // ---- A new matter or document is not a Work Item ----------------------------------------------------------------------

    private static readonly string[] KnowSubjects = { "sprk_matter", "sprk_project", "sprk_document" };
    private static readonly string[] CreationOnlyColumns = { "createdon", "modifiedon", "overriddencreatedon" };

    /// <summary>True when a rule body's <c>when</c> asks only about when a Know record was created or changed (a new record, as such).</summary>
    private static bool IsCreationOnly(JsonObject ruleBody)
    {
        if (ruleBody["subject"]?.GetValue<string>() is not { } subject || !KnowSubjects.Contains(subject)) return false;
        var when = ruleBody["when"] as JsonObject;
        var all = ruleBody["all"] as JsonArray;
        var conditions = when?.Select(kv => kv.Key).ToArray() ?? Array.Empty<string>();
        return (all is null || all.Count == 0) && conditions.All(c => CreationOnlyColumns.Contains(c, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoSeededPolicy_MakesTheCreationOfAKnowRecordAWorkItem()
    {
        var seed = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "config", "do-lane.policies.json")))!.AsObject();

        var offenders = seed["policies"]!.AsArray()
            .Select(p => p!.AsObject())
            .SelectMany(p => p["versions"]!.AsArray().Select(v => (Code: (string)p["code"]!, Body: v!["ruleBody"]!.AsObject())))
            .Where(v => IsCreationOnly(v.Body))
            .Select(v => v.Code)
            .ToArray();

        offenders.Should().BeEmpty("a new matter, project or document is Know: awareness, not an action (row-contract requirement 4)");
    }

    [Fact]
    public void TheCreationOnlyGuard_Fires_OnARuleThatRaisesEveryNewDocument()
    {
        // Must-fire control.
        var body = JsonNode.Parse("""{ "type": "Existence", "subject": "sprk_document", "when": { "createdon": { ">=": "now-1d" } }, "all": [] }""")!.AsObject();

        IsCreationOnly(body).Should().BeTrue();
    }

    [Fact]
    public void TheCreationOnlyGuard_DoesNotFire_OnARuleWithARealPredicate_OrOnADoSubject()
    {
        // Must-not-fire controls: a Know record promoted by a real predicate (FR-30), and an overdue task.
        var promoted = JsonNode.Parse("""{ "type": "Existence", "subject": "sprk_document", "when": { "createdon": { ">=": "now-1d" }, "statuscode": 1 }, "all": [] }""")!.AsObject();
        var task = JsonNode.Parse("""{ "type": "Existence", "subject": "sprk_event", "when": { "createdon": { ">=": "now-1d" } }, "all": [] }""")!.AsObject();

        IsCreationOnly(promoted).Should().BeFalse();
        IsCreationOnly(task).Should().BeFalse();
    }
}
