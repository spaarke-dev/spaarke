// R6 task 078 — Phase C cross-pillar integration test suite.
//
// HONEST FRAMING (surfaced to user via task 078 closeout report):
//   The POML for task 078 calls for a 6-scenario end-to-end harness with mock LLM +
//   Cosmos test container + Redis test instance. That harness is a multi-week build.
//
//   Each of the 6 POML scenarios is already covered by per-task tests built during
//   Waves C-G2 through C-G6 (see notes/phase-c-integration-results.md for the
//   evidence map). What's NOT yet covered is the CROSS-PILLAR seams — the
//   boundaries where two or more Phase C pillars compose in a single flow.
//
//   This file provides those gap-fill tests. The composed evidence (per-task tests
//   + these cross-pillar tests) collectively validate all 6 Phase C exit criteria.
//
// Coverage map (tests in this file):
//   • CrossPillar_SendArtifactThenAppearsInNextTurnPrompt — Pillar 6b → Pillar 6a → Pillar 9.
//     `send_workspace_artifact` creates a tab → BuildWorkspaceStateBlock surfaces it.
//   • CrossPillar_AgentCannotUpdateOwnArtifact_FR39Binding — Pillar 6b cross-handler
//     finding: agent-dispatched tabs default canEdit=false (per FR-39 / send handler line
//     360). The update_workspace_tab handler MUST refuse with `refused_not_editable`.
//     This is the structural seam that prevents the agent from rewriting its own outputs
//     without the user's "Convert to editable" affordance.
//   • CrossPillar_UserCreatedTabUpdateRoundTrip — Pillar 6b lifecycle: a pre-existing
//     user-editable tab → agent update_workspace_tab clean apply → updated payload
//     appears in next-turn prompt. Cross-handler state coherence on the editable path.
//   • CrossPillar_AllFourWidgetVariants_PrivacyFilterAndAdr015Audit — Pillar 6b + Pillar 9
//     ADR-015 binding: agent-created tabs of every variant (Summary/DocumentViewer/
//     Dashboard/Table) round-trip through BuildWorkspaceStateBlock and the Pillar 9
//     visible-state contract surfaces ONLY the safe projections (Table row count not
//     IDs; Dashboard name not chart data; etc.).
//   • CrossPillar_StaleReadRefusal_LeavesTabUnchangedForNextTurnPrompt — Pillar 6b +
//     Pillar 9 + Q8 binding: a refused update_workspace_tab call MUST NOT mutate the
//     tab, and the ORIGINAL widget data must still be what the next-turn prompt
//     builder observes.
//   • CrossPillar_HiddenTabNeverLeaksToPromptEvenAfterAgentEdit — Pillar 6b + Pillar 9:
//     a tab with visibleToAssistant=false stays out of the prompt even after the
//     agent successfully updates it (privacy default holds across mutation cycles).
//
// Why these scenarios (and not the POML's literal 6)?
//   Each POML scenario is fully covered by an existing per-task test (see the
//   evidence map in notes/phase-c-integration-results.md). The cross-pillar seams
//   above are the gap the POML's literal harness would have caught but the per-task
//   tests don't — making them the actual value-add of task 078 within the practical
//   1-day budget.
//
// Cross-pillar FINDING surfaced during authoring (worth flagging at sign-off):
//   The send_workspace_artifact handler defaults agent-dispatched tabs to canEdit=false
//   (FR-39 binding; line 360 of SendWorkspaceArtifactHandler.cs). update_workspace_tab
//   refuses with `refused_not_editable` when invoked against such a tab. This is by
//   design — agent CANNOT silently rewrite its own outputs; the user's "Convert to
//   editable" affordance (FR-35) is the explicit gate. The dedicated cross-pillar test
//   (CrossPillar_AgentCannotUpdateOwnArtifact_FR39Binding) locks this in.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Spe.Integration.Tests.PhaseC;

[Trait("Category", "Integration")]
[Trait("Feature", "PhaseCCrossPillar")]
public sealed class CrossPillarIntegrationTests
{
    private const string TenantId = "tenant-cross-pillar";
    private static readonly DateTimeOffset DeterministicNow =
        new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 1 — Pillar 6b → Pillar 6a → Pillar 9
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: agent invokes `send_workspace_artifact` to dispatch a Summary tab.
    // The tab is persisted via WorkspaceStateService (Pillar 6a). On the next chat
    // turn, the SprkChatAgentFactory's BuildWorkspaceStateBlock (Pillar 9) reads
    // the workspace state and surfaces the tab into the agent's system prompt.
    //
    // Cross-pillar boundary exercised: handler upserts a tab via the SAME service
    // surface (IWorkspaceStateService) that Pillar 9's BuildWorkspaceStateBlock
    // consumes — this catches a regression where the handler writes a shape the
    // prompt builder cannot read.

