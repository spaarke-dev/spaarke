using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Phase 5R Wave 5-E task 118R — migration-shape regression tests for the multi-node
/// migration target file <c>infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope</b>: structural assertions on the deployment JSON file authored in 118R. This is
/// NOT an end-to-end orchestrator test — that requires the Dataverse choice column to be
/// extended with <c>DeliverComposite (100000004)</c> and the playbook to be applied, neither
/// of which is in 118R scope per task POML constraints (data-only migration; the Dataverse
/// metadata gap is filed as a follow-up in <c>notes/handoffs/118R-migration-evidence.md</c>).
/// </para>
/// <para>
/// <b>What this regression guards</b>:
/// <list type="bullet">
/// <item>The migration file declares exactly 5 nodes (4 Action + 1 DeliverComposite).</item>
/// <item>Each composite section's <c>inputVariable</c> resolves to an existing Action node's
/// <c>outputVariable</c> — no dangling references.</item>
/// <item>Section names match the production-bound outputSchema field set
/// (tldr / summary / keywords / entities) per NFR-02.</item>
/// <item>Section <i>order</i> is preserved (load-bearing per task 006 spike + spec FR-02 — the
/// streaming layer emits in declaration order; the workspace widget renders in
/// declaration order).</item>
/// <item>All four Action nodes reuse the existing <c>SUM-CHAT@v1</c> action (no new actions
/// seeded per the R6 Q5 RE-SHAPED principle).</item>
/// <item>The playbook slug (<c>summarize-document-for-workspace@v1</c>) is unchanged so the
/// Phase B vector match continues to route to this playbook.</item>
/// </list>
/// </para>
/// <para>
/// <b>Backward-compat invariant verified</b>: this file's existence does NOT modify the chat
/// sibling deployment file <c>summarize-document-for-chat.playbook.json</c> — that file is
/// out of scope per FR-58 / ADR-037. Verified separately via
/// <see cref="ChatSibling_DeploymentFile_UnchangedAfterMigration_BindingPerFr58"/>.
/// </para>
/// </remarks>
public sealed class SummarizeWorkspaceMultinodeMigrationTests
{
    private const string MigrationFileRelativePath =
        "infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json";

    private const string ChatSiblingFileRelativePath =
        "src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-chat.playbook.json";

    private const string ExpectedPlaybookSlug = "summarize-document-for-workspace@v1";
    private const string ExpectedSharedActionCode = "SUM-CHAT@v1";

    // The 4 sections that StructuredOutputStreamWidget consumes — preserving NFR-02 production-
    // bound playbook semantics. Order is load-bearing per task 006 spike.
    private static readonly string[] ExpectedSectionsInOrder = { "tldr", "summary", "keywords", "entities" };

