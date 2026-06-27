// R3 Part 3 H3 — Task 065 (2026-06-21): Canvas/server mapping-drift integration test (G6
// prevention guardrail). Per spec FR-3H3.1 + AC-H3.1, this CI-running test asserts the
// playbook canvas surface (client) and the playbook execution surface (server) stay in
// lock-step:
//
//   1. Every canvas node type emitted by the PlaybookBuilder's `buildConfigJson()` switch in
//      `playbookNodeSync.ts` MUST have a matching arm in the server's `NodeService.cs`
//      `MapCanvasTypeToActionType()` switch — otherwise authoring a playbook with that node
//      type would persist a record the server cannot dispatch.
//
//   2. Every `ActionType` slot referenced by the client's `NodeTypeToActionType` lookup MUST
//      exist as a named member of the server-side `ActionType` enum in `INodeExecutor.cs` —
//      otherwise the persisted `__actionType` integer would not resolve to an executor.
//
// Source-of-truth: this is a parse-the-source test (pure C# + regex, no Roslyn / no Node
// subprocess) so it runs on every CI image without extra tooling. The brittleness trade-off is
// accepted because both sides have well-known, stable shapes (typed enums + literal switch
// arms) and the failure messages below name the missing entry exactly — a future operator
// repairs the drift in seconds.
//
// HOW TO RE-VERIFY DRIFT DETECTION (acceptance criterion AC-H3.1 "Test FAILS on intentional
// drift"): scratch-edit `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts`
// to add a new `case 'totallyMadeUpType':` arm inside the `buildConfigJson()` switch (with no
// matching server arm), then run:
//
//     dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ ^
//         --filter "FullyQualifiedName~CanvasServerMappingDriftTests"
//
// The first test below MUST fail with a message naming `totallyMadeUpType` as the unmapped
// canvas arm. REVERT the scratch edit; the test MUST return to green. DO NOT commit the
// scratch edit. This procedure was used to author + verify the test under task 065.

using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Playbooks;