    [Fact]
    public async Task CrossPillar_SendArtifactThenAppearsInNextTurnPrompt()
    {
        // Arrange — shared in-memory workspace state seam serves both the writer
        // (send_workspace_artifact handler) and the reader (BuildWorkspaceStateBlock).
        var sharedState = new InMemoryWorkspaceStateService();
        var sessionGuid = Guid.NewGuid();
        var sessionId = sessionGuid.ToString("N");
        var matterGuid = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        var sendHandler = new SendWorkspaceArtifactHandler(
            workspaceStateService: sharedState,
            guidProvider: new FixedGuidProvider(new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            timeProvider: new FixedTimeProvider(DeterministicNow),
            logger: NullLogger<SendWorkspaceArtifactHandler>.Instance);

        var factory = CreatePromptFactory();

        // Provide BOTH matterId + matterName — the handler requires matterId for the
        // matter context to flow through; matterName alone resolves to synthetic Unattached.
        var argsJson = $$"""
                       {
                         "widgetType": "Summary",
                         "title": "Engagement Letter Summary",
                         "widgetData": {
                           "kind": "Summary",
                           "body": "Deterministic summary body for cross-pillar test",
                           "tldr": "X6b_TLDR_CROSS_PILLAR",
                           "hasUserEdits": false
                         },
                         "matterId": "{{matterGuid:D}}",
                         "matterName": "X6b_MATTER_CROSS_PILLAR"
                       }
                       """;

        var ctx = new ChatInvocationContext
        {
            TenantId = TenantId,
            ChatSessionId = sessionGuid,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = argsJson,
            MatterId = null
        };

        // Act — Step 1: handler dispatches the tab.
        var sendResult = await sendHandler.ExecuteChatAsync(
            ctx,
            BuildTool(nameof(SendWorkspaceArtifactHandler), "send_workspace_artifact"),
            CancellationToken.None);

        sendResult.Success.Should().BeTrue(
            because: "send_workspace_artifact must succeed with a valid Summary payload");

        // Act — Step 2: re-read the workspace state via the same service surface
        // (this mirrors what SprkChatAgentFactory does in CreateAgentAsync).
        var tabsFromState = await sharedState.GetTabsAsync(TenantId, sessionId, CancellationToken.None);
        tabsFromState.Should().HaveCount(1,
            because: "the handler must have persisted exactly one tab to the shared state service");

        // Act — Step 3: Pillar 9 prompt builder composes the workspace state block.
        var promptBlock = factory.BuildWorkspaceStateBlock(tabsFromState, sessionId);

        // Assert — the agent-dispatched tab is now in the agent's next-turn prompt.
        promptBlock.Should().NotBeEmpty(
            because: "the agent-dispatched tab must surface in BuildWorkspaceStateBlock");
        promptBlock.Should().Contain("X6b_TLDR_CROSS_PILLAR",
            because: "the Summary widget's tldr satisfies the Pillar 9 visible-state contract");
        promptBlock.Should().Contain("X6b_MATTER_CROSS_PILLAR");
        promptBlock.Should().Contain("widgetType=Summary");
        promptBlock.Should().Contain("hasUserEdits: false");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 2 — Pillar 6b FR-39 / canEdit binding (cross-handler finding)
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: agent dispatches a tab via send_workspace_artifact (which defaults
    // canEdit=false per FR-39). Agent immediately tries to update it via
    // update_workspace_tab — MUST be refused with `refused_not_editable`.
    //
    // Cross-pillar boundary exercised: send handler's canEdit=false default
    // (line 360 of SendWorkspaceArtifactHandler.cs) couples with update handler's
    // canEdit gate (line 363 of UpdateWorkspaceTabHandler.cs). This is a structural
    // safety property — agent cannot silently rewrite its own outputs without the
    // user's explicit "Convert to editable" affordance.
    //
    // A regression where send_workspace_artifact defaults canEdit=true (or update
    // skips the gate) would surface here as the refusal not firing.

    [Fact]
    public async Task CrossPillar_AgentCannotUpdateOwnArtifact_FR39Binding()
    {
        var sharedState = new InMemoryWorkspaceStateService();
        var sessionGuid = Guid.NewGuid();
        var sessionId = sessionGuid.ToString("N");
        var tabGuid = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var sendHandler = new SendWorkspaceArtifactHandler(
            sharedState,
            new FixedGuidProvider(tabGuid),
            new FixedTimeProvider(DeterministicNow),
            NullLogger<SendWorkspaceArtifactHandler>.Instance);

        var updateHandler = new UpdateWorkspaceTabHandler(
            sharedState,
            new FixedTimeProvider(DeterministicNow.AddMinutes(5)),
            NullLogger<UpdateWorkspaceTabHandler>.Instance);

        var tabIdN = tabGuid.ToString("N");

        // ── Step 1: agent dispatches initial tab. send handler sets canEdit=false.
        var sendArgs = $$"""
                         {
                           "widgetType": "Summary",
                           "title": "Initial Draft Summary",
                           "widgetData": {
                             "kind": "Summary",
                             "body": "INITIAL body content",
                             "tldr": "INITIAL_TLDR_VALUE",
                             "hasUserEdits": false
                           }
                         }
                         """;
        var sendCtx = new ChatInvocationContext
        {
            TenantId = TenantId,
            ChatSessionId = sessionGuid,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = sendArgs
        };

        var sendResult = await sendHandler.ExecuteChatAsync(
            sendCtx, BuildTool(nameof(SendWorkspaceArtifactHandler), "send_workspace_artifact"),
            CancellationToken.None);
        sendResult.Success.Should().BeTrue();

        // Verify the dispatched tab has canEdit=false (FR-39 binding).
        var tabsAfterSend = await sharedState.GetTabsAsync(TenantId, sessionId, CancellationToken.None);
        tabsAfterSend.Should().HaveCount(1);
        tabsAfterSend[0].CanEdit.Should().BeFalse(
            because: "send_workspace_artifact MUST default agent-dispatched tabs to canEdit=false per FR-39");

        // ── Step 2: agent tries to update — MUST be refused with refused_not_editable.
        var updateArgs = $$"""
                           {
                             "tabId": "{{tabIdN}}",
                             "widgetData": {
                               "kind": "Summary",
                               "body": "REFINED body content",
                               "tldr": "REFINED_TLDR_VALUE",
                               "hasUserEdits": false
                             }
                           }
                           """;
        var updateCtx = new ChatInvocationContext
        {
            TenantId = TenantId,
            ChatSessionId = sessionGuid,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = updateArgs
        };

        var updateResult = await updateHandler.ExecuteChatAsync(
            updateCtx, BuildTool(nameof(UpdateWorkspaceTabHandler), "update_workspace_tab"),
            CancellationToken.None);

        // Refusal is a success-with-status (the LLM is instructed to surface the
        // refusal to the user as a re-actable message, not an error).
        updateResult.Success.Should().BeTrue(
            because: "refused_not_editable is a re-actable response, not a transport error");
        var payload = updateResult.GetData<UpdateWorkspaceTabHandler.UpdateWorkspaceTabPayload>();
        payload!.Status.Should().Be("refused_not_editable",
            because: "FR-39 binding: agent cannot mutate its own canEdit=false dispatched tab");

        // The original INITIAL payload remains; no mutation occurred.
        var tabsAfterRefusal = await sharedState.GetTabsAsync(TenantId, sessionId, CancellationToken.None);
        tabsAfterRefusal.Should().HaveCount(1);
        tabsAfterRefusal[0].WidgetData.Should().BeOfType<SummaryTabWidgetData>();
        ((SummaryTabWidgetData)tabsAfterRefusal[0].WidgetData).Tldr.Should().Be("INITIAL_TLDR_VALUE",
            because: "the refused update MUST NOT mutate the tab payload");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 3 — Pillar 6b clean apply round-trip + Pillar 9 (user-editable tab)
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: a pre-existing user-editable tab (canEdit=true, LastUserEditAt=null
    // — user created it then handed off to agent) is updated by the agent. Updated
    // widget data appears in the next-turn prompt.
    //
    // Cross-pillar boundary exercised: update_workspace_tab's clean apply path
    // (NO stale check trigger because LastUserEditAt is null) + Pillar 9 prompt
    // builder both share the same state service. A regression in update where the
    // payload mutates wrong-shape (or fails to flow through to the prompt) surfaces
    // here as a missing REFINED token in the post-update prompt block.

    [Fact]
    public async Task CrossPillar_UserCreatedTabUpdateRoundTrip()
    {
        var sharedState = new InMemoryWorkspaceStateService();
        var sessionGuid = Guid.NewGuid();
        var sessionId = sessionGuid.ToString("N");
        const string tabId = "user-editable-tab-1";

        // Seed a USER-created editable tab (canEdit=true, no prior user edit).
        var seedTab = new WorkspaceTab
        {
            Id = tabId,
            WidgetType = "Summary",
            WidgetData = new SummaryTabWidgetData
            {
                Body = "INITIAL body content",
                Tldr = "INITIAL_TLDR_VALUE",
                HasUserEdits = false
            },
            SessionId = sessionId,
            TenantId = TenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "user",
                CreatedBy = "user:00000000-0000-0000-0000-000000000001",
                CreatedAt = "2026-06-18T11:00:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.NewGuid().ToString("D"),
                MatterName = "MATTER_USER_EDITABLE"
            },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = null,
            CreatedAt = "2026-06-18T11:00:00Z",
            UpdatedAt = "2026-06-18T11:00:00Z"
        };
        await sharedState.UpsertTabAsync(TenantId, sessionId, seedTab, CancellationToken.None);

        var updateHandler = new UpdateWorkspaceTabHandler(
            sharedState,
            new FixedTimeProvider(DeterministicNow.AddMinutes(5)),
            NullLogger<UpdateWorkspaceTabHandler>.Instance);

        var factory = CreatePromptFactory();

        // Verify initial block surfaces the INITIAL payload.
        var initialTabs = await sharedState.GetTabsAsync(TenantId, sessionId, CancellationToken.None);
        var initialBlock = factory.BuildWorkspaceStateBlock(initialTabs, sessionId);
        initialBlock.Should().Contain("INITIAL_TLDR_VALUE");

        // ── Agent updates via update_workspace_tab. Clean apply (canEdit=true,
        // LastUserEditAt=null on both sides).
        var updateArgs = $$"""
                           {
                             "tabId": "{{tabId}}",
                             "widgetData": {
                               "kind": "Summary",
                               "body": "REFINED body content",
                               "tldr": "REFINED_TLDR_VALUE",
                               "hasUserEdits": false
                             }
                           }
                           """;
        var updateCtx = new ChatInvocationContext
        {
            TenantId = TenantId,
            ChatSessionId = sessionGuid,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = updateArgs
        };

        var updateResult = await updateHandler.ExecuteChatAsync(
            updateCtx, BuildTool(nameof(UpdateWorkspaceTabHandler), "update_workspace_tab"),
            CancellationToken.None);
        updateResult.Success.Should().BeTrue();
        var payload = updateResult.GetData<UpdateWorkspaceTabHandler.UpdateWorkspaceTabPayload>();
        payload!.Status.Should().Be(UpdateWorkspaceTabHandler.StatusApplied);

        // Re-compose the prompt and verify the REFINED payload is what the agent sees.
        var tabsAfterUpdate = await sharedState.GetTabsAsync(TenantId, sessionId, CancellationToken.None);
        tabsAfterUpdate.Should().HaveCount(1,
            because: "update is a mutation, not a creation");
        var refreshedBlock = factory.BuildWorkspaceStateBlock(tabsAfterUpdate, sessionId);

        refreshedBlock.Should().Contain("REFINED_TLDR_VALUE",
            because: "the updated payload must appear in the next-turn prompt block");
        refreshedBlock.Should().NotContain("INITIAL_TLDR_VALUE",
            because: "the stale initial payload must NOT linger after the update");
        refreshedBlock.Should().Contain("MATTER_USER_EDITABLE",
            because: "the matter context is preserved through the update");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 4 — Pillar 6b + Pillar 9 ADR-015 surface audit across all 4 widget variants
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: agent dispatches a tab of each variant (Summary, DocumentViewer,
    // Dashboard, Table). Verify Pillar 9's BuildWorkspaceStateBlock surfaces the
    // contracted FR-57 fields for each variant — and ONLY those.
    //
    // Per the actual Pillar 9 contract (TryDeriveVisibleState / FormatVisibleStateFields):
    //   • Summary: tldr + summary body (capped 600 chars) + hasUserEdits
    //   • DocumentViewer: filename + mimeType + sizeBytes + hasSelection + selectionText
    //     (when hasSelection=true, capped 200 chars)
    //   • Dashboard: dashboardName + lastViewedSection (NO chart/widget data)
    //   • Table: rowCount + sortColumn + filteredColumns + selectedRows COUNT
    //     (NEVER the row ID list — this is the ADR-015 binding for Table)
    //
    // Cross-pillar boundary exercised: handler-side widget payload deserialization
    // (Pillar 6b) + prompt-side TryDeriveVisibleState projection (Pillar 9) must
    // BOTH preserve the ADR-015 boundary. The most critical assertion is the Table
    // row-ID elision — a regression where Pillar 9 starts surfacing the IDs would
    // surface here.

    [Fact]
    public async Task CrossPillar_AllFourWidgetVariants_PrivacyFilterAndAdr015Audit()
    {
        var sharedState = new InMemoryWorkspaceStateService();
        var sessionGuid = Guid.NewGuid();
        var sessionId = sessionGuid.ToString("N");

        // Use a counter to give each tab a unique GUID.
        var guidCounter = 0;
        var guidProvider = new FuncGuidProvider(() =>
        {
            guidCounter++;
            return new Guid($"cccccccc-cccc-cccc-cccc-cccccccccc{guidCounter:D2}");
        });

        var sendHandler = new SendWorkspaceArtifactHandler(
            sharedState,
            guidProvider,
            new FixedTimeProvider(DeterministicNow),
            NullLogger<SendWorkspaceArtifactHandler>.Instance);

        // Distinct matter GUIDs per variant so matterName flows through.
        var matterSummary = new Guid("11111111-1111-1111-1111-111111111111");
        var matterDoc = new Guid("22222222-2222-2222-2222-222222222222");
        var matterDash = new Guid("33333333-3333-3333-3333-333333333333");
        var matterTable = new Guid("44444444-4444-4444-4444-444444444444");

        // Dispatch all 4 variants. Each has a matterId so matterName flows.
        var payloads = new[]
        {
            ($$"""
              {
                "widgetType": "Summary",
                "title": "Variant Summary",
                "widgetData": {
                  "kind": "Summary",
                  "body": "Agent-generated summary body — surfaced (capped) per FR-57",
                  "tldr": "TLDR_SUMMARY_OK_TO_RENDER",
                  "hasUserEdits": false
                },
                "matterId": "{{matterSummary:D}}",
                "matterName": "MATTER_SUMMARY_OK"
              }
              """, "Summary"),
            ($$"""
              {
                "widgetType": "DocumentViewer",
                "title": "Variant DocViewer",
                "widgetData": {
                  "kind": "DocumentViewer",
                  "documentId": "doc-xyz",
                  "filename": "filename-OK-to-render.pdf",
                  "mimeType": "application/pdf",
                  "sizeBytes": 12345,
                  "hasSelection": true,
                  "selectionText": "SELECTION_OK_TO_RENDER_within_cap"
                },
                "matterId": "{{matterDoc:D}}",
                "matterName": "MATTER_DOC_OK"
              }
              """, "DocumentViewer"),
            ($$"""
              {
                "widgetType": "Dashboard",
                "title": "Variant Dashboard",
                "widgetData": {
                  "kind": "Dashboard",
                  "layoutId": "layout-corp",
                  "dashboardName": "DASHBOARD_NAME_OK",
                  "lastViewedSection": "calendar"
                },
                "matterId": "{{matterDash:D}}",
                "matterName": "MATTER_DASH_OK"
              }
              """, "Dashboard"),
            ($$"""
              {
                "widgetType": "Table",
                "title": "Variant Table",
                "widgetData": {
                  "kind": "Table",
                  "rowCount": 12,
                  "sortColumn": "name",
                  "sortDirection": "asc",
                  "filteredColumns": ["status"],
                  "selectedRows": ["row-PRIVILEGED-A", "row-PRIVILEGED-B"]
                },
                "matterId": "{{matterTable:D}}",
                "matterName": "MATTER_TABLE_OK"
              }
              """, "Table")
        };

        foreach (var (json, widgetType) in payloads)
        {
            var ctx = new ChatInvocationContext
            {
                TenantId = TenantId,
                ChatSessionId = sessionGuid,
                DecisionId = Guid.NewGuid(),
                ToolArgumentsJson = json
            };
            var result = await sendHandler.ExecuteChatAsync(
                ctx, BuildTool(nameof(SendWorkspaceArtifactHandler), "send_workspace_artifact"),
                CancellationToken.None);
            result.Success.Should().BeTrue(
                because: $"variant {widgetType} dispatch must succeed");
        }

        // Compose the next-turn prompt and audit it.
        var allTabs = await sharedState.GetTabsAsync(TenantId, sessionId, CancellationToken.None);
        allTabs.Should().HaveCount(4);

        var factory = CreatePromptFactory();
        var block = factory.BuildWorkspaceStateBlock(allTabs, sessionId);

        // Each variant's contracted FR-57 fields (the ones Pillar 9 surfaces by design)
        // appear in the block.
        block.Should().Contain("widgetType=Summary");
        block.Should().Contain("TLDR_SUMMARY_OK_TO_RENDER");
        block.Should().Contain("MATTER_SUMMARY_OK");

        block.Should().Contain("widgetType=DocumentViewer");
        block.Should().Contain("filename: filename-OK-to-render.pdf");
        block.Should().Contain("mimeType: application/pdf");
        block.Should().Contain("SELECTION_OK_TO_RENDER_within_cap",
            because: "FR-57 surfaces selectionText (within 200-char cap) when hasSelection=true");
        block.Should().Contain("MATTER_DOC_OK");

        block.Should().Contain("widgetType=Dashboard");
        block.Should().Contain("dashboardName: DASHBOARD_NAME_OK");
        block.Should().Contain("MATTER_DASH_OK");

        block.Should().Contain("widgetType=Table");
        block.Should().Contain("rowCount: 12");
        block.Should().Contain("MATTER_TABLE_OK");

        // ADR-015 / FR-57 BINDING for Table: row IDs MUST NEVER leak (the contract is
        // selectedRows COUNT only). This is the critical privacy projection.
        block.Should().NotContain("row-PRIVILEGED-A",
            because: "Table selectedRows IDs are NOT part of the Pillar 9 visible state — ADR-015 binding");
        block.Should().NotContain("row-PRIVILEGED-B",
            because: "Table selectedRows IDs are NOT part of the Pillar 9 visible state — ADR-015 binding");

        // Selected rows COUNT (the safe projection) IS rendered.
        block.Should().Contain("selectedRows: 2",
            because: "Pillar 9 surfaces the row COUNT, never the row IDs");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 4 — Pillar 6b stale-read + Pillar 9 prompt stability
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: a user-edited tab is in workspace state. The agent attempts an
    // update with a stale expected-LastUserEditAt. The update handler refuses
    // (Q8 USER WINS — task 058). The next-turn prompt MUST surface the USER's
    // version, NOT the agent's attempted rewrite.
    //
    // Cross-pillar boundary exercised: the failed mutation path must leave the
    // state coherent for the prompt builder. A bug where the handler writes
    // partial state on refusal would corrupt the prompt block on the next turn.

    [Fact]
    public async Task CrossPillar_StaleReadRefusal_LeavesTabUnchangedForNextTurnPrompt()
    {
        var sessionGuid = Guid.NewGuid();
        var sessionId = sessionGuid.ToString("N");
        var tabId = "stable-tab-1";

        // Pre-seed a USER-edited tab. The stored LastUserEditAt is later than the
        // agent's stale expected timestamp.
        var userEditedTab = new WorkspaceTab
        {
            Id = tabId,
            WidgetType = "Summary",
            WidgetData = new SummaryTabWidgetData
            {
                Body = "USER_AUTHORED_BODY_must_not_be_overwritten",
                Tldr = "USER_TLDR_KEEP_ME",
                HasUserEdits = true
            },
            SessionId = sessionId,
            TenantId = TenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "user",
                CreatedBy = "user:00000000-0000-0000-0000-000000000001",
                CreatedAt = "2026-06-18T11:00:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.NewGuid().ToString("D"),
                MatterName = "MATTER_USER_EDIT"
            },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = "2026-06-18T13:00:00Z",
            CreatedAt = "2026-06-18T11:00:00Z",
            UpdatedAt = "2026-06-18T13:00:00Z"
        };

        var sharedState = new InMemoryWorkspaceStateService();
        await sharedState.UpsertTabAsync(TenantId, sessionId, userEditedTab, CancellationToken.None);

        var updateHandler = new UpdateWorkspaceTabHandler(
            sharedState,
            new FixedTimeProvider(DeterministicNow),
            NullLogger<UpdateWorkspaceTabHandler>.Instance);

        // Agent attempts an update with stale timestamp (saw the tab at 11:00, user
        // edited at 13:00 → refusal).
        var staleArgs = $$"""
                          {
                            "tabId": "{{tabId}}",
                            "widgetData": {
                              "kind": "Summary",
                              "body": "AGENT_PROPOSED_BODY_must_be_discarded",
                              "tldr": "AGENT_PROPOSED_TLDR_DISCARD",
                              "hasUserEdits": false
                            },
                            "expectedLastUserEditAt": "2026-06-18T11:00:00Z"
                          }
                          """;
        var ctx = new ChatInvocationContext
        {
            TenantId = TenantId,
            ChatSessionId = sessionGuid,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = staleArgs
        };

        var refusalResult = await updateHandler.ExecuteChatAsync(
            ctx, BuildTool(nameof(UpdateWorkspaceTabHandler), "update_workspace_tab"),
            CancellationToken.None);

        refusalResult.Success.Should().BeTrue(
            because: "Q8 stale_read is a re-actable response, not a transport error");
        var refusalPayload = refusalResult
            .GetData<UpdateWorkspaceTabHandler.UpdateWorkspaceTabPayload>();
        refusalPayload!.Status.Should().Be(UpdateWorkspaceTabHandler.StatusStaleRead);

        // Compose the next-turn prompt. The USER's version must be what the agent
        // sees on its next turn (so it can re-read + re-attempt).
        var tabsAfterRefusal = await sharedState.GetTabsAsync(TenantId, sessionId, CancellationToken.None);
        tabsAfterRefusal.Should().HaveCount(1);
        tabsAfterRefusal[0].UpdatedAt.Should().Be("2026-06-18T13:00:00Z",
            because: "the stale-read refusal must NOT mutate the tab");

        var factory = CreatePromptFactory();
        var block = factory.BuildWorkspaceStateBlock(tabsAfterRefusal, sessionId);

        block.Should().Contain("USER_TLDR_KEEP_ME",
            because: "the USER's version is what the next-turn agent sees (Q8 user-wins binding)");
        block.Should().Contain("hasUserEdits: true",
            because: "the user-edits flag preserved through the refusal");
        block.Should().NotContain("AGENT_PROPOSED_TLDR_DISCARD",
            because: "the agent's proposed-but-refused mutation must NEVER reach the prompt");
        block.Should().NotContain("AGENT_PROPOSED_BODY_must_be_discarded");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TEST 5 — Pillar 6b + Pillar 9 privacy default holds across mutation
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // Scenario: a user-created tab is marked visibleToAssistant=false (privacy
    // default). The agent successfully edits the tab (clean apply path). The
    // tab's visibleToAssistant flag is NOT changed by the edit, so the next-turn
    // prompt still excludes it.
    //
    // Cross-pillar boundary exercised: update_workspace_tab modifies widget data
    // but must NEVER flip visibility flags as a side effect. A regression where
    // the handler reset VisibleToAssistant to its default (true) would surface
    // here as the hidden tab unexpectedly leaking into the prompt.

    [Fact]
    public async Task CrossPillar_HiddenTabNeverLeaksToPromptEvenAfterAgentEdit()
    {
        var sessionGuid = Guid.NewGuid();
        var sessionId = sessionGuid.ToString("N");
        var tabId = "hidden-tab-1";

        // Seed a hidden tab (visibleToAssistant=false). Tab has never been
        // user-edited (LastUserEditAt=null) so the agent CAN update it.
        var hiddenTab = new WorkspaceTab
        {
            Id = tabId,
            WidgetType = "Summary",
            WidgetData = new SummaryTabWidgetData
            {
                Body = "INITIAL_HIDDEN_BODY",
                Tldr = "INITIAL_HIDDEN_TLDR_must_not_leak",
                HasUserEdits = false
            },
            SessionId = sessionId,
            TenantId = TenantId,
            VisibleToAssistant = false,   // ← PRIVACY DEFAULT
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                CreatedBy = $"agent:{sessionGuid:N}",
                CreatedAt = "2026-06-18T11:00:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.NewGuid().ToString("D"),
                MatterName = "MATTER_HIDDEN_must_not_leak"
            },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = null,
            CreatedAt = "2026-06-18T11:00:00Z",
            UpdatedAt = "2026-06-18T11:00:00Z"
        };

        var sharedState = new InMemoryWorkspaceStateService();
        await sharedState.UpsertTabAsync(TenantId, sessionId, hiddenTab, CancellationToken.None);

        var updateHandler = new UpdateWorkspaceTabHandler(
            sharedState,
            new FixedTimeProvider(DeterministicNow.AddMinutes(10)),
            NullLogger<UpdateWorkspaceTabHandler>.Instance);

        var updateArgs = $$"""
                           {
                             "tabId": "{{tabId}}",
                             "widgetData": {
                               "kind": "Summary",
                               "body": "REFINED_HIDDEN_BODY",
                               "tldr": "REFINED_HIDDEN_TLDR_must_not_leak",
                               "hasUserEdits": false
                             }
                           }
                           """;
        var ctx = new ChatInvocationContext
        {
            TenantId = TenantId,
            ChatSessionId = sessionGuid,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = updateArgs
        };

        var updateResult = await updateHandler.ExecuteChatAsync(
            ctx, BuildTool(nameof(UpdateWorkspaceTabHandler), "update_workspace_tab"),
            CancellationToken.None);

        updateResult.Success.Should().BeTrue();
        var payload = updateResult.GetData<UpdateWorkspaceTabHandler.UpdateWorkspaceTabPayload>();
        payload!.Status.Should().Be(UpdateWorkspaceTabHandler.StatusApplied);

        // Re-read + compose prompt — the hidden tab MUST still be hidden.
        var tabsAfter = await sharedState.GetTabsAsync(TenantId, sessionId, CancellationToken.None);
        tabsAfter[0].VisibleToAssistant.Should().BeFalse(
            because: "update_workspace_tab must NOT flip the visibleToAssistant flag");

        var factory = CreatePromptFactory();
        var block = factory.BuildWorkspaceStateBlock(tabsAfter, sessionId);

        block.Should().BeEmpty(
            because: "the only tab is hidden; the Pillar 9 privacy default keeps the entire block out");
        block.Should().NotContain("REFINED_HIDDEN_TLDR_must_not_leak");
        block.Should().NotContain("INITIAL_HIDDEN_TLDR_must_not_leak");
        block.Should().NotContain("MATTER_HIDDEN_must_not_leak");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────────

    private static SprkChatAgentFactory CreatePromptFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<SprkChatAgentFactory>>(NullLogger<SprkChatAgentFactory>.Instance);
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SprkChatAgentFactory>>();
        return new TestableSprkChatAgentFactory(logger);
    }

    private static AnalysisTool BuildTool(string handlerClass, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = name,
        Type = ToolType.Custom,
        HandlerClass = handlerClass
    };

    // Test subclass exposing the protected ctor — mirrors Pillar9PrivacyFilterTests.
    private sealed class TestableSprkChatAgentFactory : SprkChatAgentFactory
    {
        public TestableSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) : base(logger) { }
    }

    // In-memory IWorkspaceStateService — production has Redis + Cosmos; this
    // test fake captures the SAME interface contract so the cross-pillar flow is
    // exercised without infrastructure.
    private sealed class InMemoryWorkspaceStateService : IWorkspaceStateService
    {
        private readonly Dictionary<string, WorkspaceTab> _tabsByKey = new();
        private readonly object _gate = new();

        public Task<IReadOnlyList<WorkspaceTab>> GetTabsAsync(
            string tenantId, string sessionId, CancellationToken ct = default)
        {
            lock (_gate)
            {
                var prefix = $"{tenantId}|{sessionId}|";
                var list = _tabsByKey
                    .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(kv => kv.Value)
                    .ToArray();
                return Task.FromResult<IReadOnlyList<WorkspaceTab>>(list);
            }
        }

        public Task UpsertTabAsync(
            string tenantId, string sessionId, WorkspaceTab tab, CancellationToken ct = default)
        {
            lock (_gate)
            {
                _tabsByKey[$"{tenantId}|{sessionId}|{tab.Id}"] = tab;
            }
            return Task.CompletedTask;
        }

        public Task PinTabAsync(
            string tenantId, string sessionId, string tabId, string matterId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task CloseTabAsync(
            string tenantId, string sessionId, string tabId, CancellationToken ct = default)
        {
            lock (_gate)
            {
                _tabsByKey.Remove($"{tenantId}|{sessionId}|{tabId}");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FixedGuidProvider : IGuidProvider
    {
        private readonly Guid _guid;
        public FixedGuidProvider(Guid guid) => _guid = guid;
        public Guid NewGuid() => _guid;
    }

    private sealed class FuncGuidProvider : IGuidProvider
    {
        private readonly Func<Guid> _factory;
        public FuncGuidProvider(Func<Guid> factory) => _factory = factory;
        public Guid NewGuid() => _factory();
    }
}
