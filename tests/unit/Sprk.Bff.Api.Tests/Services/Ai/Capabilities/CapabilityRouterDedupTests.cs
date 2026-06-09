using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Capabilities;

/// <summary>
/// R6 task 042 (D-B-09) — CapabilityRouter dedup tests (FR-30).
///
/// Verifies the structural collapse of the R5 Gap A duplicate-fire bug:
///   • CapabilityRouter populates <see cref="CapabilityRoutingResult.SelectedPlaybookId"/>
///     when intent resolves to a SINGLE unambiguous capability with a playbook binding.
///   • The consumer's <see cref="SprkChatAgentFactory.BuildDedupDirective"/> emits a
///     destination-aware system-prompt enrichment for workspace / form-prefill /
///     side-effect destinations, suppressing the chat-agent's parallel inline render.
///   • Chat destination is a no-op (current behavior preserved — single render either way).
///   • NFR-01 conversational primacy is preserved unconditionally (no SelectedPlaybookId
///     when intent is ambiguous, free-form, or unmatched).
///
/// Test coverage matrix:
///   1.  Single confident capability with PlaybookId → SelectedPlaybookId populated (Layer 1).
///   2.  Single confident capability WITHOUT PlaybookId → SelectedPlaybookId null.
///   3.  Ambiguous tie (two equal-scoring capabilities) → SelectedPlaybookId null even when
///       both have PlaybookIds (consumer falls through to conversational default).
///   4.  Uncertain result → SelectedPlaybookId null.
///   5.  Free-form / empty turn → SelectedPlaybookId null (NFR-01 preservation).
///   6.  Layer 3 fallback → SelectedPlaybookId null (broad superset, no single playbook).
///   7.  BuildDedupDirective(Workspace) → non-empty directive naming workspace tab.
///   8.  BuildDedupDirective(FormPrefill) → non-empty directive naming form pre-fill.
///   9.  BuildDedupDirective(SideEffect) → non-empty directive naming background action.
///   10. BuildDedupDirective(Chat) → empty (caller short-circuits; current behavior preserved).
///   11. Refinement / follow-up scenario — second turn's free-form message → SelectedPlaybookId
///       null on the follow-up turn (NFR-01 preservation across turns).
///   12. CapabilityRoutingResult.Confident factory accepts selectedPlaybookId parameter.
///   13. Directive references the literal `invoke_playbook` tool name (binding to the
///       data-driven tool surface — task 023 / D-A-15).
///   14. Directive explicitly preserves NFR-01 by instructing single-sentence
///       acknowledgment (not silence).
///   15. Tied capabilities with one having PlaybookId and one without → SelectedPlaybookId
///       null (single == 1 contract).
/// </summary>
public sealed class CapabilityRouterDedupTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CapabilityManifestEntry MakeEntry(
        string name,
        string[] keywordHints,
        Guid? playbookId = null,
        bool isEnabled = true)
    {
        return new CapabilityManifestEntry(
            CapabilityName: name,
            Description: $"Description for {name}",
            KeywordHints: keywordHints,
            PlaybookId: playbookId,
            ToolNames: [],
            IsEnabled: isEnabled,
            TenantRestrictions: []);
    }

    private static CapabilityRouter BuildRouter(
        IEnumerable<CapabilityManifestEntry> entries,
        CapabilityRouterOptions? options = null)
    {
        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        manifest.Refresh(entries.ToList());

        var opts = Options.Create(options ?? new CapabilityRouterOptions());
        return new CapabilityRouter(manifest, opts, NullLogger<CapabilityRouter>.Instance);
    }

    // ── Test 1: Single confident capability with PlaybookId → SelectedPlaybookId populated

    [Fact]
    public void RouteSync_PopulatesSelectedPlaybookId_WhenSingleConfidentCapabilityHasPlaybook()
    {
        // Arrange — one dominant capability with a PlaybookId (mirrors summarize-document-for-chat@v1).
        var playbookId = Guid.Parse("44285d15-1360-f111-ab0b-70a8a59455f4");
        var summarize = MakeEntry(
            "summarize_document",
            ["summarize", "tldr", "summary"],
            playbookId: playbookId);
        var unrelated = MakeEntry("legal_research", ["case law", "court"]);

        var router = BuildRouter([summarize, unrelated]);

        // Act — message dominates the summarize capability.
        var result = router.RouteSync("summarize this document tldr", activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeTrue("dominant keyword match should exceed 0.8 threshold");
        result.SelectedCapabilities.Should().ContainSingle().Which.Should().Be("summarize_document");
        result.SelectedPlaybookId.Should().Be(playbookId,
            "single confident capability with a non-null PlaybookId should propagate the GUID for dedup");
    }

    // ── Test 2: Single confident capability WITHOUT PlaybookId → null

    [Fact]
    public void RouteSync_SelectedPlaybookIdIsNull_WhenCapabilityHasNoPlaybook()
    {
        // Arrange — single dominant capability but no PlaybookId (global capability).
        var globalCap = MakeEntry("global_action", ["unique_keyword"], playbookId: null);
        var router = BuildRouter([globalCap]);

        // Act
        var result = router.RouteSync("trigger unique_keyword now", activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().ContainSingle().Which.Should().Be("global_action");
        result.SelectedPlaybookId.Should().BeNull(
            "a global capability without a playbook binding should NOT carry a SelectedPlaybookId — " +
            "the consumer falls through to current conversational behavior");
    }

    // ── Test 3: Tied capabilities → null even when both have PlaybookIds

    [Fact]
    public void RouteSync_SelectedPlaybookIdIsNull_OnTiedCapabilitiesEvenWithPlaybookIds()
    {
        // Arrange — two capabilities with PlaybookIds that share the same scoring keywords.
        var capA = MakeEntry(
            "summarize_chat",
            ["summarize", "document"],
            playbookId: Guid.NewGuid());
        var capB = MakeEntry(
            "summarize_workspace",
            ["summarize", "document"],
            playbookId: Guid.NewGuid());

        var router = BuildRouter([capA, capB]);

        // Act — "summarize document" hits BOTH capabilities at 2/2 each.
        var result = router.RouteSync("summarize document", activePlaybookName: null);

        // Assert
        // Equal score → confidence ≈ 0.5 → Uncertain (not Confident).
        // BUT even if BOTH had also been Confident (e.g., via playbook bias), the dedup
        // contract requires SelectedPlaybookId == null on ties — ambiguity is NOT a
        // confident playbook resolution.
        result.SelectedPlaybookId.Should().BeNull(
            "ambiguous tie between two playbook-bound capabilities must NOT pick a " +
            "playbook — consumer falls through to NFR-01 conversational primacy");
    }

    // ── Test 4: Uncertain result → null

    [Fact]
    public void RouteSync_SelectedPlaybookIdIsNull_WhenResultIsUncertain()
    {
        // Arrange — single capability with a PlaybookId; message has no matching keywords.
        var cap = MakeEntry("legal_research", ["case law"], playbookId: Guid.NewGuid());
        var router = BuildRouter([cap]);

        // Act — message has no relevant keywords.
        var result = router.RouteSync("what is the weather like today", activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeFalse();
        result.SelectedPlaybookId.Should().BeNull(
            "uncertain routing must NOT propagate any playbook ID — only confident " +
            "single-winner results carry a SelectedPlaybookId");
    }

    // ── Test 5: Free-form / empty turn → null (NFR-01 preservation)

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello, can you help me with something general")]
    public void RouteSync_SelectedPlaybookIdIsNull_OnFreeFormOrEmptyMessage(string? message)
    {
        // Arrange — playbook-bound capability exists, but free-form message doesn't hit it.
        var cap = MakeEntry("summarize_document", ["summarize", "tldr"], playbookId: Guid.NewGuid());
        var router = BuildRouter([cap]);

        // Act
        var result = router.RouteSync(message!, activePlaybookName: null);

        // Assert
        result.SelectedPlaybookId.Should().BeNull(
            "NFR-01 conversational primacy: free-form / empty turns must NOT trigger the " +
            "dedup directive — the chat-agent responds conversationally as normal");
    }

    // ── Test 6: Layer 3 fallback → null

    [Fact]
    public void CapabilityRoutingResult_Fallback_HasNullSelectedPlaybookId()
    {
        // Arrange & Act — Layer 3 factory method.
        var result = CapabilityRoutingResult.Fallback(
            fallbackCapabilityNames: [],
            selectedToolNames: ["SearchDocuments", "RefineText"],
            latencyMs: 1);

        // Assert
        result.IsConfident.Should().BeFalse();
        result.Layer.Should().Be(3);
        result.SelectedPlaybookId.Should().BeNull(
            "Layer 3 fallback returns the broad tool superset — there is no single playbook " +
            "to bind, so SelectedPlaybookId must always be null");
    }

    // ── Test 7: BuildDedupDirective(Workspace) → non-empty, names workspace tab

    [Fact]
    public void BuildDedupDirective_Workspace_EmitsNonEmptyDirectiveNamingWorkspaceTab()
    {
        // Act
        var directive = SprkChatAgentFactory.BuildDedupDirective(NodeDestination.Workspace);

        // Assert
        directive.Should().NotBeNullOrEmpty();
        directive.Should().Contain("workspace",
            "the directive should reference the workspace surface so the LLM understands " +
            "where the playbook output will render");
        directive.Should().Contain("`invoke_playbook`",
            "the directive should name the literal tool to which it applies");
    }

    // ── Test 8: BuildDedupDirective(FormPrefill) → non-empty, names form

    [Fact]
    public void BuildDedupDirective_FormPrefill_EmitsNonEmptyDirectiveNamingForm()
    {
        // Act
        var directive = SprkChatAgentFactory.BuildDedupDirective(NodeDestination.FormPrefill);

        // Assert
        directive.Should().NotBeNullOrEmpty();
        directive.Should().Contain("form",
            "form-prefill directive should name the form surface");
        directive.Should().Contain("`invoke_playbook`",
            "directive should name the literal tool");
    }

    // ── Test 9: BuildDedupDirective(SideEffect) → non-empty, names background action

    [Fact]
    public void BuildDedupDirective_SideEffect_EmitsNonEmptyDirectiveNamingBackground()
    {
        // Act
        var directive = SprkChatAgentFactory.BuildDedupDirective(NodeDestination.SideEffect);

        // Assert
        directive.Should().NotBeNullOrEmpty();
        directive.Should().Contain("system",
            "side-effect directive should reference the system / background surface " +
            "(no user-visible render)");
        directive.Should().Contain("`invoke_playbook`");
    }

    // ── Test 10: BuildDedupDirective(Chat) → empty

    [Fact]
    public void BuildDedupDirective_Chat_EmitsEmptyDirective()
    {
        // Act
        var directive = SprkChatAgentFactory.BuildDedupDirective(NodeDestination.Chat);

        // Assert
        directive.Should().BeEmpty(
            "chat destination is the default rendering surface — no dedup directive needed; " +
            "current behavior preserved (single render in chat)");
    }

    // ── Test 11: Refinement / follow-up — second turn unaffected (NFR-01 preservation)

    [Fact]
    public void RouteSync_FreeFormFollowUp_DoesNotCarrySelectedPlaybookId()
    {
        // Arrange — same router; first turn would resolve to a playbook, second turn is free-form.
        var cap = MakeEntry("summarize_document", ["summarize", "tldr"], playbookId: Guid.NewGuid());
        var router = BuildRouter([cap]);

        // Act — first turn matches the capability.
        var firstTurn = router.RouteSync("summarize this tldr", activePlaybookName: null);
        firstTurn.SelectedPlaybookId.Should().NotBeNull(
            "sanity: first turn should resolve to the playbook for the dedup directive");

        // Second turn is free-form / refinement.
        var secondTurn = router.RouteSync("can you make it shorter?", activePlaybookName: null);

        // Assert — second turn must be evaluated independently and not carry the previous
        // turn's playbook binding.
        secondTurn.SelectedPlaybookId.Should().BeNull(
            "NFR-01 conversational primacy: refinement / follow-up turns must NOT inherit " +
            "the previous turn's SelectedPlaybookId — each turn is evaluated independently");
    }

    // ── Test 12: CapabilityRoutingResult.Confident factory accepts selectedPlaybookId

    [Fact]
    public void CapabilityRoutingResult_Confident_AcceptsSelectedPlaybookIdParameter()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var result = CapabilityRoutingResult.Confident(
            ["cap_a"],
            confidence: 0.95,
            layer: 1,
            latencyMs: 3,
            selectedPlaybookId: playbookId);

        // Assert
        result.SelectedPlaybookId.Should().Be(playbookId);
        result.IsConfident.Should().BeTrue();
    }

    [Fact]
    public void CapabilityRoutingResult_Confident_SelectedPlaybookIdDefaultsToNull()
    {
        // Act — call factory WITHOUT selectedPlaybookId (backward-compatible existing call sites).
        var result = CapabilityRoutingResult.Confident(["cap_a"], 0.95, layer: 1, latencyMs: 3);

        // Assert — backward compat: existing call sites that don't pass the new param see null.
        result.SelectedPlaybookId.Should().BeNull(
            "new SelectedPlaybookId parameter must default to null for backward compatibility " +
            "with existing CapabilityRoutingResult.Confident call sites (test code, Layer 3 paths)");
    }

    // ── Test 13: Directive references `invoke_playbook` tool name

    [Theory]
    [InlineData(NodeDestination.Workspace)]
    [InlineData(NodeDestination.FormPrefill)]
    [InlineData(NodeDestination.SideEffect)]
    public void BuildDedupDirective_NonChatDestinations_NameInvokePlaybookTool(NodeDestination destination)
    {
        // Act
        var directive = SprkChatAgentFactory.BuildDedupDirective(destination);

        // Assert — the directive must name the literal tool identifier so the LLM
        // knows EXACTLY which tool's response surface the directive applies to.
        // Task 023 (D-A-15) made `invoke_playbook` the canonical generic dispatcher;
        // any future renaming requires updating this contract in lock-step.
        directive.Should().Contain("`invoke_playbook`",
            "directive must reference the canonical `invoke_playbook` tool by name " +
            "(task 023 / D-A-15 binding) so the LLM scopes the dedup to that tool only");
    }

    // ── Test 14: Directive explicitly preserves NFR-01 (single-sentence acknowledgment)

    [Theory]
    [InlineData(NodeDestination.Workspace)]
    [InlineData(NodeDestination.FormPrefill)]
    [InlineData(NodeDestination.SideEffect)]
    public void BuildDedupDirective_NonChatDestinations_PreservesNFR01ConversationalPrimacy(
        NodeDestination destination)
    {
        // Act
        var directive = SprkChatAgentFactory.BuildDedupDirective(destination);

        // Assert — NFR-01 binding: the directive must NOT silence the LLM. It must
        // instruct a single-sentence acknowledgment so the chat surface remains
        // conversational; only the parallel inline analysis content is suppressed.
        directive.Should().Contain("SINGLE-SENTENCE acknowledgment",
            "NFR-01 conversational primacy: directive MUST instruct a single-sentence " +
            "acknowledgment (not silence) — the LLM still acknowledges the user's intent");

        // Also: the directive must explicitly state follow-up turns are unaffected,
        // so the LLM doesn't continue suppressing on subsequent refinement turns.
        directive.Should().Contain("follow-up",
            "directive must clarify that subsequent turns are unaffected so the LLM " +
            "doesn't continue silencing on refinement/comparison turns (NFR-01 preservation)");
    }

    // ── Test 15: Tied capabilities (one with PlaybookId, one without) → null

    [Fact]
    public void RouteSync_SelectedPlaybookIdIsNull_WhenTiedAndOnlyOneCapabilityHasPlaybook()
    {
        // Arrange — two capabilities tie at top score; only one has a PlaybookId.
        var capWithPlaybook = MakeEntry(
            "with_playbook",
            ["shared_keyword"],
            playbookId: Guid.NewGuid());
        var capWithoutPlaybook = MakeEntry(
            "without_playbook",
            ["shared_keyword"]);

        var router = BuildRouter([capWithPlaybook, capWithoutPlaybook]);

        // Act
        var result = router.RouteSync("shared_keyword fires here", activePlaybookName: null);

        // Assert — tie → confidence ≈ 0.5 → Uncertain by default. The dedup contract
        // additionally requires that even if a configuration made this Confident (e.g.,
        // playbook bias on a tied entry), the single-winner rule (Length == 1) gates
        // SelectedPlaybookId. Verify the multi-capability path keeps SelectedPlaybookId
        // null regardless of confidence.
        result.SelectedPlaybookId.Should().BeNull(
            "ambiguity-on-ties contract: SelectedPlaybookId is populated ONLY when " +
            "exactly one capability wins (Length == 1) AND it has a PlaybookId");
    }

    // ── Test 16: Real-world scenario — summarize-document-for-chat → chat destination is no-op

    [Fact]
    public void EndToEnd_ChatDestination_NoDirectiveApplied()
    {
        // This test documents the chat destination behavior:
        //   1. Router resolves to summarize-document-for-chat playbook (chat destination)
        //   2. SelectedPlaybookId is populated
        //   3. BuildDedupDirective(Chat) returns empty
        //   4. NO system prompt enrichment → current chat behavior preserved
        var chatPlaybookId = Guid.Parse("44285d15-1360-f111-ab0b-70a8a59455f4");
        var cap = MakeEntry(
            "summarize_chat",
            ["summarize", "tldr", "summary"],
            playbookId: chatPlaybookId);
        var router = BuildRouter([cap]);

        // Routing resolves to the chat playbook.
        var result = router.RouteSync("summarize this tldr", activePlaybookName: null);
        result.IsConfident.Should().BeTrue();
        result.SelectedPlaybookId.Should().Be(chatPlaybookId);

        // Directive for chat destination is empty (caller short-circuits).
        var directive = SprkChatAgentFactory.BuildDedupDirective(NodeDestination.Chat);
        directive.Should().BeEmpty(
            "chat destination produces no directive — chat-agent renders inline as the " +
            "single render surface; no parallel path A vs path B problem");
    }

    // ── Test 17: Real-world scenario — summarize-document-for-workspace → directive applied

    [Fact]
    public void EndToEnd_WorkspaceDestination_DirectiveApplied()
    {
        // This test documents the workspace destination behavior (R5 SC-18 fix):
        //   1. Router resolves to summarize-document-for-workspace playbook (workspace destination)
        //   2. SelectedPlaybookId populated
        //   3. BuildDedupDirective(Workspace) returns the suppression directive
        //   4. System prompt enriched → chat-agent emits ONLY a single-sentence ack
        //   5. Playbook renders to workspace tab via StructuredOutputStreamWidget
        //   6. Total: ONE render (workspace) per user intent — R5 Gap A eliminated
        var workspacePlaybookId = Guid.NewGuid();
        var cap = MakeEntry(
            "summarize_workspace",
            ["workspace_summary_keyword"],
            playbookId: workspacePlaybookId);
        var router = BuildRouter([cap]);

        // Routing resolves to the workspace playbook.
        var result = router.RouteSync("workspace_summary_keyword", activePlaybookName: null);
        result.IsConfident.Should().BeTrue();
        result.SelectedPlaybookId.Should().Be(workspacePlaybookId);

        // Directive for workspace destination is non-empty and suppresses inline content.
        var directive = SprkChatAgentFactory.BuildDedupDirective(NodeDestination.Workspace);
        directive.Should().NotBeEmpty();
        directive.Should().Contain("Do NOT emit the analysis content inline",
            "the directive must explicitly forbid inline analysis content so the chat-agent " +
            "does not produce a parallel render alongside the workspace widget");
    }

    // ── Test 18: Real-world scenario — matter pre-fill → directive applied (NFR-07 path)

    [Fact]
    public void EndToEnd_FormPrefillDestination_DirectiveAppliedWithoutTouchingPreFillPath()
    {
        // This test documents the form-prefill behavior:
        //   1. Router resolves to matter-prefill playbook (form-prefill destination)
        //   2. SelectedPlaybookId populated
        //   3. BuildDedupDirective(FormPrefill) returns directive
        //   4. Pre-fill flow (IWorkspacePrefillAi, MatterPreFillService) is UNCHANGED — NFR-07 binding
        //   5. Chat-agent emits single-sentence ack ("Pre-filling the form...")
        var matterPreFillId = Guid.NewGuid();
        var cap = MakeEntry(
            "matter_prefill",
            ["prefill_matter_keyword"],
            playbookId: matterPreFillId);
        var router = BuildRouter([cap]);

        var result = router.RouteSync("prefill_matter_keyword", activePlaybookName: null);
        result.IsConfident.Should().BeTrue();
        result.SelectedPlaybookId.Should().Be(matterPreFillId);

        var directive = SprkChatAgentFactory.BuildDedupDirective(NodeDestination.FormPrefill);
        directive.Should().NotBeEmpty();
        directive.Should().Contain("form");
    }
}