[Trait("Category", "Integration")]
[Trait("Phase", "P7.1")]
[Trait("Coverage", "FR-3H3.1,AC-H3.1")]
public sealed class CanvasServerMappingDriftTests
{
    // Repo-root-relative paths. Resolved at runtime via AppContext.BaseDirectory walk-up so
    // the test works under both `dotnet test` (bin/Debug) and CI containers (bin/Release).
    private const string PlaybookNodeSyncPath =
        "src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts";
    private const string PlaybookTypesPath =
        "src/client/code-pages/PlaybookBuilder/src/types/playbook.ts";
    private const string NodeServicePath =
        "src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs";
    private const string NodeExecutorPath =
        "src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs";

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-3H3.1 — Canvas type strings MUST exist as server switch arms
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CanvasNodeTypes_ExistInServerMapping_NoDrift()
    {
        // Arrange — parse both sources.
        var repoRoot = FindRepoRoot();
        var clientCanvasTypes = ExtractClientCanvasTypes(
            Path.Combine(repoRoot, PlaybookNodeSyncPath));
        var serverCanvasArms = ExtractServerCanvasArms(
            Path.Combine(repoRoot, NodeServicePath));

        // Sanity — neither side should parse empty (would indicate the regex broke after a
        // source-file refactor; better to surface that than silently pass with an empty set).
        clientCanvasTypes.Should().NotBeEmpty(
            "client-side `buildConfigJson()` switch in {0} should contain at least one " +
            "`case 'X':` arm — if this assertion fails, the source file shape changed and " +
            "the ExtractClientCanvasTypes() regex needs to be updated. Inspect the file and " +
            "verify the switch shape, then update the regex literal in this test class.",
            PlaybookNodeSyncPath);
        serverCanvasArms.Should().NotBeEmpty(
            "server-side `MapCanvasTypeToActionType()` switch in {0} should contain at least " +
            "one `\"X\" => ActionType.Y,` arm — if this assertion fails, the source file " +
            "shape changed and the ExtractServerCanvasArms() regex needs to be updated.",
            NodeServicePath);

        // Act — compute the asymmetry. Client ⊆ Server is the binding direction:
        // server MAY have arms the client doesn't expose (e.g., "callWebhook", "parallel",
        // "aiEmbedding", "sendTeamsMessage" are server-only today). The client MUST NOT
        // emit a canvas type the server cannot dispatch.
        var canvasMissingFromServer = clientCanvasTypes.Except(serverCanvasArms).OrderBy(s => s).ToArray();

        // Assert — name the offending canvas types directly so a future drift failure
        // is debuggable in 5 seconds (no spelunking required).
        canvasMissingFromServer.Should().BeEmpty(
            "FR-3H3.1 / AC-H3.1 — every canvas node type emitted by the PlaybookBuilder " +
            "(`buildConfigJson()` switch in {0}) MUST have a matching arm in the server's " +
            "`MapCanvasTypeToActionType()` switch (in {1}). Authoring a playbook with an " +
            "unmapped canvas type would persist a __actionType the server cannot dispatch. " +
            "DRIFT: client emits these canvas types with no server arm: [{2}]. To fix: add " +
            "the matching `\"X\" => ActionType.Y,` arm to MapCanvasTypeToActionType (and a " +
            "MapCanvasTypeToNodeType entry) in {1}, plus the corresponding ActionType enum " +
            "value in {3}.",
            PlaybookNodeSyncPath,
            NodeServicePath,
            string.Join(", ", canvasMissingFromServer),
            NodeExecutorPath);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-3H3.1 (companion) — Client ActionType enum members MUST exist server-side
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClientActionTypeMembers_ExistInServerActionTypeEnum_NoDrift()
    {
        // Arrange — parse both ActionType enums.
        var repoRoot = FindRepoRoot();
        var clientActionTypeMembers = ExtractClientActionTypeMembers(
            Path.Combine(repoRoot, PlaybookTypesPath));
        var serverActionTypeMembers = ExtractServerActionTypeMembers(
            Path.Combine(repoRoot, NodeExecutorPath));

        // Sanity — both enums must parse non-empty.
        clientActionTypeMembers.Should().NotBeEmpty(
            "client-side `enum ActionType` in {0} should contain at least one member — if " +
            "this assertion fails, the enum shape changed and ExtractClientActionTypeMembers() " +
            "needs to be updated.",
            PlaybookTypesPath);
        serverActionTypeMembers.Should().NotBeEmpty(
            "server-side `public enum ActionType` in {0} should contain at least one member.",
            NodeExecutorPath);

        // Act — compute drift. Client ⊆ Server is the binding direction (server may carry
        // executor types the canvas doesn't expose, e.g., QueryDataverse=51, AgentService=60,
        // GroundingVerify=70, LiveFact=80, IndexRetrieve=90, EvidenceSufficiency=100,
        // DeclineToFind=110, ReturnInsightArtifact=120, Sanitization=130, ObservationEmit=140).
        // The client MUST NOT carry an ActionType the server doesn't define.
        var clientOnlyMembers = clientActionTypeMembers
            .Where(kvp => !serverActionTypeMembers.TryGetValue(kvp.Key, out var serverValue) ||
                          serverValue != kvp.Value)
            .ToArray();

        // Assert — name the offending members (including value mismatches, not just missing
        // names — a renumbered slot is just as broken as a missing one).
        clientOnlyMembers.Should().BeEmpty(
            "FR-3H3.1 / AC-H3.1 — every member of the client `ActionType` enum in {0} MUST " +
            "exist in the server `ActionType` enum in {1} with the SAME integer value (the " +
            "value is the wire format stored in `sprk_configjson.__actionType`). DRIFT: these " +
            "client members are missing or value-mismatched on the server: [{2}]. To fix: " +
            "either add the matching server enum value or correct the client value to match " +
            "the server.",
            PlaybookTypesPath,
            NodeExecutorPath,
            string.Join(", ", clientOnlyMembers.Select(kvp =>
                serverActionTypeMembers.TryGetValue(kvp.Key, out var serverValue)
                    ? $"{kvp.Key} (client={kvp.Value}, server={serverValue})"
                    : $"{kvp.Key} (client={kvp.Value}, missing server-side)")));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Source parsers (pure regex — see notes at top of file on approach choice)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extract the set of canvas node type strings emitted by the client's
    /// <c>buildConfigJson()</c> switch in <c>playbookNodeSync.ts</c>. Matches
    /// <c>case 'X':</c> arms with single or double quotes inside the switch body.
    /// </summary>
    private static HashSet<string> ExtractClientCanvasTypes(string playbookNodeSyncPath)
    {
        File.Exists(playbookNodeSyncPath).Should().BeTrue(
            "playbookNodeSync.ts must exist at expected repo-relative path: {0}",
            playbookNodeSyncPath);

        var source = File.ReadAllText(playbookNodeSyncPath);

        // Match `case 'X':` or `case "X":` inside the file. Scoped narrowly enough to
        // avoid grabbing case arms outside `buildConfigJson()`, we slice the source
        // starting at the buildConfigJson body and ending at the next top-level export
        // (`export function`) or `// ---` divider — the file uses dashed dividers between
        // sections so this is a stable boundary.
        const string startMarker = "export function buildConfigJson";
        var startIdx = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIdx < 0) return [];

        // Find the next top-level export OR section divider after the function start.
        // The buildConfigJson() function body ends well before the next `export` keyword
        // appears at column 0; scan for either marker.
        var afterStart = source[startIdx..];
        var endIdx = new[]
        {
            afterStart.IndexOf("\nexport ", StringComparison.Ordinal),
            afterStart.IndexOf("\n// ---", StringComparison.Ordinal),
        }.Where(i => i > 0).DefaultIfEmpty(afterStart.Length).Min();
        var slice = afterStart[..endIdx];

        var caseRegex = new Regex(@"case\s+['""]([a-zA-Z_][a-zA-Z0-9_]*)['""]:\s*", RegexOptions.Compiled);
        return new HashSet<string>(
            caseRegex.Matches(slice).Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Extract the set of canvas node type strings handled by the server's
    /// <c>MapCanvasTypeToActionType()</c> switch in <c>NodeService.cs</c>. Matches
    /// switch-expression arms of the shape <c>"X" => ActionType.Y,</c>.
    /// </summary>
    private static HashSet<string> ExtractServerCanvasArms(string nodeServicePath)
    {
        File.Exists(nodeServicePath).Should().BeTrue(
            "NodeService.cs must exist at expected repo-relative path: {0}",
            nodeServicePath);

        var source = File.ReadAllText(nodeServicePath);

        // Scope to the MapCanvasTypeToActionType method body. The method signature is a
        // stable anchor; the body terminates at the next standalone `};` (closing the
        // switch expression).
        const string startMarker = "MapCanvasTypeToActionType(string canvasType)";
        var startIdx = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIdx < 0) return [];

        var afterStart = source[startIdx..];
        var endIdx = afterStart.IndexOf("};", StringComparison.Ordinal);
        if (endIdx < 0) endIdx = afterStart.Length;
        var slice = afterStart[..endIdx];

        // Match `"X" => ...` arms. C# switch-expression syntax inside the slice.
        var armRegex = new Regex(@"""([a-zA-Z_][a-zA-Z0-9_]*)""\s*=>", RegexOptions.Compiled);
        return new HashSet<string>(
            armRegex.Matches(slice).Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Extract the client-side <c>ActionType</c> enum members + their numeric values from
    /// <c>playbook.ts</c>. Returns a name→value map (case-sensitive on name).
    /// </summary>
    private static Dictionary<string, int> ExtractClientActionTypeMembers(string playbookTypesPath)
    {
        File.Exists(playbookTypesPath).Should().BeTrue(
            "playbook.ts must exist at expected repo-relative path: {0}",
            playbookTypesPath);

        var source = File.ReadAllText(playbookTypesPath);

        // Scope to `export enum ActionType { ... }` block.
        const string startMarker = "export enum ActionType";
        var startIdx = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIdx < 0) return [];

        var afterStart = source[startIdx..];
        var openBrace = afterStart.IndexOf('{');
        if (openBrace < 0) return [];
        var closeBrace = afterStart.IndexOf('}', openBrace);
        if (closeBrace < 0) return [];
        var body = afterStart[(openBrace + 1)..closeBrace];

        // Match `Name = N,` (ignore comments, trailing commas optional on last entry).
        var memberRegex = new Regex(
            @"^\s*([A-Z][a-zA-Z0-9_]*)\s*=\s*(\d+)\s*,?\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        var members = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in memberRegex.Matches(body))
        {
            members[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }
        return members;
    }

    /// <summary>
    /// Extract the server-side <c>ActionType</c> enum members + their numeric values from
    /// <c>INodeExecutor.cs</c>. Returns a name→value map (case-sensitive on name).
    /// </summary>
    private static Dictionary<string, int> ExtractServerActionTypeMembers(string nodeExecutorPath)
    {
        File.Exists(nodeExecutorPath).Should().BeTrue(
            "INodeExecutor.cs must exist at expected repo-relative path: {0}",
            nodeExecutorPath);

        var source = File.ReadAllText(nodeExecutorPath);

        // Scope to `public enum ActionType { ... }`. The closing `}` is at column 0 in this
        // file, so locate the brace pair starting from the enum header.
        const string startMarker = "public enum ActionType";
        var startIdx = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIdx < 0) return [];

        var afterStart = source[startIdx..];
        var openBrace = afterStart.IndexOf('{');
        if (openBrace < 0) return [];

        // Find matching close brace (account for nested braces inside XML doc comments,
        // though there shouldn't be any — we walk balanced depth from openBrace).
        var depth = 0;
        var closeBrace = -1;
        for (var i = openBrace; i < afterStart.Length; i++)
        {
            var ch = afterStart[i];
            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0) { closeBrace = i; break; }
            }
        }
        if (closeBrace < 0) return [];
        var body = afterStart[(openBrace + 1)..closeBrace];

        // Match `Name = N,` (multiline, trailing comma optional).
        var memberRegex = new Regex(
            @"^\s*([A-Z][a-zA-Z0-9_]*)\s*=\s*(\d+)\s*,?\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        var members = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in memberRegex.Matches(body))
        {
            members[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }
        return members;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Repo-root discovery (walks up from AppContext.BaseDirectory until we find the
    // expected source files — works under `dotnet test` from any nested bin directory).
    // ─────────────────────────────────────────────────────────────────────────────

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            // Repo root contains the `src/` and `tests/` top-level directories. Use the
            // playbookNodeSync.ts path as a positive sentinel — if it exists under this
            // candidate root, this is the repo root.
            var candidate = Path.Combine(dir.FullName, PlaybookNodeSyncPath);
            if (File.Exists(candidate)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate repo root from AppContext.BaseDirectory={AppContext.BaseDirectory}. " +
            $"Walked up looking for `{PlaybookNodeSyncPath}` but did not find it. This indicates " +
            "the test project is being run from outside the Spaarke repo tree.");
    }
}