    /// <summary>
    /// Loads the migration file, walking up from the test bin directory to the repo root.
    /// Uses the same pattern as <c>InsightsNodesIntegrationTests</c> for file-resolution.
    /// </summary>
    private static JsonDocument LoadMigrationFile(string relativePath = MigrationFileRelativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                var json = File.ReadAllText(candidate);
                return JsonDocument.Parse(json);
            }
            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {relativePath} walking up from {AppContext.BaseDirectory}. " +
            "Test depends on the file existing at the repo-relative path.");
    }

    [Fact]
    public void MigrationFile_Exists_AndIsValidJson()
    {
        using var doc = LoadMigrationFile();
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void PlaybookSlug_IsUnchanged_PreservingPhaseBVectorMatch()
    {
        using var doc = LoadMigrationFile();
        var playbook = doc.RootElement.GetProperty("playbook");
        playbook.GetProperty("name").GetString().Should().Be(ExpectedPlaybookSlug,
            "Phase B vector match (PlaybookDispatcher.RunPhaseBVectorMatchAsync) routes " +
            "'summarize this document' queries to this playbook via embedded metadata; " +
            "changing the slug would break vector match");
    }

    [Fact]
    public void Nodes_ContainExactlyFourActionNodesPlusOneCompositeNode()
    {
        using var doc = LoadMigrationFile();
        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();

        nodes.Should().HaveCount(5,
            "migration should produce 4 Action nodes (one per outputSchema section) + 1 " +
            "terminal DeliverComposite node — total 5");

        var actionNodes = nodes.Where(n =>
            n.GetProperty("nodeType").GetString() == "AIAnalysis").ToList();
        actionNodes.Should().HaveCount(4,
            "exactly 4 AI Analysis Action nodes — one per outputSchema field");

        var compositeNodes = nodes.Where(n =>
            n.GetProperty("nodeType").GetString() == "DeliverComposite").ToList();
        compositeNodes.Should().HaveCount(1,
            "exactly 1 terminal DeliverComposite node aggregating the 4 Action outputs");
    }

    [Fact]
    public void ActionNodes_AllReuseExistingSumChatV1Action_NoNewActionsSeeded()
    {
        using var doc = LoadMigrationFile();

        // Per R6 Q5 RE-SHAPED principle: outputSchema is intrinsic to the action; same action
        // can be invoked N times with per-node focus hints to produce different sections.
        doc.RootElement.GetProperty("actions").GetArrayLength().Should().Be(0,
            "no new actions seeded — all four Action nodes reuse existing SUM-CHAT@v1");

        var actionNodes = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Where(n => n.GetProperty("nodeType").GetString() == "AIAnalysis")
            .ToList();

        foreach (var node in actionNodes)
        {
            node.GetProperty("actionCode").GetString().Should().Be(ExpectedSharedActionCode,
                "every Action node must reference the existing SUM-CHAT@v1 action by " +
                "actionCode FK (no new action seeded)");
            node.GetProperty("actionType").GetInt32().Should().Be(0,
                "ActionType.AiAnalysis = 0; handled by existing AiAnalysisNodeExecutor");
        }
    }

    [Fact]
    public void CompositeNode_DeclaresFourSectionsInDeclarationOrder_PreservingNfr02()
    {
        using var doc = LoadMigrationFile();

        var composite = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("nodeType").GetString() == "DeliverComposite");

        composite.GetProperty("actionType").GetInt32().Should().Be(42,
            "ActionType.DeliverComposite = 42; handled by DeliverCompositeNodeExecutor");

        var configJson = composite.GetProperty("configJson");
        configJson.GetProperty("destination").GetString().Should().Be("workspace");
        configJson.GetProperty("widgetType").GetString().Should().Be("structured-output-stream",
            "registry key from register-structured-output-stream-widget.ts:30 unchanged " +
            "from R6 task 033 — preserves NFR-02 widget render parity");

        var sections = configJson.GetProperty("sections").EnumerateArray()
            .Select(s => s.GetProperty("sectionName").GetString())
            .ToArray();

        sections.Should().Equal(ExpectedSectionsInOrder,
            "section order is load-bearing per task 006 spike + spec FR-02 — TL;DR must " +
            "be emitted FIRST so the workspace pane populates within ~300-500ms");
    }

    [Fact]
    public void EveryCompositeSection_InputVariable_ResolvesToActionNodeOutputVariable()
    {
        // The DeliverCompositeNodeExecutor (line 154-156) reads
        // context.GetPreviousOutput(spec.InputVariable) to find each section's content. A
        // dangling inputVariable would cause the section to be silently dropped (FR-52: partial
        // composite is valid) — but for THIS migration, every section MUST resolve.
        using var doc = LoadMigrationFile();

        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();

        var actionOutputVariables = nodes
            .Where(n => n.GetProperty("nodeType").GetString() == "AIAnalysis")
            .Select(n => n.GetProperty("outputVariable").GetString())
            .ToHashSet();

        var composite = nodes.Single(n => n.GetProperty("nodeType").GetString() == "DeliverComposite");
        var sections = composite.GetProperty("configJson").GetProperty("sections").EnumerateArray();

        foreach (var section in sections)
        {
            var inputVariable = section.GetProperty("inputVariable").GetString();
            var sectionName = section.GetProperty("sectionName").GetString();

            actionOutputVariables.Should().Contain(inputVariable,
                $"composite section '{sectionName}' references inputVariable '{inputVariable}' " +
                $"which must match an upstream Action node's outputVariable; otherwise the " +
                $"section is silently dropped per DeliverCompositeNodeExecutor FR-52 semantics");
        }
    }

    [Fact]
    public void CompositeNode_DependsOnAllFourActionNodes_ForOrchestrationCorrectness()
    {
        using var doc = LoadMigrationFile();

        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        var actionNames = nodes
            .Where(n => n.GetProperty("nodeType").GetString() == "AIAnalysis")
            .Select(n => n.GetProperty("name").GetString())
            .ToArray();

        var composite = nodes.Single(n => n.GetProperty("nodeType").GetString() == "DeliverComposite");
        var dependsOn = composite.GetProperty("dependsOn").EnumerateArray()
            .Select(d => d.GetString())
            .ToArray();

        dependsOn.Should().BeEquivalentTo(actionNames,
            "DeliverComposite must depend on EVERY Action node so the orchestrator runs them " +
            "before invoking the composite — otherwise PreviousOutputs is empty and the " +
            "composite emits zero sections");
    }

    [Fact]
    public void ActionNodes_EachCarryPerSectionFocusHint_DirectsSumChatPromptToOneSection()
    {
        // Each Action node invokes SUM-CHAT@v1 with templateParameters.focus = section name —
        // directs the action's JPS prompt to produce ONLY that section's content (per the
        // R6 Q5 RE-SHAPED principle: same action, different per-node config). This is what
        // makes "4 Action nodes share 1 action" work without prompt redundancy.
        using var doc = LoadMigrationFile();

        var actionNodes = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Where(n => n.GetProperty("nodeType").GetString() == "AIAnalysis")
            .ToList();

        foreach (var node in actionNodes)
        {
            var config = node.GetProperty("configJson");
            config.TryGetProperty("templateParameters", out var templateParams).Should().BeTrue(
                $"Action node '{node.GetProperty("name").GetString()}' must declare " +
                "templateParameters.focus to direct SUM-CHAT@v1 to one section");

            var focus = templateParams.GetProperty("focus").GetString();
            ExpectedSectionsInOrder.Should().Contain(focus!,
                $"focus hint '{focus}' must match one of the four production sections " +
                "(tldr/summary/keywords/entities) — NFR-02 production-bound preservation");
        }
    }

    [Fact]
    public void ChatSibling_DeploymentFile_UnchangedAfterMigration_BindingPerFr58()
    {
        // FR-58 / ADR-037 binding: chat sibling stays single-action. This test asserts the
        // chat sibling's deployment file still exists with its single-node shape — guards
        // against accidental migration to multi-node.
        using var doc = LoadMigrationFile(ChatSiblingFileRelativePath);

        var playbook = doc.RootElement.GetProperty("playbook");
        playbook.GetProperty("name").GetString().Should().Be("summarize-document-for-chat@v1");

        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        nodes.Should().HaveCount(1,
            "chat sibling MUST remain single-action per FR-58 / ADR-037; per-section " +
            "streaming offers no UX benefit for the chat surface");

        nodes.Single().GetProperty("nodeType").GetString().Should().Be("AIAnalysis",
            "chat sibling's single node must remain AIAnalysis — NOT migrated to DeliverComposite");
    }
}
